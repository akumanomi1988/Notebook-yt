using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class NotebookSourceBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResearchSourceId { get; set; }
    public string ExternalSourceId { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime? VerifiedAtUtc { get; set; }
}

public sealed class NotebookIntegration(EditorialDb db, GoogleAuthorization google, IConfiguration configuration, MediaStorage storage)
{
    private (string BaseUrl, string Region, string Project) Settings()
    {
        var region = configuration["NotebookEnterprise:Region"] ?? "global"; var project = configuration["NotebookEnterprise:ProjectNumber"] ?? "";
        if (region is not ("global" or "us" or "eu") || !Regex.IsMatch(project, "^[0-9]{3,30}$")) throw new StudioException(409, "Configura el proyecto numérico y la región de Notebook Enterprise.");
        return ($"https://{region}-discoveryengine.googleapis.com/v1alpha/projects/{project}/locations/{region}/notebooks", region, project);
    }
    public async Task CheckAccess()
    {
        var channel = await db.Channels.SingleAsync(); var settings = await db.Set<ChannelSettings>().FindAsync(channel.Id);
        if (settings?.NotebookProduct != "Enterprise" || !settings.EnterpriseLicenseAttested) throw new StudioException(409, "Se necesita una licencia Enterprise declarada y una cuenta OAuth autorizada. Usa el procedimiento manual si no dispones de ella.");
        Settings();
        if (!await db.Set<GoogleConnection>().AnyAsync(c => c.Kind == "NotebookEnterprise" && c.Status == "Connected")) throw new StudioException(409, "Conecta Notebook Enterprise con OAuth.");
    }
    private async Task<string> NotebookId(Guid productionId)
    {
        var reference = await db.NotebookReferences.SingleOrDefaultAsync(n => n.ProductionId == productionId) ?? throw new StudioException(409, "Falta el notebook.");
        var settings = Settings(); var uri = new Uri(reference.Url);
        if (uri.Host != "notebook.cloud.google.com" || !uri.AbsolutePath.StartsWith($"/{settings.Region}/notebook/")) throw new StudioException(409, "El notebook no pertenece a la región Enterprise configurada.");
        var id = uri.AbsolutePath.TrimEnd('/').Split('/').Last(); if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]+$")) throw new StudioException(400, "Identificador de notebook inválido."); return id;
    }
    public async Task Create(BackgroundJob job, CancellationToken ct)
    {
        await CheckAccess(); var id = job.ProductionId!.Value;
        if (await db.NotebookReferences.AnyAsync(n => n.ProductionId == id, ct)) return;
        var p = await db.Productions.FindAsync([id], ct); var w = await StudioRules.Workspace(db, id);
        var settings = Settings(); w.NotebookCreationMethod = "ApiRequestPending"; await db.SaveChangesAsync(ct);
        using var response = await google.Send(HttpMethod.Post, settings.BaseUrl, "NotebookEnterprise", JsonContent.Create(new { title = p!.Topic }), ct);
        if (!response.IsSuccessStatusCode) throw new StudioException(409, "Notebook Enterprise rechazó la creación. Revisa la licencia, IAM y las cuotas; concilia el resultado antes de repetir.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct); var external = body.GetProperty("notebookId").GetString()!;
        if (!Regex.IsMatch(external, "^[A-Za-z0-9_-]+$")) throw new StudioException(502, "La respuesta de Google no contiene un notebook válido.");
        db.Add(new NotebookReference { ProductionId = id, Url = $"https://notebook.cloud.google.com/{settings.Region}/notebook/{external}?project={settings.Project}" });
        w.NotebookCreationMethod = "OfficialEnterpriseApi"; w.NotebookVerifiedAtUtc = DateTime.UtcNow; w.Stage = "NotebookReady";
        var task = await db.Set<ManualNotebookTask>().SingleOrDefaultAsync(t => t.ProductionId == id && t.Kind == "CreateNotebook", ct);
        if (task is not null) { task.Status = "Completed"; task.EvidenceKind = "ProviderVerified"; task.Evidence = external; task.CompletedAtUtc = DateTime.UtcNow; }
        StudioRules.Event(db, id, "NotebookCreatedByOfficialApi", external); await db.SaveChangesAsync(ct);
    }
    public async Task Verify(Guid id, CancellationToken ct)
    {
        await CheckAccess(); var external = await NotebookId(id);
        using var response = await google.Send(HttpMethod.Get, Settings().BaseUrl + "/" + external, "NotebookEnterprise", null, ct);
        if (!response.IsSuccessStatusCode) throw new StudioException(409, "Google no confirmó el notebook o sus permisos.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (body.GetProperty("notebookId").GetString() != external) throw new StudioException(502, "Google devolvió un notebook distinto.");
        var w = await StudioRules.Workspace(db, id); w.NotebookVerifiedAtUtc = DateTime.UtcNow;
        var creationTask = await db.Set<ManualNotebookTask>().SingleOrDefaultAsync(t => t.ProductionId == id && t.Kind == "CreateNotebook", ct);
        if (creationTask is not null && creationTask.Status != "Completed")
        {
            creationTask.Status = "Completed"; creationTask.CompletedAtUtc = DateTime.UtcNow; creationTask.EvidenceKind = "ProviderVerified";
            creationTask.Evidence = "Google confirmed access to the associated notebook; creation method was not independently verified.";
            w.NotebookCreationMethod = "ManualAssociationVerifiedByApi"; w.Stage = "NotebookReady";
        }
        var connection = await db.Set<GoogleConnection>().SingleAsync(c => c.Kind == "NotebookEnterprise", ct); connection.LastVerifiedAtUtc = DateTime.UtcNow;
        StudioRules.Event(db, id, "NotebookVerifiedByOfficialApi", external); await db.SaveChangesAsync(ct);
    }
    public async Task Sync(BackgroundJob job, CancellationToken ct)
    {
        await CheckAccess(); var id = job.ProductionId!.Value; var notebook = await NotebookId(id); var settings = Settings();
        await Verify(id, ct);
        var sources = await db.Set<ResearchSource>().Where(s => s.ProductionId == id && s.Included).ToListAsync(ct);
        foreach (var source in sources)
        {
            var binding = await db.Set<NotebookSourceBinding>().SingleOrDefaultAsync(b => b.ResearchSourceId == source.Id, ct);
            if (binding?.Status == "Verified") continue;
            if (binding is not null)
            {
                if (binding.ExternalSourceId.Length == 0) throw new StudioException(409, "Una fuente tiene un resultado ambiguo. Concilia sus datos en el notebook antes de continuar manualmente.");
                await VerifySource(binding, notebook, ct); continue;
            }
            binding = new NotebookSourceBinding { ResearchSourceId = source.Id, Status = "RequestPending" }; db.Add(binding); await db.SaveChangesAsync(ct);
            HttpResponseMessage response;
            if (source.Kind == "Document")
            {
                var artifact = await db.Set<MediaArtifact>().FindAsync([source.ArtifactId], ct) ?? throw new StudioException(409, "Documento inexistente.");
                var content = new StreamContent(File.OpenRead(storage.Resolve(artifact.StorageKey))); content.Headers.ContentType = new MediaTypeHeaderValue(artifact.MimeType);
                var request = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl.Replace("/v1alpha/", "/upload/v1alpha/") + $"/{notebook}/sources:uploadFile") { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await google.AccessToken("NotebookEnterprise", ct));
                request.Headers.Add("X-Goog-Upload-Protocol", "raw"); request.Headers.Add("X-Goog-Upload-File-Name", Uri.EscapeDataString(source.Title));
                using (request) response = await google.Client.SendAsync(request, ct);
            }
            else
            {
                object payload = source.Kind switch { "Note" => new { textContent = new { sourceName = source.Title, content = source.Notes } }, "YouTube" => new { videoContent = new { youtubeUrl = source.Url } }, _ => new { webContent = new { url = source.Url, sourceName = source.Title } } };
                response = await google.Send(HttpMethod.Post, settings.BaseUrl + $"/{notebook}/sources:batchCreate", "NotebookEnterprise", JsonContent.Create(new { userContents = new[] { payload } }), ct);
            }
            using (response)
            {
                if (!response.IsSuccessStatusCode) throw new StudioException(409, "Google no confirmó la fuente. Revisa el notebook antes de repetir la incorporación.");
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct); var external = body.TryGetProperty("sources", out var list) ? list[0] : body;
                binding.ExternalSourceId = external.GetProperty("sourceId").GetProperty("id").GetString()!;
                if (string.IsNullOrWhiteSpace(binding.ExternalSourceId)) throw new StudioException(502, "Falta el identificador oficial de la fuente.");
                // Acceptance is provider evidence of registration, not proof of completed source processing.
                binding.Status = "Registered"; binding.VerifiedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await VerifySource(binding, notebook, ct);
            }
        }
        var task = await db.Set<ManualNotebookTask>().SingleOrDefaultAsync(t => t.ProductionId == id && t.Kind == "AddNotebookSources", ct);
        if (task is not null) { task.Status = "Completed"; task.EvidenceKind = "ProviderVerified"; task.Evidence = "Official source retrieval confirmed complete processing"; task.CompletedAtUtc = DateTime.UtcNow; }
        StudioRules.Event(db, id, "NotebookSourcesVerified", sources.Count.ToString()); await db.SaveChangesAsync(ct);
    }
    private async Task VerifySource(NotebookSourceBinding binding, string notebook, CancellationToken ct)
    {
        using var verify = await google.Send(HttpMethod.Get, Settings().BaseUrl + $"/{notebook}/sources/{Uri.EscapeDataString(binding.ExternalSourceId)}", "NotebookEnterprise", null, ct);
        if (!verify.IsSuccessStatusCode) throw new StudioException(503, "La fuente está registrada; su consulta oficial se reintentará sin volver a crearla.");
        var verified = await verify.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!verified.TryGetProperty("settings", out var state) || !state.TryGetProperty("status", out var status) || status.GetString() != "SOURCE_STATUS_COMPLETE") throw new StudioException(503, "Google aún procesa la fuente. Se volverá a consultar su identificador oficial.");
        binding.Status = "Verified"; binding.VerifiedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }
    public static void MapNotebook(WebApplication app)
    {
        app.MapPost("/api/studio/productions/{id:guid}/notebook/create", async (Guid id, EditorialDb db, NotebookIntegration integration) =>
        {
            await StudioRules.Editable(db, id); await integration.CheckAccess(); await StudioRules.Workspace(db, id);
            if (!await db.Set<SourcePackage>().AnyAsync(p => p.ProductionId == id)) throw new StudioException(409, "Congela primero el paquete de fuentes.");
            await StudioRules.Enqueue(db, "CreateNotebook", id, null, "notebook:create:" + id); await db.SaveChangesAsync(); return Results.Accepted();
        });
        app.MapPost("/api/studio/productions/{id:guid}/notebook/sources", async (Guid id, EditorialDb db, NotebookIntegration integration) =>
        {
            await StudioRules.Editable(db, id); await integration.CheckAccess(); await StudioRules.Workspace(db, id);
            if (!await db.Set<SourcePackage>().AnyAsync(p => p.ProductionId == id)) throw new StudioException(409, "Congela primero el paquete de fuentes.");
            await StudioRules.Enqueue(db, "SyncNotebookSources", id, null, "notebook:sources:" + id); await db.SaveChangesAsync(); return Results.Accepted();
        });
        app.MapPost("/api/studio/productions/{id:guid}/notebook/verify", async (Guid id, EditorialDb db, NotebookIntegration integration, CancellationToken ct) => { await StudioRules.Editable(db, id); await integration.Verify(id, ct); return Results.Ok(new { providerVerified = true }); });
    }
}
