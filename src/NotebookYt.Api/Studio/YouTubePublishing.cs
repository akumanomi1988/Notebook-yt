using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public record PublicationSnapshot(string Title, string Description, string[] Tags, string CategoryId, string Language, bool MadeForKids, bool ContainsSyntheticMedia, string Visibility, DateTime? PublishAtUtc);

public sealed class YouTubePublishing(EditorialDb db, GoogleAuthorization google, MediaStorage storage)
{
    public static bool IsUploadSession(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "www.googleapis.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/upload/youtube/", StringComparison.Ordinal);
    private async Task<YouTubeUpload> Upload(BackgroundJob job, CancellationToken ct) => await db.Set<YouTubeUpload>().FindAsync([job.ResourceId], ct) ?? throw new StudioException(404, "Subida inexistente.");
    private static async Task Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 308) return;
        if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) throw new StudioException(503, "YouTube está temporalmente limitado; el trabajo se reintentará de forma acotada.");
        // Provider error bodies may contain credentials or signed URLs, so only the status is persisted.
        await Task.CompletedTask;
        throw new StudioException(409, $"YouTube rechazó la operación (HTTP {(int)response.StatusCode}). Revisa cuota, permisos, sesión de subida y restricciones de cuenta.");
    }
    public async Task Process(BackgroundJob job, CancellationToken ct)
    {
        var upload = await Upload(job, ct); if (upload.VideoId.Length != 0) return;
        var metadata = JsonSerializer.Deserialize<PublicationSnapshot>(upload.MetadataSnapshot)!;
        StudioRules.ValidatePublicationSchedule(metadata.Visibility, metadata.PublishAtUtc);
        var artifact = await db.Set<MediaArtifact>().FindAsync([upload.ArtifactId], ct) ?? throw new StudioException(409, "Falta el archivo aprobado.");
        if (upload.Status == "Queued")
        {
            await using (var stream = File.OpenRead(storage.Resolve(artifact.StorageKey)))
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) != artifact.Sha256) throw new StudioException(409, "El vídeo cambió después de su aprobación.");
            upload.Status = "Initializing"; await db.SaveChangesAsync(ct);
            var body = new { snippet = new { title = metadata.Title, description = metadata.Description, tags = metadata.Tags, categoryId = metadata.CategoryId, defaultLanguage = metadata.Language }, status = new { privacyStatus = "private", selfDeclaredMadeForKids = metadata.MadeForKids, containsSyntheticMedia = metadata.ContainsSyntheticMedia } };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status") { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await google.AccessToken("YouTube", ct));
            request.Headers.Add("X-Upload-Content-Length", artifact.SizeBytes.ToString()); request.Headers.Add("X-Upload-Content-Type", "video/mp4");
            using var response = await google.Client.SendAsync(request, ct); await Ensure(response);
            var session = response.Headers.Location?.AbsoluteUri;
            if (session is null || !IsUploadSession(session)) throw new StudioException(502, "Google no devolvió una sesión de subida oficial válida.");
            upload.ProtectedSessionUrl = google.Protect(session); upload.Status = "Uploading"; await db.SaveChangesAsync(ct);
        }
        if (upload.ProtectedSessionUrl.Length == 0) { upload.Status = "NeedsAttention"; throw new StudioException(409, "No se conoce el resultado de la inicialización. No se creará otra subida automáticamente."); }
        var url = google.Unprotect(upload.ProtectedSessionUrl); if (!IsUploadSession(url)) throw new StudioException(409, "Sesión de subida inválida.");
        using var probeContent = new ByteArrayContent([]); probeContent.Headers.ContentRange = new ContentRangeHeaderValue(artifact.SizeBytes);
        using var probe = await google.Send(HttpMethod.Put, url, "YouTube", probeContent, ct); await Ensure(probe);
        if ((int)probe.StatusCode != 308) { await Finish(upload, job, probe, ct); return; }
        upload.BytesConfirmed = ConfirmedBytes(probe); if (upload.BytesConfirmed < 0 || upload.BytesConfirmed >= artifact.SizeBytes) throw new StudioException(502, "El servidor devolvió un rango incoherente.");
        var length = (int)Math.Min(8 * 1024 * 1024, artifact.SizeBytes - upload.BytesConfirmed); var chunk = new byte[length];
        await using (var file = File.OpenRead(storage.Resolve(artifact.StorageKey))) { file.Position = upload.BytesConfirmed; await file.ReadExactlyAsync(chunk, ct); }
        using var content = new ByteArrayContent(chunk); content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4"); content.Headers.ContentRange = new ContentRangeHeaderValue(upload.BytesConfirmed, upload.BytesConfirmed + length - 1, artifact.SizeBytes);
        using var responseChunk = await google.Send(HttpMethod.Put, url, "YouTube", content, ct); await Ensure(responseChunk);
        if ((int)responseChunk.StatusCode == 308)
        {
            upload.BytesConfirmed = ConfirmedBytes(responseChunk); job.Status = "Queued"; job.AvailableAtUtc = DateTime.UtcNow.AddSeconds(1); job.Attempts = 0;
        }
        else await Finish(upload, job, responseChunk, ct);
        await db.SaveChangesAsync(ct);
    }
    public static long ConfirmedBytes(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Range", out var ranges)) return 0;
        var match = Regex.Match(ranges.Single(), @"^bytes=0-(\d+)$");
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var last) || last == long.MaxValue) throw new StudioException(502, "YouTube devolvió un rango de subida inválido.");
        return last + 1;
    }
    private async Task Finish(YouTubeUpload upload, BackgroundJob job, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct); var id = body.GetProperty("id").GetString()!;
        if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{6,100}$")) throw new StudioException(502, "Google no devolvió un vídeo válido.");
        upload.VideoId = id; upload.Status = "Uploaded"; upload.LastVerifiedAtUtc = DateTime.UtcNow;
        upload.LastError = "";
        var w = await StudioRules.Workspace(db, upload.ProductionId); w.Stage = "Uploaded";
        StudioRules.Event(db, upload.ProductionId, "YouTubeUploadConfirmed", id);
        await StudioRules.Enqueue(db, "PublishYouTube", upload.ProductionId, upload.Id, "publish:" + upload.Id);
        await db.SaveChangesAsync(ct);
    }
    public async Task Publish(BackgroundJob job, CancellationToken ct)
    {
        var upload = await Upload(job, ct); if (upload.VideoId.Length == 0) throw new StudioException(409, "La subida aún no está confirmada.");
        var meta = JsonSerializer.Deserialize<PublicationSnapshot>(upload.MetadataSnapshot)!;
        StudioRules.ValidatePublicationSchedule(meta.Visibility, meta.PublishAtUtc);
        var thumbnail = await db.Set<MediaArtifact>().Where(a => a.ProductionId == upload.ProductionId && a.Kind == "Thumbnail" && a.Status == "Valid").OrderByDescending(a => a.ImportedAtUtc).FirstAsync(ct);
        using (var content = new StreamContent(File.OpenRead(storage.Resolve(thumbnail.StorageKey))))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(thumbnail.MimeType);
            using var response = await google.Send(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/thumbnails/set?videoId=" + Uri.EscapeDataString(upload.VideoId), "YouTube", content, ct); await Ensure(response);
        }
        object status;
        if (meta.PublishAtUtc is DateTime date)
        {
            if (date <= DateTime.UtcNow) throw new StudioException(409, "El horario de publicación ya pasó. El vídeo permanece privado; revisa su programación en YouTube.");
            status = new { privacyStatus = "private", publishAt = date.ToString("O"), selfDeclaredMadeForKids = meta.MadeForKids, containsSyntheticMedia = meta.ContainsSyntheticMedia };
        }
        else status = new { privacyStatus = meta.Visibility, selfDeclaredMadeForKids = meta.MadeForKids, containsSyntheticMedia = meta.ContainsSyntheticMedia };
        using var update = await google.Send(HttpMethod.Put, "https://www.googleapis.com/youtube/v3/videos?part=snippet,status", "YouTube", JsonContent.Create(new { id = upload.VideoId,
            snippet = new { title = meta.Title, description = meta.Description, tags = meta.Tags, categoryId = meta.CategoryId, defaultLanguage = meta.Language }, status }), ct); await Ensure(update);
        var verifyKey = "verify:" + upload.Id; var verifyJob = await db.Set<BackgroundJob>().SingleOrDefaultAsync(j => j.IdempotencyKey == verifyKey, ct);
        if (verifyJob is null) await StudioRules.Enqueue(db, "VerifyYouTube", upload.ProductionId, upload.Id, verifyKey);
        else if (verifyJob.Status != "Running") { verifyJob.Status = "Queued"; verifyJob.Attempts = 0; verifyJob.AvailableAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
    public async Task Verify(BackgroundJob job, CancellationToken ct)
    {
        var upload = await Upload(job, ct); if (upload.VideoId.Length == 0) throw new StudioException(409, "No hay vídeo confirmado para verificar.");
        using var response = await google.Send(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/videos?part=status,snippet&id=" + Uri.EscapeDataString(upload.VideoId), "YouTube", null, ct); await Ensure(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct); var items = body.GetProperty("items");
        if (items.GetArrayLength() != 1) throw new StudioException(409, "YouTube no devuelve el vídeo esperado.");
        var video = items[0]; var connection = await db.Set<GoogleConnection>().SingleAsync(c => c.Kind == "YouTube", ct);
        if (video.GetProperty("snippet").GetProperty("channelId").GetString() != connection.ExternalChannelId) throw new StudioException(409, "El vídeo pertenece a otro canal.");
        var status = video.GetProperty("status"); var state = status.GetProperty("uploadStatus").GetString();
        if (state is "failed" or "rejected" or "deleted") { upload.Status = "NeedsAttention"; throw new StudioException(409, "YouTube rechazó o eliminó el vídeo. Revisa su panel."); }
        upload.Status = state == "processed" && status.GetProperty("privacyStatus").GetString() == "public" ? "Published" : status.TryGetProperty("publishAt", out _) ? "Scheduled" : "Uploaded";
        upload.LastVerifiedAtUtc = DateTime.UtcNow; (await StudioRules.Workspace(db, upload.ProductionId)).Stage = upload.Status;
        upload.LastError = "";
        StudioRules.Event(db, upload.ProductionId, "YouTubeStateVerified", upload.Status);
        var metadata = JsonSerializer.Deserialize<PublicationSnapshot>(upload.MetadataSnapshot)!;
        if (upload.Status != "Published" && !(state == "processed" && metadata.Visibility == "private" && metadata.PublishAtUtc is null))
        {
            var deadline = (metadata.PublishAtUtc ?? job.CreatedAtUtc).AddDays(3);
            if (DateTime.UtcNow > deadline) throw new StudioException(409, "La verificación excedió el plazo previsto. Revisa el estado directamente en YouTube.");
            job.Status = "Queued"; job.Attempts = 0;
            job.AvailableAtUtc = metadata.PublishAtUtc is DateTime scheduled && scheduled > DateTime.UtcNow ? scheduled.AddMinutes(1) : DateTime.UtcNow.AddMinutes(15);
        }
        await db.SaveChangesAsync(ct);
    }
    public static void MapYouTube(WebApplication app)
    {
        app.MapPost("/api/studio/productions/{id:guid}/upload", async (Guid id, PublicationConsent consent, EditorialDb db, MediaStorage storage) =>
        {
            var existing = await db.Set<YouTubeUpload>().SingleOrDefaultAsync(u => u.ProductionId == id); if (existing is not null) return Results.Ok(existing);
            await StudioRules.Editable(db, id); var w = await StudioRules.Workspace(db, id); var check = await QualityGate.Check(db, storage, id);
            var p = await db.Productions.FindAsync(id); var settings = await db.Set<ChannelSettings>().FindAsync(p!.ChannelId);
            if (!consent.Approved || !check.Ready || (settings?.RequireEditorialApproval != false && w.ApprovedAtUtc is null)) throw new StudioException(409, "Falta aprobación explícita o hay bloqueos de calidad.");
            if (!await db.Set<GoogleConnection>().AnyAsync(c => c.Kind == "YouTube" && c.Status == "Connected")) throw new StudioException(409, "Conecta YouTube antes de encolar la subida.");
            var metadata = new PublicationSnapshot(w.YouTubeTitle, w.YouTubeDescription, w.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), w.CategoryId, w.Language, w.MadeForKids!.Value, w.ContainsSyntheticMedia, w.Visibility, w.PublishAtUtc);
            var upload = new YouTubeUpload { ProductionId = id, ArtifactId = w.CanonicalArtifactId!.Value, MetadataSnapshot = JsonSerializer.Serialize(metadata) }; db.Add(upload);
            w.Stage = "Uploading"; await StudioRules.Enqueue(db, "UploadYouTube", id, upload.Id, "upload:" + id);
            StudioRules.Event(db, id, "PublicationExplicitlyApproved", StudioRules.Text(consent.Evidence, 4000, true)); await db.SaveChangesAsync(); return Results.Accepted($"/api/studio/productions/{id}", upload);
        });
        app.MapPost("/api/studio/uploads/{id:guid}/verify", async (Guid id, EditorialDb db) =>
        {
            var upload = await db.Set<YouTubeUpload>().FindAsync(id) ?? throw new StudioException(404, "Subida inexistente.");
            var key = "verify:" + id; var job = await db.Set<BackgroundJob>().SingleOrDefaultAsync(j => j.IdempotencyKey == key);
            if (job is null) await StudioRules.Enqueue(db, "VerifyYouTube", upload.ProductionId, id, key);
            else if (job.Status != "Running") { job.Status = "Queued"; job.AvailableAtUtc = DateTime.UtcNow; job.Attempts = 0; }
            await db.SaveChangesAsync(); return Results.Accepted();
        });
        app.MapPost("/api/studio/uploads/{id:guid}/reconcile", async (Guid id, UploadReconciliation input, EditorialDb db, YouTubePublishing publishing, CancellationToken ct) =>
        {
            var upload = await db.Set<YouTubeUpload>().FindAsync(id) ?? throw new StudioException(404, "Subida inexistente.");
            await StudioRules.EditableJob(db, upload.ProductionId);
            if (upload.VideoId.Length != 0 || !input.Attested || !Regex.IsMatch(input.VideoId ?? "", "^[A-Za-z0-9_-]{6,100}$")) throw new StudioException(409, "La conciliación requiere un identificador válido y una declaración explícita; el vídeo confirmado no puede sustituirse.");
            if (await db.Set<BackgroundJob>().AnyAsync(j => j.ResourceId == id && j.Status == "Running", ct)) throw new StudioException(409, "Espera al final del trabajo en curso.");
            var evidence = StudioRules.Text(input.Evidence, 4000, true); upload.VideoId = input.VideoId!;
            await publishing.Verify(new BackgroundJob { ResourceId = id, ProductionId = upload.ProductionId, Kind = "VerifyYouTube" }, ct);
            StudioRules.Event(db, upload.ProductionId, "UploadReconciledByOperator", $"Video {input.VideoId}; channel confirmed by YouTube; file association attested: {evidence}");
            foreach (var old in await db.Set<BackgroundJob>().Where(j => j.ResourceId == id && j.Kind == "UploadYouTube").ToListAsync(ct)) old.Status = "Succeeded";
            await db.SaveChangesAsync(ct); return Results.Ok(upload);
        });
        app.MapPut("/api/studio/uploads/{id:guid}/metadata", async (Guid id, PublishedMetadataEdit input, EditorialDb db, YouTubePublishing publishing, CancellationToken ct) =>
        {
            StudioRules.ValidatePublicationSchedule(input.Visibility, input.PublishAtUtc);
            var upload = await db.Set<YouTubeUpload>().FindAsync(id) ?? throw new StudioException(404, "Subida inexistente."); await StudioRules.EditableJob(db, upload.ProductionId);
            if (!input.Approved || upload.VideoId.Length == 0 || await db.Set<BackgroundJob>().AnyAsync(j => j.ResourceId == id && j.Status == "Running", ct)) throw new StudioException(409, "La revisión requiere aprobación explícita y un vídeo confirmado sin trabajos en curso.");
            await publishing.Verify(new BackgroundJob { ResourceId = id, CreatedAtUtc = DateTime.UtcNow }, ct);
            var old = JsonSerializer.Deserialize<PublicationSnapshot>(upload.MetadataSnapshot)!;
            if (input.PublishAtUtc is DateTime date && (date.Kind != DateTimeKind.Utc || date <= DateTime.UtcNow || upload.Status == "Published")) throw new StudioException(409, "Solo se puede programar una fecha futura en un vídeo que no se haya publicado.");
            var revised = old with { Title = StudioRules.Text(input.Title, 100, true), Description = StudioRules.Text(input.Description, 5000, true), Tags = StudioRules.Text(input.Tags, 500).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Visibility = StudioRules.Choice(input.Visibility, "private", "unlisted", "public"), PublishAtUtc = input.PublishAtUtc };
            StudioRules.ValidatePublicationText(revised.Title, revised.Description, input.Tags);
            var evidence = StudioRules.Text(input.Evidence, 4000, true);
            StudioRules.Event(db, upload.ProductionId, "PublicationMetadataRevisionApproved", JsonSerializer.Serialize(new { previous = old, revised, evidence })); upload.MetadataSnapshot = JsonSerializer.Serialize(revised);
            var key = "publish:" + id; var job = await db.Set<BackgroundJob>().SingleOrDefaultAsync(j => j.IdempotencyKey == key, ct);
            if (job is null) await StudioRules.Enqueue(db, "PublishYouTube", upload.ProductionId, id, key);
            else { job.Status = "Queued"; job.Attempts = 0; job.AvailableAtUtc = DateTime.UtcNow; }
            var verifyJob = await db.Set<BackgroundJob>().SingleOrDefaultAsync(j => j.IdempotencyKey == "verify:" + id, ct);
            if (verifyJob is not null && verifyJob.Status != "Running") verifyJob.Status = "Cancelled";
            await db.SaveChangesAsync(ct); return Results.Accepted();
        });
    }
}
public record PublicationConsent(bool Approved, string Evidence);
public record UploadReconciliation(string VideoId, bool Attested, string Evidence);
public record PublishedMetadataEdit(string Title, string Description, string Tags, string Visibility, DateTime? PublishAtUtc, bool Approved, string Evidence);
