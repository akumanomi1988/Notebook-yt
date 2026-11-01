using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class NotebookLmOperationProcessor(EditorialDb db, INotebookLmProvider provider, MediaStorage storage)
{
    public async Task Execute(BackgroundJob job, CancellationToken ct)
    {
        var operation = await db.Set<NotebookLmOperation>().FindAsync([job.ResourceId], ct)
            ?? throw new StudioException(404, "Operación NotebookLM inexistente.");
        if (operation.Status is "Completed" or "OutcomeUnknown" or "DeferredQuota") return;
        if (operation.Status == "Running")
        {
            operation.Status = "OutcomeUnknown"; operation.ErrorCode = "INTERRUPTED_OUTCOME_UNKNOWN";
            job.Status = "NeedsAttention"; return;
        }
        operation.Status = "Running"; operation.ModifiedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        using var request = JsonDocument.Parse(operation.RequestJson);
        var root = request.RootElement;
        if (operation.Kind != "CreateNotebook")
        {
            var notebook = await provider.GetNotebookAsync(operation.NotebookId, ct);
            if (!notebook.Success || !ContainsId(notebook.Json, operation.NotebookId))
            {
                operation.Status = "Failed"; operation.ErrorCode = notebook.Success ? "NOTEBOOK_ID_MISMATCH" : notebook.Code;
                operation.ModifiedAtUtc = DateTime.UtcNow; return;
            }
        }
        NotebookLmCliResult? result = null;
        switch (operation.Kind)
        {
            case "CreateNotebook":
                result = await provider.CreateNotebookAsync(root.GetProperty("title").GetString()!, ct);
                break;
            case "AddSource":
            {
                var url = root.GetProperty("url").GetString()!;
                var currentSources = await provider.ListSourcesAsync(operation.NotebookId, ct);
                if (!currentSources.Success || !TryReadSources(currentSources.Json, out var sources))
                {
                    operation.Status = "Failed"; operation.ErrorCode = currentSources.Success ? "SOURCE_LIST_UNVERIFIED" : currentSources.Code; break;
                }
                var existing = sources.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Url) &&
                    string.Equals(NormalizeSourceUrl(x.Url), NormalizeSourceUrl(url), StringComparison.Ordinal));
                if (existing is not null)
                {
                    result = new(true, "OK", JsonSerializer.Serialize(new { id = existing.Id, url = existing.Url, duplicate = true }), 0);
                    break;
                }
                result = await provider.AddSourceAsync(operation.NotebookId, url, ct);
                break;
            }
            case "Research":
                result = await provider.StartResearchAsync(operation.NotebookId, root.GetProperty("query").GetString()!, root.GetProperty("mode").GetString()!, ct);
                break;
            case "GenerateVideo":
            {
                var auth = await provider.CheckAuthenticationAsync(ct);
                if (!auth.Success || !IsAuthReady(auth.Json))
                {
                    operation.Status = "Failed"; operation.ErrorCode = "AUTH_REQUIRED"; break;
                }
                var usage = await provider.GetUsageAsync(ct);
                var assessment = NotebookLmQuotaManager.Assess(usage.Success ? usage.Json : null);
                SaveUsage(assessment);
                if (!usage.Success || assessment.VideoAvailable is null)
                {
                    operation.Status = "DeferredQuota"; operation.ErrorCode = usage.Success ? "QUOTA_UNKNOWN" : usage.Code;
                    job.Status = "NeedsAttention"; break;
                }
                if (!assessment.VideoAvailable.Value || assessment.IsExhausted == true)
                {
                    operation.Status = "DeferredQuota"; operation.ErrorCode = "VIDEO_QUOTA_UNAVAILABLE";
                    job.Status = "NeedsAttention"; break;
                }
                var sources = root.GetProperty("sourceIds").EnumerateArray().Select(x => x.GetString()!).ToArray();
                var registered = await provider.ListSourcesAsync(operation.NotebookId, ct);
                var sourceIds = registered.Success ? ReadIds(registered.Json) : [];
                if (!registered.Success || sources.Any(id => !sourceIds.Contains(id, StringComparer.Ordinal)))
                {
                    operation.Status = "Failed"; operation.ErrorCode = registered.Success ? "SOURCE_NOT_IN_NOTEBOOK" : registered.Code; break;
                }
                var existingArtifacts = await provider.ListArtifactsAsync(operation.NotebookId, "video", ct);
                if (!existingArtifacts.Success || !TryReadArtifacts(existingArtifacts.Json, out var artifacts))
                {
                    operation.Status = "Failed"; operation.ErrorCode = existingArtifacts.Success ? "ARTIFACT_LIST_UNVERIFIED" : existingArtifacts.Code; break;
                }
                if (artifacts.Any(x => !new[] { "failed", "error", "cancelled", "canceled" }.Contains(x.Status, StringComparer.OrdinalIgnoreCase)))
                {
                    operation.Status = "Failed"; operation.ErrorCode = "EXISTING_VIDEO_ARTIFACT_REQUIRES_RECONCILIATION";
                    operation.ResultJson = JsonSerializer.Serialize(artifacts); break;
                }
                result = await provider.GenerateVideoAsync(operation.NotebookId, root.GetProperty("prompt").GetString()!,
                    root.GetProperty("language").GetString()!, root.GetProperty("style").GetString()!,
                    root.TryGetProperty("customStyle", out var custom) ? custom.GetString() : null, sources, ct);
                break;
            }
            case "DownloadVideo":
                await Download(operation, root, ct);
                return;
            default: throw new StudioException(400, "Tipo de operación NotebookLM desconocido.");
        }
        if (operation.Status != "Running" || result is null) { operation.ModifiedAtUtc = DateTime.UtcNow; return; }
        if (result.Success)
        {
            operation.ResultJson = result.Json!;
            operation.ProviderTaskId = FindString(result.Json, "task_id", "taskId", "run_id", "runId") ?? "";
            operation.ProviderArtifactId = FindString(result.Json, "artifact_id", "artifactId") ?? "";
            operation.Status = "Completed"; operation.ErrorCode = "";
            StudioRules.Event(db, operation.ProductionId, "NotebookLmOperationCompleted", $"{operation.Id}; {operation.Kind}; {operation.ProviderTaskId}; {operation.ProviderArtifactId}");
        }
        else
        {
            operation.Status = result.OutcomeUnknown ? "OutcomeUnknown" : "Failed";
            operation.ErrorCode = result.Code;
            if (result.OutcomeUnknown) job.Status = "NeedsAttention";
        }
        operation.ModifiedAtUtc = DateTime.UtcNow;
    }

    private async Task Download(NotebookLmOperation operation, JsonElement root, CancellationToken ct)
    {
        var productionId = operation.ProductionId ?? throw new StudioException(400, "La descarga requiere una producción.");
        var notebookUrl = await db.NotebookReferences.Where(x => x.ProductionId == productionId)
            .Select(x => x.Url).SingleOrDefaultAsync(ct);
        if (!NotebookLmBindings.MatchesNotebook(notebookUrl, operation.NotebookId))
            throw new StudioException(409, "Asocia el cuaderno a la producción antes de importar un artefacto.");
        Directory.CreateDirectory(storage.Root);
        var temporary = Path.Combine(storage.Root, Guid.NewGuid().ToString("N") + ".notebooklm-download.mp4");
        var artifactId = root.GetProperty("artifactId").GetString()!;
        try
        {
            var result = await provider.DownloadVideoAsync(operation.NotebookId, artifactId, temporary, ct);
            if (!result.Success)
            {
                operation.Status = result.OutcomeUnknown ? "OutcomeUnknown" : "Failed"; operation.ErrorCode = result.Code;
                if (result.OutcomeUnknown) { operation.Status = "OutcomeUnknown"; }
                operation.ModifiedAtUtc = DateTime.UtcNow; return;
            }
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new StudioException(502, "La CLI informó de descarga, pero no apareció un archivo MP4 verificable.");
            await using var file = File.OpenRead(temporary); var saved = await storage.Save(file, 1024L * 1024 * 1024, ct);
            var existing = await db.Set<MediaArtifact>().SingleOrDefaultAsync(x => x.ProductionId == productionId && x.Kind == "Video" && x.Sha256 == saved.Hash, ct);
            if (existing is not null)
            {
                operation.MediaArtifactId = existing.Id; operation.Status = "Completed";
                operation.ResultJson = JsonSerializer.Serialize(new { artifactId = existing.Id, sha256 = existing.Sha256, duplicateContent = true });
            }
            else
            {
                var artifact = new MediaArtifact
                {
                    ProductionId = productionId, Kind = "Video", OriginalName = "notebooklm-" + artifactId + ".mp4",
                    StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length, MimeType = "video/mp4",
                    Status = "PendingInspection", Origin = "NotebookLmProviderVerified",
                    OriginEvidence = $"NotebookLM CLI artifact {artifactId}; notebook {operation.NotebookId}. Provider association recorded by CLI; origin is not cryptographic proof."
                };
                db.Add(artifact); await db.SaveChangesAsync(ct);
                operation.MediaArtifactId = artifact.Id; operation.ProviderArtifactId = artifactId; operation.Status = "Completed";
                operation.ResultJson = JsonSerializer.Serialize(new { artifactId = artifact.Id, sha256 = saved.Hash, duplicateContent = false });
                await StudioRules.Enqueue(db, "InspectArtifact", productionId, artifact.Id, "inspect:" + artifact.Id);
                StudioRules.Event(db, productionId, "NotebookLmVideoDownloaded", $"{operation.Id}; external artifact {artifactId}; internal artifact {artifact.Id}; SHA256 {saved.Hash}");
            }
            operation.ModifiedAtUtc = DateTime.UtcNow;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void SaveUsage(NotebookLmQuotaAssessment assessment)
    {
        db.Add(new NotebookLmUsageSnapshot { Status = assessment.Status, IsExhausted = assessment.IsExhausted,
            ActiveWindow = assessment.ActiveWindow ?? "", ResetsAtUtc = DateTime.TryParse(assessment.ResetAtUtc, out var reset) ? reset.ToUniversalTime() : null,
            VideoAvailable = assessment.VideoAvailable, EvidenceJson = assessment.Evidence?.GetRawText() ?? "{}", CheckedAtUtc = DateTime.UtcNow });
    }

    private static bool IsAuthReady(string? json)
    {
        try { using var doc = JsonDocument.Parse(json ?? "{}"); var root = doc.RootElement; return root.TryGetProperty("status", out var status) && status.GetString() == "ok" && root.TryGetProperty("checks", out var checks) && checks.TryGetProperty("token_fetch", out var token) && token.ValueKind == JsonValueKind.True; }
        catch (JsonException) { return false; }
    }
    private static bool TryReadArtifacts(string? json, out List<ExistingArtifact> artifacts)
    {
        artifacts = [];
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}"); var root = doc.RootElement;
            JsonElement items;
            if (root.ValueKind == JsonValueKind.Array) items = root;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("artifacts", out var array) && array.ValueKind == JsonValueKind.Array) items = array;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out array) && array.ValueKind == JsonValueKind.Array) items = array;
            else return false;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return false;
                var id = FindStringProperty(item, "id", "artifact_id", "artifactId");
                if (string.IsNullOrWhiteSpace(id)) return false;
                var status = FindStringProperty(item, "status", "state") ?? "unknown";
                artifacts.Add(new ExistingArtifact(id, status));
            }
            return true;
        }
        catch (JsonException) { return false; }
    }
    private static string? FindStringProperty(JsonElement root, params string[] keys)
    {
        foreach (var key in keys) if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        return null;
    }
    private static bool TryReadSources(string? json, out List<ExistingSource> sources)
    {
        sources = [];
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}"); var root = doc.RootElement; JsonElement items;
            if (root.ValueKind == JsonValueKind.Array) items = root;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("sources", out var array) && array.ValueKind == JsonValueKind.Array) items = array;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out array) && array.ValueKind == JsonValueKind.Array) items = array;
            else return false;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return false;
                var id = FindStringProperty(item, "id", "source_id", "sourceId");
                var url = FindStringProperty(item, "url", "source_url", "sourceUrl");
                if (string.IsNullOrWhiteSpace(id) ||
                    (!string.IsNullOrWhiteSpace(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))) return false;
                sources.Add(new ExistingSource(id, url));
            }
            return true;
        }
        catch (JsonException) { return false; }
    }
    private static string NormalizeSourceUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        var builder = new UriBuilder(uri) { Fragment = "" };
        if (builder.Path.Length > 1) builder.Path = builder.Path.TrimEnd('/');
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
    private sealed record ExistingSource(string Id, string? Url);
    private sealed record ExistingArtifact(string Id, string Status);
    private static bool ContainsId(string? json, string expected) => ReadIds(json).Contains(expected, StringComparer.Ordinal);
    private static string[] ReadIds(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}"); var values = new List<string>();
            void Walk(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) values.Add(id.GetString()!);
                    foreach (var p in element.EnumerateObject()) if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Walk(p.Value);
                }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Walk(item);
            }
            Walk(doc.RootElement); return values.Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }
    private static string? FindString(string? json, params string[] names)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            return Find(doc.RootElement);
            string? Find(JsonElement item)
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in names) if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
                    foreach (var property in item.EnumerateObject()) if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && Find(property.Value) is { } found) return found;
                }
                else if (item.ValueKind == JsonValueKind.Array) foreach (var element in item.EnumerateArray()) if (Find(element) is { } found) return found;
                return null;
            }
        }
        catch (JsonException) { return null; }
    }
}

public static class NotebookLmOperationEndpoints
{
    public static void MapNotebookLmOperations(this WebApplication app)
    {
        app.MapGet("/api/studio/notebooklm/operations", async (EditorialDb db, CancellationToken ct) =>
            Results.Ok(await db.Set<NotebookLmOperation>().AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct)));
        app.MapPost("/api/studio/notebooklm/operations", async (NotebookLmOperationInput input, HttpRequest request, EditorialDb db, INotebookLmProvider provider, CancellationToken ct) =>
        {
            var key = request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 180) throw new StudioException(400, "Se requiere una clave Idempotency-Key válida.");
            var kind = input.Kind switch { "create_notebook" => "CreateNotebook", "add_source" => "AddSource", "research" => "Research", "generate_video" => "GenerateVideo", "download_video" => "DownloadVideo", _ => throw new StudioException(400, "Operación NotebookLM no admitida.") };
            if (input.ProductionId is Guid productionId)
            {
                var production = await db.Productions.FindAsync([productionId], ct) ?? throw new StudioException(404, "Producción inexistente.");
                if (production.Status == ProductionStatus.Cancelled) throw new StudioException(409, "La producción está cancelada.");
            }
            if (input.AffectExpertKnowledge && kind != "AddSource") throw new StudioException(400, "La opción de conocimiento permanente solo se aplica al alta de una fuente.");
            if (kind == "AddSource")
            {
                var expertNotebook = await db.Set<NotebookExpert>().AnyAsync(x => x.NotebookId == input.NotebookId && x.Enabled, ct);
                if (expertNotebook)
                {
                    if (!input.AffectExpertKnowledge || input.ProductionId is not null) throw new StudioException(409, "Añadir una fuente modifica el conocimiento permanente del experto y requiere esa acción explícita.");
                }
                else
                {
                    if (input.ProductionId is not Guid productionNotebookId) throw new StudioException(400, "La operación requiere una producción con cuaderno dedicado.");
                    var notebookUrl = await db.NotebookReferences.Where(x => x.ProductionId == productionNotebookId)
                        .Select(x => x.Url).SingleOrDefaultAsync(ct);
                    if (!NotebookLmBindings.MatchesNotebook(notebookUrl, input.NotebookId!))
                        throw new StudioException(409, "El ID de cuaderno no coincide con la referencia registrada para esta producción.");
                }
            }
            if (kind is "Research" or "GenerateVideo" or "DownloadVideo")
            {
                if (input.ProductionId is not Guid productionNotebookId) throw new StudioException(400, "La operación requiere una producción con cuaderno dedicado.");
                if (await db.Set<NotebookExpert>().AnyAsync(x => x.NotebookId == input.NotebookId, ct)) throw new StudioException(409, "Los cuadernos expertos son conocimiento permanente; usa el cuaderno de la producción para tareas episódicas.");
                var notebookUrl = await db.NotebookReferences.Where(x => x.ProductionId == productionNotebookId)
                    .Select(x => x.Url).SingleOrDefaultAsync(ct);
                if (!NotebookLmBindings.MatchesNotebook(notebookUrl, input.NotebookId!)) throw new StudioException(409, "El ID de cuaderno no coincide con la referencia registrada para esta producción.");
            }
            if (kind != "CreateNotebook" && !System.Text.RegularExpressions.Regex.IsMatch(input.NotebookId ?? "", "^[A-Za-z0-9_-]{1,160}$")) throw new StudioException(400, "Selecciona un ID de cuaderno NotebookLM real.");
            var sourceIds = input.SourceIds ?? [];
            if (sourceIds.Length > 100 || sourceIds.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x, "^[A-Za-z0-9_-]{1,160}$")) || sourceIds.Distinct().Count() != sourceIds.Length) throw new StudioException(400, "Selección de fuentes no válida.");
            object requestData = kind switch
            {
                "CreateNotebook" when !string.IsNullOrWhiteSpace(input.Title) && input.Title.Length <= 200 => new { title = input.Title.Trim() },
                "AddSource" when input.Url is not null && input.Url.Length <= 2048 && Uri.TryCreate(input.Url, UriKind.Absolute, out var sourceUri) && sourceUri.Scheme == Uri.UriSchemeHttps && sourceUri.UserInfo.Length == 0 => new { url = input.Url, affectExpertKnowledge = input.AffectExpertKnowledge },
                "Research" when !string.IsNullOrWhiteSpace(input.Query) && input.Query.Length <= 2000 && input.Mode is "fast" or "deep" => new { query = input.Query.Trim(), mode = input.Mode },
                "GenerateVideo" when !string.IsNullOrWhiteSpace(input.Prompt) && input.Prompt.Length <= 20000 && !string.IsNullOrWhiteSpace(input.Language) && !string.IsNullOrWhiteSpace(input.Style) && sourceIds.Length > 0 => new { prompt = input.Prompt, language = input.Language, style = input.Style, customStyle = input.CustomStyle, sourceIds },
                "DownloadVideo" when !string.IsNullOrWhiteSpace(input.ArtifactId) && System.Text.RegularExpressions.Regex.IsMatch(input.ArtifactId, "^[A-Za-z0-9_-]{1,160}$") => new { artifactId = input.ArtifactId },
                _ => throw new StudioException(400, "Faltan campos requeridos o exceden sus límites.")
            };
            var serialized = JsonSerializer.Serialize(requestData);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind, input.ProductionId, input.NotebookId, serialized }))));
            var existing = await db.Set<NotebookLmOperation>().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.RequestHash != hash) throw new StudioException(409, "La clave idempotente se usó para otra petición.");
                return Results.Ok(existing);
            }
            if (kind is "CreateNotebook" or "AddSource" or "Research" or "GenerateVideo")
            {
                var auth = await provider.CheckAuthenticationAsync(ct);
                if (!auth.Success) throw new StudioException(409, "No se puede confirmar una sesión válida de NotebookLM; consulta el diagnóstico.");
            }
            var operation = new NotebookLmOperation { ProductionId = input.ProductionId, Kind = kind, NotebookId = input.NotebookId ?? "", IdempotencyKey = key, RequestHash = hash, RequestJson = serialized };
            db.Add(operation);
            await StudioRules.Enqueue(db, "RunNotebookLmOperation", operation.ProductionId, operation.Id, "notebooklm-operation:" + key);
            StudioRules.Event(db, operation.ProductionId, "NotebookLmOperationQueued", $"{operation.Id}; {kind}");
            await db.SaveChangesAsync(ct);
            return Results.Accepted("/api/studio/notebooklm/operations", operation);
        });
    }
}

public sealed record NotebookLmOperationInput(string Kind, Guid? ProductionId, string? NotebookId, string? Title, string? Url,
    string? Query, string? Mode, string? Prompt, string? Language, string? Style, string? CustomStyle, string[]? SourceIds, string? ArtifactId,
    bool AffectExpertKnowledge = false);
