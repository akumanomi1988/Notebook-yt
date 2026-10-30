using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class StudioException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public static class StudioRules
{
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Text(string? value, int maximum = 12000, bool required = false)
    {
        if (value is null || value.Length > maximum || (required && string.IsNullOrWhiteSpace(value)))
            throw new StudioException(400, "El campo es obligatorio o supera su longitud permitida.");
        return value.Trim();
    }
    public static string Choice(string? value, params string[] allowed) => allowed.Contains(value) ? value! : throw new StudioException(400, "Valor no permitido.");
    public static void ValidatePublicationText(string title, string description, string tags)
    {
        if (title.Length > 100 || title.IndexOfAny(['<', '>']) >= 0 || description.IndexOfAny(['<', '>']) >= 0 || Encoding.UTF8.GetByteCount(description) > 5000)
            throw new StudioException(400, "YouTube admite un título de hasta 100 caracteres y una descripción de hasta 5000 bytes UTF-8; no admite los caracteres < o >.");
        var entries = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var length = entries.Sum(tag => tag.Length + (tag.Contains(' ') ? 2 : 0)) + Math.Max(0, entries.Length - 1);
        if (length > 500) throw new StudioException(400, "Las etiquetas superan 500 caracteres contando separadores y comillas de etiquetas con espacios.");
    }
    public static string PublicUrl(string? value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new StudioException(400, "Indica una URL pública HTTPS válida, sin credenciales.");
        // URLs are registered as citations, never fetched by this application.
        return uri.AbsoluteUri;
    }
    public static async Task<Production> Editable(EditorialDb db, Guid id)
    {
        var p = await db.Productions.FindAsync(id) ?? throw new StudioException(404, "Producción inexistente.");
        if (p.Status == ProductionStatus.Cancelled) throw new StudioException(409, "La producción está cancelada.");
        if (await db.Set<YouTubeUpload>().AnyAsync(u => u.ProductionId == id)) throw new StudioException(409, "La producción tiene una subida registrada; sus datos están congelados.");
        return p;
    }
    public static async Task<ProductionWorkspace> Workspace(EditorialDb db, Guid id) => await db.Set<ProductionWorkspace>().FindAsync(id) ?? throw new StudioException(404, "Abre primero el espacio de trabajo de esta producción.");
    public static async Task EditableJob(EditorialDb db, Guid id)
    {
        var p = await db.Productions.FindAsync(id) ?? throw new StudioException(404, "Producción inexistente.");
        if (p.Status == ProductionStatus.Cancelled) throw new StudioException(409, "La producción está cancelada.");
    }
    public static void Event(EditorialDb db, Guid? productionId, string kind, string detail) => db.Add(new StudioEvent { ProductionId = productionId, Kind = kind, Detail = detail });
    public static async Task Enqueue(EditorialDb db, string kind, Guid? productionId, Guid? resourceId, string key)
    {
        if (!await db.Set<BackgroundJob>().AnyAsync(j => j.IdempotencyKey == key))
            db.Add(new BackgroundJob { Kind = kind, ProductionId = productionId, ResourceId = resourceId, IdempotencyKey = key });
    }
    public static readonly (string Kind, string Instruction, string Expected)[] ManualSteps = [
        ("CreateNotebook", "Crea un notebook privado en la interfaz oficial de NotebookLM y registra aquí su URL.", "Notebook dedicado y URL registrada"),
        ("AddNotebookSources", "Transfiere el paquete y todas las fuentes incluidas al notebook. Comprueba que se hayan procesado correctamente.", "Fuentes incorporadas y revisadas por el operador"),
        ("ConfigureVideoOverview", "Abre Studio / Video Overview. Selecciona únicamente los formatos, estilos e idioma disponibles en tu cuenta. Copia las instrucciones editoriales del paquete.", "Configuración anotada por el operador"),
        ("ReviewProducerNotes", "Si tu cuenta ofrece notas de productor, revísalas y apruébalas en NotebookLM. En otro caso indica explícitamente que esta opción no está disponible.", "Revisión o justificación de no aplicabilidad"),
        ("StartVideoGeneration", "Inicia Video Overview en NotebookLM. Este botón solo registra tu declaración; Notebook-yt no inicia ni controla la generación.", "Solicitud de generación declarada por el operador"),
        ("ConfirmVideoGeneration", "Espera a que NotebookLM muestre el vídeo terminado. Reprodúcelo y registra la evidencia. No confirmes basándote solo en el tiempo transcurrido.", "Vídeo reproducible observado en NotebookLM"),
        ("DownloadNotebookVideo", "Descarga el vídeo desde el control oficial Download de NotebookLM, respetando los permisos y restricciones de sus fuentes.", "Archivo original descargado con autorización"),
        ("ImportNotebookVideo", "Importa el archivo original descargado, declara su procedencia y espera la inspección técnica. La importación no prueba criptográficamente su origen.", "Archivo real importado e inspeccionado")
    ];
    public static async Task<string> NextAction(EditorialDb db, Production p, ProductionWorkspace? w)
    {
        if (p.Status == ProductionStatus.Cancelled) return "Cancelada: no admite cambios";
        if (w is null) return "Abrir espacio de trabajo";
        var upload = await db.Set<YouTubeUpload>().SingleOrDefaultAsync(x => x.ProductionId == p.Id);
        if (upload is not null) return upload.Status switch { "Published" => "Publicación confirmada por YouTube", "NeedsAttention" => "Resolver la subida sin crear otra", "Scheduled" => "Esperar y verificar publicación", "Uploaded" => "Verificar estado en YouTube", _ => "Consultar trabajo de subida" };
        if (!await db.Set<SourcePackage>().AnyAsync(x => x.ProductionId == p.Id)) return "Preparar fuentes y congelar paquete";
        var tasks = await db.Set<ManualNotebookTask>().Where(t => t.ProductionId == p.Id).ToListAsync();
        foreach (var step in ManualSteps)
            if (!tasks.Any(t => t.Kind == step.Kind && t.Status == "Completed")) return step.Instruction;
        if (w.CanonicalArtifactId is null) return "Seleccionar vídeo NotebookLM inspeccionado";
        var settings = await db.Set<ChannelSettings>().FindAsync(p.ChannelId);
        if (w.ApprovedAtUtc is null && settings?.RequireEditorialApproval != false) return "Completar metadatos y revisión editorial";
        if (!await db.Set<GoogleConnection>().AnyAsync(c => c.Kind == "YouTube" && c.Status == "Connected")) return "Conectar YouTube mediante OAuth";
        return "Aprobar y encolar subida a YouTube";
    }
    public static DateTime ResolveLocalTime(DateOnly date, TimeOnly time, string zone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // Shift a nonexistent local time forward; choose the later occurrence of an ambiguous time.
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (tz.IsAmbiguousTime(local)) return new DateTimeOffset(local, tz.GetAmbiguousTimeOffsets(local).Min()).UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }
}
