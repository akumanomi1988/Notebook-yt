using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class MediaStorage(IConfiguration configuration)
{
    public string Root { get; } = Path.GetFullPath(configuration["Storage:Root"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookYt", "storage"));
    public string Resolve(string key)
    {
        if (key.Length != 69 || !key.EndsWith(".blob", StringComparison.Ordinal) || !key[..64].All(char.IsAsciiHexDigit)) throw new StudioException(400, "Referencia de almacenamiento inválida.");
        return Path.Combine(Root, key);
    }
    public bool Exists(string key) => File.Exists(Resolve(key));
    public async Task<(string Key, string Hash, long Length)> Save(Stream input, long maximum, CancellationToken ct)
    {
        Directory.CreateDirectory(Root); var temporary = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            long size = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                var buffer = new byte[1024 * 1024]; int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    size += count; if (size > maximum) throw new StudioException(413, "El archivo supera el tamaño permitido.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (size == 0) throw new StudioException(400, "El archivo está vacío.");
                await output.FlushAsync(ct); output.Flush(true);
            }
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset()); var key = digest + ".blob";
            if (!File.Exists(Resolve(key))) File.Move(temporary, Resolve(key));
            return (key, digest, size);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<JsonElement> Probe(string path, CancellationToken ct, bool videoOnly = false)
    {
        var args = new List<string> { "-v", "error", "-protocol_whitelist", "file,pipe" };
        if (videoOnly) args.AddRange(["-f", "mov"]);
        args.AddRange(["-show_format", "-show_streams", "-of", "json", path]);
        return JsonDocument.Parse(await Run("ffprobe", args, ct)).RootElement.Clone();
    }

    public async Task Inspect(MediaArtifact artifact, CancellationToken ct)
    {
        var path = Resolve(artifact.StorageKey);
        await using (var stream = File.OpenRead(path))
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) != artifact.Sha256) throw new StudioException(409, "El checksum del archivo no coincide.");
        if (artifact.Kind == "Thumbnail")
        {
            await using var file = File.OpenRead(path); var magic = new byte[8];
            if (file.Length < 8) throw new StudioException(400, "Imagen incompleta.");
            await file.ReadExactlyAsync(magic, ct);
            if (!(magic[0] == 0xff && magic[1] == 0xd8 && magic[2] == 0xff) && !magic.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new StudioException(400, "Se requiere una imagen JPEG o PNG real.");
        }
        var data = await Probe(path, ct, artifact.Kind == "Video"); var streams = data.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new StudioException(400, "El archivo no contiene vídeo o imagen.");
        artifact.Width = video.GetProperty("width").GetInt32(); artifact.Height = video.GetProperty("height").GetInt32(); artifact.VideoCodec = video.GetProperty("codec_name").GetString() ?? "";
        artifact.Container = data.GetProperty("format").GetProperty("format_name").GetString() ?? "";
        if (artifact.Kind == "Thumbnail")
        {
            if (artifact.VideoCodec is not ("mjpeg" or "png") || artifact.Width < 640 || artifact.Height < 360 || artifact.SizeBytes > 2 * 1024 * 1024 || Math.Abs((double)artifact.Width / artifact.Height - 16.0 / 9) > 0.02) throw new StudioException(400, "La miniatura debe ser JPEG/PNG, 16:9, al menos 640×360 y hasta 2 MB.");
            var actualMime = artifact.VideoCodec == "png" ? "image/png" : "image/jpeg";
            if (artifact.MimeType != actualMime) throw new StudioException(400, "La imagen no coincide con el tipo MIME declarado.");
            artifact.MimeType = actualMime;
        }
        else
        {
            if (!artifact.Container.Split(',').Contains("mp4") || artifact.VideoCodec is not ("h264" or "hevc" or "av1" or "vp9")) throw new StudioException(400, "Se requiere vídeo MP4 con códec compatible.");
            artifact.DurationSeconds = double.Parse(data.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            artifact.HasAudio = streams.Any(s => s.GetProperty("codec_type").GetString() == "audio");
            if (!double.IsFinite(artifact.DurationSeconds) || artifact.DurationSeconds <= 0 || !artifact.HasAudio) throw new StudioException(400, "El vídeo debe tener duración válida y audio.");
            await Run("ffmpeg", ["-v", "error", "-xerror", "-protocol_whitelist", "file,pipe", "-f", "mov", "-i", path, "-f", "null", "-"], ct);
            artifact.MimeType = "video/mp4";
        }
        artifact.Status = "Valid"; artifact.ValidationError = "";
    }

    public async Task<string> Run(string tool, IEnumerable<string> arguments, CancellationToken ct)
    {
        var executable = configuration[$"Media:{tool}Path"] ?? tool;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new StudioException(503, "No se pudo iniciar la herramienta multimedia.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var result = await output; var diagnostic = await error;
        if (process.ExitCode != 0) throw new StudioException(400, "Inspección multimedia fallida. Comprueba que el archivo esté completo y sea compatible.");
        return result;
    }
}

public static class MediaEndpoints
{
    public static void MapMedia(this WebApplication app)
    {
        app.MapPost("/api/studio/productions/{id:guid}/artifacts", async (Guid id, string kind, string name, string? evidence, bool? attested, HttpRequest request, EditorialDb db, MediaStorage storage, CancellationToken ct) =>
        {
            await StudioRules.Editable(db, id); await StudioRules.Workspace(db, id); StudioRules.Choice(kind, "Video", "Document", "Thumbnail");
            var safeName = StudioRules.Text(name, 200, true);
            if (safeName.Any(char.IsControl)) throw new StudioException(400, "El nombre del archivo contiene caracteres de control.");
            safeName = Path.GetFileName(safeName.Replace('\\', '/'));
            if (safeName is "" or "." or "..") throw new StudioException(400, "El nombre del archivo no es válido.");
            if (kind == "Video" && attested == true && (string.IsNullOrWhiteSpace(evidence) || !await db.NotebookReferences.AnyAsync(n => n.ProductionId == id))) throw new StudioException(409, "Registra el notebook y declara autorización y procedencia del vídeo original.");
            var allowed = kind switch { "Video" => new[] { "video/mp4" }, "Thumbnail" => ["image/jpeg", "image/png"], _ => ["application/pdf", "text/plain", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"] };
            if (!allowed.Contains(request.ContentType?.Split(';')[0])) throw new StudioException(415, "Tipo de archivo no permitido.");
            var limit = kind == "Video" ? 1024L * 1024 * 1024 : kind == "Thumbnail" ? 2L * 1024 * 1024 : 20L * 1024 * 1024;
            var saved = await storage.Save(request.Body, limit, ct);
            if (kind == "Document")
            {
                await using var file = File.OpenRead(storage.Resolve(saved.Key)); var magic = new byte[Math.Min(5, saved.Length)]; await file.ReadExactlyAsync(magic, ct);
                if ((request.ContentType?.Split(';')[0] == "application/pdf" && Encoding.ASCII.GetString(magic) != "%PDF-") || (request.ContentType?.Contains("wordprocessingml") == true && (magic.Length < 2 || magic[0] != 'P' || magic[1] != 'K'))) throw new StudioException(400, "El documento no coincide con su tipo declarado.");
            }
            var existing = await db.Set<MediaArtifact>().SingleOrDefaultAsync(a => a.ProductionId == id && a.Kind == kind && a.Sha256 == saved.Hash);
            if (existing is not null) return Results.Ok(existing);
            var artifact = new MediaArtifact { ProductionId = id, Kind = kind, OriginalName = safeName, StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length,
                MimeType = request.ContentType!.Split(';')[0], Origin = kind == "Video" ? attested == true ? "OperatorAttested" : "Unverified" : "OperatorProvided", OriginEvidence = StudioRules.Text(evidence ?? "", 4000), Status = kind == "Document" ? "Valid" : "PendingInspection" };
            db.Add(artifact); if (kind != "Document") await StudioRules.Enqueue(db, "InspectArtifact", id, artifact.Id, "inspect:" + artifact.Id);
            StudioRules.Event(db, id, "ArtifactImported", $"{artifact.Id}: {artifact.Origin}; SHA256 {saved.Hash}"); await db.SaveChangesAsync(); return Results.Created($"/api/studio/artifacts/{artifact.Id}/file", artifact);
        }).DisableAntiforgery();
        app.MapPost("/api/studio/artifacts/{id:guid}/attest", async (Guid id, TaskCompletion input, EditorialDb db) =>
        {
            var artifact = await db.Set<MediaArtifact>().FindAsync(id) ?? throw new StudioException(404, "Archivo inexistente."); await StudioRules.Editable(db, artifact.ProductionId);
            if (artifact.Kind != "Video" || artifact.Status != "Valid" || !input.Attested || !await db.NotebookReferences.AnyAsync(n => n.ProductionId == artifact.ProductionId)) throw new StudioException(409, "Se requiere un vídeo válido, un notebook asociado y una declaración explícita de procedencia y autorización.");
            if (artifact.Origin == "OperatorAttested") return Results.Ok(artifact);
            artifact.OriginEvidence = StudioRules.Text(input.Evidence, 4000, true); artifact.Origin = "OperatorAttested";
            StudioRules.Event(db, artifact.ProductionId, "ArtifactOriginAttestedByOperator", $"{artifact.Id}; SHA256 {artifact.Sha256}: {artifact.OriginEvidence}"); await db.SaveChangesAsync(); return Results.Ok(artifact);
        });
        app.MapGet("/api/studio/artifacts/{id:guid}/file", async (Guid id, EditorialDb db, MediaStorage storage) =>
        {
            var artifact = await db.Set<MediaArtifact>().FindAsync(id) ?? throw new StudioException(404, "Archivo inexistente.");
            if (artifact.Status != "Valid" || !storage.Exists(artifact.StorageKey)) throw new StudioException(409, "El archivo no está disponible o no ha superado la inspección.");
            return Results.File(storage.Resolve(artifact.StorageKey), artifact.MimeType, fileDownloadName: artifact.Kind == "Document" ? artifact.OriginalName : null, enableRangeProcessing: true);
        });
        app.MapPost("/api/studio/artifacts/{id:guid}/thumbnail", async (Guid id, ThumbnailInput input, EditorialDb db, MediaStorage storage, CancellationToken ct) =>
        {
            var source = await db.Set<MediaArtifact>().FindAsync(id) ?? throw new StudioException(404, "Vídeo inexistente."); await StudioRules.Editable(db, source.ProductionId);
            if (source.Kind != "Video" || source.Status != "Valid" || source.Origin != "OperatorAttested" || input.Seconds < 0 || input.Seconds >= source.DurationSeconds || !double.IsFinite(input.Seconds)) throw new StudioException(400, "Selecciona un instante válido de un vídeo NotebookLM inspeccionado.");
            if (!double.IsFinite(input.CropX) || !double.IsFinite(input.CropY) || input.CropX is < 0 or > 1 || input.CropY is < 0 or > 1) throw new StudioException(400, "El foco del recorte debe estar entre 0 y 1.");
            var overlay = StudioRules.Text(input.Text ?? "", 120);
            var temporary = Path.Combine(storage.Root, Guid.NewGuid().ToString("N") + ".jpg");
            var textFile = Path.Combine(storage.Root, Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                var filter = $"scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720:(iw-ow)*{input.CropX.ToString(CultureInfo.InvariantCulture)}:(ih-oh)*{input.CropY.ToString(CultureInfo.InvariantCulture)}";
                if (overlay.Length > 0)
                {
                    await File.WriteAllTextAsync(textFile, overlay, new UTF8Encoding(false), ct);
                    var font = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf") : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
                    static string Escape(string value) => value.Replace('\\', '/').Replace(":", "\\:").Replace("'", "\\'");
                    filter += $",drawtext=fontfile='{Escape(font)}':textfile='{Escape(textFile)}':fontcolor=white:fontsize=48:x=(w-text_w)/2:y=h-text_h-40:box=1:boxcolor=black@0.7:boxborderw=16";
                }
                await storage.Run("ffmpeg", ["-v", "error", "-ss", input.Seconds.ToString(CultureInfo.InvariantCulture), "-protocol_whitelist", "file,pipe", "-f", "mov", "-i", storage.Resolve(source.StorageKey), "-frames:v", "1", "-vf", filter, "-q:v", "3", temporary], ct);
                await using var file = File.OpenRead(temporary); var saved = await storage.Save(file, 2 * 1024 * 1024, ct);
                var existing = await db.Set<MediaArtifact>().SingleOrDefaultAsync(a => a.ProductionId == source.ProductionId && a.Kind == "Thumbnail" && a.Sha256 == saved.Hash); if (existing is not null) return Results.Ok(existing);
                var artifact = new MediaArtifact { ProductionId = source.ProductionId, Kind = "Thumbnail", OriginalName = "notebook-frame.jpg", StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length, Origin = "DerivedNotebookFrame", OriginEvidence = $"{source.Id} at {input.Seconds}s", MimeType = "image/jpeg" };
                await storage.Inspect(artifact, ct); db.Add(artifact); StudioRules.Event(db, source.ProductionId, "ThumbnailExtracted", artifact.OriginEvidence); await db.SaveChangesAsync(); return Results.Ok(artifact);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(textFile)) File.Delete(textFile); }
        });
        app.MapGet("/api/studio/storage", async (EditorialDb db, MediaStorage storage) =>
        {
            var artifacts = await db.Set<MediaArtifact>().ToListAsync(); var keys = artifacts.Select(a => a.StorageKey).ToHashSet(); Directory.CreateDirectory(storage.Root);
            return Results.Ok(new { root = storage.Root, missing = artifacts.Where(a => !storage.Exists(a.StorageKey)).Select(a => a.Id), orphanFiles = Directory.EnumerateFiles(storage.Root, "*.blob").Count(p => !keys.Contains(Path.GetFileName(p))), partialFiles = Directory.EnumerateFiles(storage.Root, "*.partial").Count() });
        });
        app.MapPost("/api/studio/storage/cleanup", async (EditorialDb db, MediaStorage storage) =>
        {
            var keys = (await db.Set<MediaArtifact>().Select(a => a.StorageKey).ToListAsync()).ToHashSet(); var removed = 0; Directory.CreateDirectory(storage.Root);
            var retentionDays = await db.Set<ChannelSettings>().Select(s => (int?)s.RetentionDays).SingleOrDefaultAsync() ?? 90;
            // Never remove a referenced artifact. Only old staging and unreferenced blobs are eligible.
            foreach (var file in Directory.EnumerateFiles(storage.Root))
                if ((file.EndsWith(".partial") || file.EndsWith(".blob")) && !keys.Contains(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-retentionDays)) { File.Delete(file); removed++; }
            StudioRules.Event(db, null, "StorageCleanup", removed.ToString()); await db.SaveChangesAsync(); return Results.Ok(new { removed });
        });
    }
}
public record ThumbnailInput(double Seconds, double CropX = 0.5, double CropY = 0.5, string? Text = null);
