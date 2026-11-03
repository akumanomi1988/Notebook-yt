using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class WorkflowControl
{
    public static readonly string[] PreparationStages = ["research", "strategy", "script", "production"];
    public static readonly string[] FinalStages = [.. PreparationStages, "seo", "thumbnail", "editorial_review"];

    public static async Task<List<string>> ExpertFailures(EditorialDb db, Guid id, bool final)
    {
        var runs = await db.Set<ExpertWorkflowRun>().Where(r => r.ProductionId == id).ToListAsync();
        return (final ? FinalStages : PreparationStages).Where(stage =>
        {
            var latest = runs.Where(r => r.StageKey == stage).OrderByDescending(r => r.Sequence).FirstOrDefault();
            return latest is null || latest.Status != "Approved" || string.IsNullOrWhiteSpace(latest.Response)
                || string.IsNullOrWhiteSpace(latest.ConversationId);
        }).Select(stage => $"Falta una respuesta aprobada y trazable del experto: {stage}.").ToList();
    }

    public static (DateTime Start, DateTime End) LocalDay(DateTime utc, string zone)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(zone)));
        return (StudioRules.ResolveLocalTime(date, TimeOnly.MinValue, zone),
            StudioRules.ResolveLocalTime(date.AddDays(1), TimeOnly.MinValue, zone));
    }

    public static async Task EnsureDailyUpload(EditorialDb db, Guid productionId, DateTime utc)
    {
        var production = await db.Productions.FindAsync(productionId) ?? throw new StudioException(404, "Producción inexistente.");
        var channel = await db.Channels.FindAsync(production.ChannelId) ?? throw new StudioException(409, "Configura el canal.");
        var day = LocalDay(utc, channel.TimeZone);
        var ids = db.Productions.Where(p => p.ChannelId == channel.Id).Select(p => p.Id);
        if (await db.Set<StudioEvent>().AnyAsync(e => e.Kind == "YouTubeUploadConfirmed" && e.CreatedAtUtc >= day.Start
            && e.CreatedAtUtc < day.End && e.ProductionId != productionId && ids.Contains(e.ProductionId!.Value)))
            throw new StudioException(409, "Notebook-yt ya completó una subida en este día local. Conserva el episodio en la reserva.");
        if (await db.Set<YouTubeUpload>().AnyAsync(u => u.ProductionId != productionId && ids.Contains(u.ProductionId)
            && u.VideoId == "" && u.Status != "Failed"))
            throw new StudioException(409, "Hay otra subida pendiente o de resultado incierto. Concíliala antes de reservar otra.");
    }

    public static async Task<object> Status(EditorialDb db, MediaStorage storage, Guid id)
    {
        var production = await db.Productions.FindAsync(id) ?? throw new StudioException(404, "Producción inexistente.");
        var workspace = await db.Set<ProductionWorkspace>().FindAsync(id);
        var quality = workspace is null ? new QualityResult(false, ["Abre el espacio de trabajo."], []) : await QualityGate.Check(db, storage, id);
        var expertFailures = await ExpertFailures(db, id, true);
        var upload = await db.Set<YouTubeUpload>().SingleOrDefaultAsync(u => u.ProductionId == id);
        var operations = await db.Set<NotebookLmOperation>().Where(o => o.ProductionId == id).ToListAsync();
        return new
        {
            productionId = id, production.Topic, production.PlannedDate, stage = workspace?.Stage ?? "Unopened",
            readyForReserve = quality.Ready && workspace?.ApprovedAtUtc != null && expertFailures.Count == 0 && upload == null,
            quality, expertFailures,
            publicationApproved = workspace?.ApprovedAtUtc != null,
            uploaded = upload?.VideoId.Length > 0, videoId = upload?.VideoId,
            operations = operations.Select(o => new { o.Id, o.Kind, o.Status, o.ErrorCode, o.ProviderTaskId, o.ProviderArtifactId }),
            nextAction = await StudioRules.NextAction(db, production, workspace),
            limitations = new[] { "La planificación actual asigna temas aprobados; no investiga siete temas nuevos automáticamente.",
                "Los expertos, la generación y la descarga son operaciones persistentes separadas; aún no hay un ciclo diario completo que las encadene.",
                "La infografía de NotebookLM no dispone todavía de un adaptador integrado." }
        };
    }

    public static void MapWorkflowControl(this WebApplication app)
    {
        app.MapGet("/api/studio/productions/{id:guid}/workflow", async (Guid id, EditorialDb db, MediaStorage storage) =>
            Results.Ok(await Status(db, storage, id)));
        app.MapGet("/api/studio/productions/{id:guid}/reserve.zip", async (Guid id, EditorialDb db, MediaStorage storage, CancellationToken ct) =>
        {
            var production = await db.Productions.FindAsync([id], ct) ?? throw new StudioException(404, "Producción inexistente.");
            var workspace = await db.Set<ProductionWorkspace>().FindAsync([id], ct)
                ?? throw new StudioException(409, "Abre el espacio de trabajo y completa la producción antes de exportar.");
            var quality = await QualityGate.Check(db, storage, id);
            var expertFailures = await ExpertFailures(db, id, true);
            if (!quality.Ready || workspace.ApprovedAtUtc == null || expertFailures.Count > 0)
                throw new StudioException(409, "El episodio aún no es una reserva aprobada: " + string.Join(" ", quality.Failures.Concat(expertFailures)));
            var video = await db.Set<MediaArtifact>().SingleAsync(a => a.Id == workspace.CanonicalArtifactId, ct);
            var thumbnail = await db.Set<MediaArtifact>().Where(a => a.ProductionId == id && a.Kind == "Thumbnail" && a.Status == "Valid")
                .OrderByDescending(a => a.ImportedAtUtc).FirstAsync(ct);
            // Keep the verified handles open until the response finishes; never export filesystem paths or credentials.
            var files = new List<(string Name, MediaArtifact Artifact, FileStream Stream)>();
            try
            {
                foreach (var pair in new[] { ("video.mp4", video), (thumbnail.MimeType == "image/png" ? "thumbnail.png" : "thumbnail.jpg", thumbnail) })
                {
                    var file = new FileStream(storage.Resolve(pair.Item2.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read);
                    files.Add((pair.Item1, pair.Item2, file));
                    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct)) != pair.Item2.Sha256)
                        throw new StudioException(409, "Un archivo cambió desde su revisión. Vuelve a inspeccionarlo.");
                    file.Position = 0;
                }
                var sources = await db.Set<ResearchSource>().Where(s => s.ProductionId == id && s.Included).ToListAsync(ct);
                var packages = await db.Set<SourcePackage>().Where(p => p.ProductionId == id).OrderBy(p => p.Version).ToListAsync(ct);
                if (packages.Any(p => StudioRules.Hash(p.Markdown) != p.Sha256))
                    throw new StudioException(409, "Un paquete de fuentes cambió desde su aprobación.");
                var experts = await db.Set<ExpertWorkflowRun>().Where(r => r.ProductionId == id).OrderBy(r => r.Sequence)
                    .Select(r => new { r.StageKey, r.Status, r.Prompt, r.Response, r.ReviewedResponse, r.ReferencesJson, r.ConversationId, r.ExecutionNotebookId, r.ApprovalEvidence }).ToListAsync(ct);
                var notebook = await db.NotebookReferences.SingleOrDefaultAsync(n => n.ProductionId == id, ct);
                var manifest = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1, productionId = id, production.Topic, production.PlannedDate,
                    metadata = new PublicationSnapshot(workspace.YouTubeTitle, workspace.YouTubeDescription,
                        workspace.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), workspace.CategoryId,
                        workspace.Language, workspace.MadeForKids!.Value, workspace.ContainsSyntheticMedia, workspace.Visibility, workspace.PublishAtUtc),
                    notebookUrl = notebook?.Url, sources, experts, workspace.ApprovedAtUtc, workspace.ApprovalEvidence,
                    files = files.Select(f => new { path = f.Name, f.Artifact.Sha256, f.Artifact.SizeBytes, f.Artifact.Origin, f.Artifact.OriginEvidence }),
                    providerOriginCryptographicallyVerified = false
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
                return Results.Stream(async output =>
                {
                    try
                    {
                        await using var spool = new FileStream(Path.Combine(storage.Root, Guid.NewGuid().ToString("N") + ".reserve.tmp"),
                            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                        using (var archive = new ZipArchive(spool, ZipArchiveMode.Create, leaveOpen: true))
                        {
                        await using (var entry = archive.CreateEntry("youtube.json").Open()) await entry.WriteAsync(manifest, ct);
                        foreach (var package in packages)
                        {
                            await using var entry = archive.CreateEntry($"sources-v{package.Version}.md").Open();
                            await entry.WriteAsync(System.Text.Encoding.UTF8.GetBytes(package.Markdown), ct);
                        }
                        foreach (var file in files)
                        {
                            await using var entry = archive.CreateEntry(file.Name, CompressionLevel.NoCompression).Open();
                            await file.Stream.CopyToAsync(entry, ct);
                        }
                        }
                        spool.Position = 0;
                        await spool.CopyToAsync(output, ct);
                    }
                    finally { foreach (var file in files) await file.Stream.DisposeAsync(); }
                }, "application/zip", $"production-{id}.zip");
            }
            catch { foreach (var file in files) await file.Stream.DisposeAsync(); throw; }
        });
    }
}
