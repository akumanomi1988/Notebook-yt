using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class StudioEndpoints
{
    public static void MapStudio(this WebApplication app)
    {
        var api = app.MapGroup("/api/studio");
        api.MapGet("/settings", async (EditorialDb db) =>
        {
            var channel = await db.Channels.SingleOrDefaultAsync();
            return channel is null ? Results.Problem(statusCode: 409, detail: "Configura primero el canal.") : Results.Ok(new {
                channel = channel.ToResponse(), settings = await db.Set<ChannelSettings>().FindAsync(channel.Id) ?? new ChannelSettings { Id = channel.Id },
                niches = await db.Niches.OrderBy(n => n.Weekday).ToListAsync(), nicheSettings = await db.Set<NicheSettings>().ToListAsync()
            });
        });
        api.MapPut("/settings", async (ChannelSettings input, EditorialDb db) =>
        {
            var channel = await db.Channels.SingleOrDefaultAsync() ?? throw new StudioException(409, "Configura primero el canal.");
            input.Id = channel.Id;
            input.Description = StudioRules.Text(input.Description, 4000);
            input.Language = StudioRules.Text(input.Language, 20, true);
            input.Audience = StudioRules.Text(input.Audience, 4000);
            input.Tone = StudioRules.Text(input.Tone, 2000);
            input.Guidelines = StudioRules.Text(input.Guidelines);
            input.Visibility = StudioRules.Choice(input.Visibility, "private", "unlisted", "public");
            input.NotebookProduct = StudioRules.Choice(input.NotebookProduct, "Personal", "Enterprise");
            if (input.HorizonDays is < 1 or > 90 || input.LeadDays is < 0 or > 90 || input.MinimumDurationSeconds < 1 || input.MaximumDurationSeconds < input.MinimumDurationSeconds || input.MaximumDurationSeconds > 14400 || input.MinimumHeight is < 144 or > 2160 || input.RetentionDays is < 7 or > 3650)
                throw new StudioException(400, "Revisa los límites de planificación, retención y calidad.");
            var current = await db.Set<ChannelSettings>().FindAsync(channel.Id);
            if (current is null) db.Add(input); else db.Entry(current).CurrentValues.SetValues(input);
            StudioRules.Event(db, null, "ChannelSettingsUpdated", "Configuración editorial actualizada");
            await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapPut("/niches/{id:guid}", async (Guid id, NicheSettings input, EditorialDb db) =>
        {
            if (!await db.Niches.AnyAsync(n => n.Id == id)) return Results.NotFound();
            input.Id = id; input.Audience = StudioRules.Text(input.Audience, 4000); input.InclusionRules = StudioRules.Text(input.InclusionRules);
            input.ExclusionRules = StudioRules.Text(input.ExclusionRules); input.VideoApproach = StudioRules.Text(input.VideoApproach, 4000);
            var existing = await db.Set<NicheSettings>().FindAsync(id);
            if (existing is null) db.Add(input); else db.Entry(existing).CurrentValues.SetValues(input);
            await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapGet("/topics", async (EditorialDb db) => await db.Set<EditorialTopic>().AsNoTracking().OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAtUtc).ToListAsync());
        api.MapPost("/topics", async (TopicInput input, EditorialDb db) =>
        {
            var title = StudioRules.Text(input.Title, 300, true); var normalized = title.Normalize().ToUpperInvariant();
            if (!await db.Niches.AnyAsync(n => n.Id == input.ContentNicheId && n.Enabled)) throw new StudioException(400, "Selecciona un nicho activo.");
            var existing = await db.Set<EditorialTopic>().SingleOrDefaultAsync(t => t.ContentNicheId == input.ContentNicheId && t.NormalizedTitle == normalized);
            if (existing is not null) return Results.Conflict(new { detail = "El tema ya existe en este nicho.", existing.Id });
            var topic = new EditorialTopic { ContentNicheId = input.ContentNicheId, Title = title, NormalizedTitle = normalized,
                Origin = StudioRules.Choice(input.Origin, "Manual", "NotebookLMImport", "SourceMetadata"), EvergreenAssessment = StudioRules.Text(input.EvergreenAssessment, 4000), Priority = Math.Clamp(input.Priority, 0, 100) };
            db.Add(topic); await db.SaveChangesAsync(); return Results.Created($"/api/studio/topics/{topic.Id}", topic);
        });
        api.MapPut("/topics/{id:guid}", async (Guid id, TopicDecision input, EditorialDb db) =>
        {
            var topic = await db.Set<EditorialTopic>().FindAsync(id) ?? throw new StudioException(404, "Tema inexistente.");
            if (topic.ProductionId is not null) throw new StudioException(409, "El tema ya tiene una producción.");
            topic.Status = StudioRules.Choice(input.Status, "Proposed", "Approved", "Rejected"); topic.Priority = Math.Clamp(input.Priority, 0, 100);
            topic.EvergreenAssessment = StudioRules.Text(input.EvergreenAssessment, 4000);
            StudioRules.Event(db, null, "TopicDecision", $"{topic.Id}: {topic.Status}"); await db.SaveChangesAsync(); return Results.Ok(topic);
        });
        api.MapPost("/topics/{id:guid}/production", async (Guid id, TopicProductionInput input, EditorialDb db) =>
        {
            var topic = await db.Set<EditorialTopic>().FindAsync(id) ?? throw new StudioException(404, "Tema inexistente.");
            if (topic.ProductionId is not null) return Results.Ok((await db.Productions.FindAsync(topic.ProductionId))!.ToResponse());
            if (topic.Status != "Approved" || input.PlannedDate == default) throw new StudioException(409, "Aprueba el tema e indica una fecha válida.");
            var niche = await db.Niches.FindAsync(topic.ContentNicheId);
            if (niche is null || !niche.Enabled) throw new StudioException(409, "El nicho no está activo.");
            var p = new Production { ChannelId = niche.ChannelId, ContentNicheId = niche.Id, NicheNameSnapshot = niche.Name, Topic = topic.Title, PlannedDate = input.PlannedDate };
            db.Add(p); db.Add(new ProductionStatusHistory { ProductionId = p.Id, To = ProductionStatus.Draft }); topic.ProductionId = p.Id;
            await db.SaveChangesAsync(); await OpenWorkspace(db, p, topic.Id); await db.SaveChangesAsync(); return Results.Created($"/api/studio/productions/{p.Id}", p.ToResponse());
        });
        api.MapPost("/productions/{id:guid}/open", async (Guid id, EditorialDb db) =>
        {
            var p = await StudioRules.Editable(db, id); var w = await OpenWorkspace(db, p, null); await db.SaveChangesAsync(); return Results.Ok(w);
        });
        api.MapGet("/productions/{id:guid}", async (Guid id, EditorialDb db) =>
        {
            var p = await db.Productions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id) ?? throw new StudioException(404, "Producción inexistente.");
            var w = await db.Set<ProductionWorkspace>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == id);
            return Results.Ok(new { production = p.ToResponse(), workspace = w, nextAction = await StudioRules.NextAction(db, p, w),
                sources = await db.Set<ResearchSource>().Where(s => s.ProductionId == id).ToListAsync(), packages = await db.Set<SourcePackage>().Where(s => s.ProductionId == id).OrderByDescending(s => s.Version).ToListAsync(),
                sourceBindings = await db.Set<NotebookSourceBinding>().Where(b => db.Set<ResearchSource>().Any(s => s.Id == b.ResearchSourceId && s.ProductionId == id)).ToListAsync(),
                tasks = (await db.Set<ManualNotebookTask>().Where(t => t.ProductionId == id).ToListAsync()).OrderBy(t => Array.FindIndex(StudioRules.ManualSteps, step => step.Kind == t.Kind)),
                notebook = await db.NotebookReferences.SingleOrDefaultAsync(n => n.ProductionId == id), artifacts = await db.Set<MediaArtifact>().Where(a => a.ProductionId == id).OrderByDescending(a => a.ImportedAtUtc).ToListAsync(),
                reviews = await db.Set<QualityReview>().Where(r => r.ProductionId == id).ToListAsync(), events = await db.Set<StudioEvent>().Where(e => e.ProductionId == id).OrderBy(e => e.CreatedAtUtc).ToListAsync(),
                uploads = await db.Set<YouTubeUpload>().Where(u => u.ProductionId == id).ToListAsync() });
        });
        api.MapPut("/productions/{id:guid}", async (Guid id, WorkspaceInput input, EditorialDb db) =>
        {
            await StudioRules.Editable(db, id); var w = await StudioRules.Workspace(db, id);
            if (await db.Set<SourcePackage>().AnyAsync(p => p.ProductionId == id) && (w.Objective != input.Objective || w.VideoInstructions != input.VideoInstructions || w.Audience != input.Audience || w.Language != input.Language))
                throw new StudioException(409, "El paquete de fuentes ya está congelado. Conserva el contexto histórico.");
            w.Objective = StudioRules.Text(input.Objective); w.VideoInstructions = StudioRules.Text(input.VideoInstructions); w.Audience = StudioRules.Text(input.Audience, 4000); w.Language = StudioRules.Text(input.Language, 20, true);
            w.YouTubeTitle = StudioRules.Text(input.YouTubeTitle, 100); w.YouTubeDescription = StudioRules.Text(input.YouTubeDescription, 5000); w.Tags = StudioRules.Text(input.Tags, 500);
            StudioRules.ValidatePublicationText(w.YouTubeTitle, w.YouTubeDescription, w.Tags);
            w.CategoryId = StudioRules.Text(input.CategoryId, 10, true);
            if (!w.CategoryId.All(char.IsAsciiDigit)) throw new StudioException(400, "La categoría debe ser un identificador numérico de YouTube.");
            w.MadeForKids = input.MadeForKids; w.ContainsSyntheticMedia = input.ContainsSyntheticMedia; w.Visibility = StudioRules.Choice(input.Visibility, "private", "unlisted", "public");
            if (input.PublishAtUtc is not null && (input.PublishAtUtc.Value.Kind != DateTimeKind.Utc || input.PublishAtUtc <= DateTime.UtcNow)) throw new StudioException(400, "La publicación programada debe ser futura y UTC.");
            if (input.PublishAtUtc is not null && await db.Set<ProductionWorkspace>().AnyAsync(other => other.Id != id && other.PublishAtUtc == input.PublishAtUtc && db.Productions.Any(p => p.Id == other.Id && p.Status != ProductionStatus.Cancelled))) throw new StudioException(409, "Otra producción ya ocupa ese horario.");
            StudioRules.ValidatePublicationSchedule(w.Visibility, input.PublishAtUtc);
            w.PublishAtUtc = input.PublishAtUtc; w.ApprovedAtUtc = null; w.ApprovalEvidence = ""; w.ModifiedAtUtc = DateTime.UtcNow;
            if (w.Stage == "ReadyForUpload") w.Stage = "QualityReview";
            StudioRules.Event(db, id, "WorkspaceUpdated", JsonSerializer.Serialize(input)); await db.SaveChangesAsync(); return Results.Ok(w);
        });
        api.MapPost("/productions/{id:guid}/sources", async (Guid id, SourceInput input, EditorialDb db) =>
        {
            await StudioRules.Editable(db, id); await StudioRules.Workspace(db, id);
            if (await db.Set<SourcePackage>().AnyAsync(p => p.ProductionId == id)) throw new StudioException(409, "Las fuentes del paquete ya están congeladas.");
            var kind = StudioRules.Choice(input.Kind, "Url", "YouTube", "Note", "Document");
            var url = kind is "Url" or "YouTube" ? StudioRules.PublicUrl(input.Url) : "";
            if (kind == "YouTube" && new Uri(url).Host is not ("www.youtube.com" or "youtube.com" or "youtu.be")) throw new StudioException(400, "Indica una URL de YouTube.");
            var notes = StudioRules.Text(input.Notes); var provenance = StudioRules.Text(input.Provenance, 4000, true);
            if (kind == "Note" && notes.Length == 0) throw new StudioException(400, "La nota está vacía.");
            if (kind == "Document" && !await db.Set<MediaArtifact>().AnyAsync(a => a.Id == input.ArtifactId && a.ProductionId == id && a.Kind == "Document" && a.Status == "Valid")) throw new StudioException(400, "Sube primero el documento.");
            var fp = StudioRules.Hash(kind + "|" + url + "|" + (kind == "Note" ? notes : input.ArtifactId?.ToString()));
            if (await db.Set<ResearchSource>().AnyAsync(s => s.ProductionId == id && s.Fingerprint == fp)) throw new StudioException(409, "La fuente ya está registrada.");
            var source = new ResearchSource { ProductionId = id, Title = StudioRules.Text(input.Title, 300, true), Kind = kind, Url = url, Notes = notes, Provenance = provenance,
                Classification = StudioRules.Choice(input.Classification, "Primary", "Secondary", "Context"), Fingerprint = fp, Included = input.Included, ArtifactId = input.ArtifactId };
            db.Add(source); StudioRules.Event(db, id, "SourceRegistered", source.Id.ToString()); await db.SaveChangesAsync(); return Results.Ok(source);
        });
        api.MapPut("/sources/{id:guid}/inclusion", async (Guid id, InclusionInput input, EditorialDb db) =>
        {
            var source = await db.Set<ResearchSource>().FindAsync(id) ?? throw new StudioException(404, "Fuente inexistente."); await StudioRules.Editable(db, source.ProductionId);
            if (await db.Set<SourcePackage>().AnyAsync(p => p.ProductionId == source.ProductionId)) throw new StudioException(409, "El paquete ya está congelado.");
            source.Included = input.Included; await db.SaveChangesAsync(); return Results.Ok(source);
        });
        api.MapPost("/productions/{id:guid}/package", async (Guid id, EditorialDb db) =>
        {
            var p = await StudioRules.Editable(db, id); var w = await StudioRules.Workspace(db, id);
            var editorial = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(s => s.ProductionId == id);
            if (editorial is not null)
            {
                if (editorial.ActiveSnapshotId is null) throw new StudioException(409, "Aprueba la vista previa en Dirección editorial antes de preparar NotebookLM.");
                var snapshot = await db.Set<EditorialPackageSnapshot>().FindAsync(editorial.ActiveSnapshotId);
                return Results.Ok(await db.Set<SourcePackage>().FindAsync(snapshot!.SourcePackageId));
            }
            var existing = await db.Set<SourcePackage>().OrderByDescending(s => s.Version).FirstOrDefaultAsync(s => s.ProductionId == id);
            if (existing is not null) return Results.Ok(existing);
            var sources = await db.Set<ResearchSource>().Where(s => s.ProductionId == id && s.Included).OrderBy(s => s.RegisteredAtUtc).ToListAsync();
            if (sources.Count == 0 || string.IsNullOrWhiteSpace(w.Objective)) throw new StudioException(409, "Indica el objetivo y al menos una fuente con procedencia.");
            var text = new StringBuilder().AppendLine($"# {p.Topic}").AppendLine($"\nObjective: {w.Objective}\nAudience: {w.Audience}\nLanguage: {w.Language}\nEditorial snapshot: {w.EditorialSnapshot}\nVideo instructions: {w.VideoInstructions}\n\n## Sources");
            foreach (var source in sources) text.AppendLine($"- [{source.Id}] {source.Title}\n  Kind: {source.Kind}; URL: {source.Url}; Document: {source.ArtifactId}\n  Provenance: {source.Provenance}\n  Registered UTC: {source.RegisteredAtUtc:O}\n  Context: {source.Notes}");
            var package = new SourcePackage { ProductionId = id, Version = 1, Markdown = text.ToString(), Sha256 = StudioRules.Hash(text.ToString()) }; db.Add(package);
            foreach (var step in StudioRules.ManualSteps) db.Add(new ManualNotebookTask { ProductionId = id, Kind = step.Kind, Instructions = step.Instruction, ExpectedResult = step.Expected });
            w.Stage = "NotebookPending"; StudioRules.Event(db, id, "SourcePackageFrozen", package.Sha256); await db.SaveChangesAsync(); return Results.Ok(package);
        });
        api.MapGet("/packages/{id:guid}/download", async (Guid id, EditorialDb db) =>
        {
            var package = await db.Set<SourcePackage>().FindAsync(id) ?? throw new StudioException(404, "Paquete inexistente.");
            return Results.File(Encoding.UTF8.GetBytes(package.Markdown), "text/markdown; charset=utf-8", $"source-package-{package.ProductionId}-v{package.Version}.md");
        });
        api.MapPost("/tasks/{id:guid}/complete", async (Guid id, TaskCompletion input, EditorialDb db) =>
        {
            var task = await db.Set<ManualNotebookTask>().FindAsync(id) ?? throw new StudioException(404, "Tarea inexistente."); await StudioRules.Editable(db, task.ProductionId);
            if (task.Status == "Completed") return Results.Ok(task);
            var tasks = await db.Set<ManualNotebookTask>().Where(t => t.ProductionId == task.ProductionId).ToListAsync();
            var index = Array.FindIndex(StudioRules.ManualSteps, s => s.Kind == task.Kind);
            if (StudioRules.ManualSteps.Take(index).Any(s => !tasks.Any(t => t.Kind == s.Kind && t.Status == "Completed"))) throw new StudioException(409, "Completa primero la tarea anterior.");
            if (task.Kind == "CreateNotebook" && !await db.NotebookReferences.AnyAsync(n => n.ProductionId == task.ProductionId)) throw new StudioException(409, "Registra primero la URL del notebook.");
            if (task.Kind == "ImportNotebookVideo" && !await db.Set<MediaArtifact>().AnyAsync(a => a.ProductionId == task.ProductionId && a.Kind == "Video" && a.Status == "Valid" && a.Origin == "OperatorAttested")) throw new StudioException(409, "Falta un vídeo real inspeccionado con procedencia declarada.");
            var editorial = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(c => c.ProductionId == task.ProductionId);
            if (editorial is not null)
            {
                if (editorial.ActiveSnapshotId is null) throw new StudioException(409, "Aprueba primero el paquete editorial vigente.");
                if (task.Kind == "ConfigureVideoOverview" && !await db.Set<StudioEvent>().AnyAsync(e => e.ProductionId == task.ProductionId && e.Kind == "EditorialSettingsAttested" && e.Detail.Contains(editorial.ActiveSnapshotId.Value.ToString()))) throw new StudioException(409, "Registra los ajustes realmente usados en Dirección editorial.");
            }
            if (!input.Attested) throw new StudioException(400, "Debes declarar expresamente que has realizado la operación.");
            task.Evidence = StudioRules.Text(input.Evidence, 4000, true); task.EvidenceKind = "OperatorAttested"; task.Status = "Completed"; task.CompletedAtUtc = DateTime.UtcNow;
            var w = await StudioRules.Workspace(db, task.ProductionId);
            w.Stage = task.Kind switch { "CreateNotebook" => "NotebookReady", "StartVideoGeneration" => "AwaitingNotebookGeneration", "ConfirmVideoGeneration" => "AwaitingVideoDownload", "DownloadNotebookVideo" => "AwaitingVideoImport", "ImportNotebookVideo" => "VideoImported", _ => w.Stage };
            if (task.Kind == "CreateNotebook") w.NotebookCreationMethod = "ManualOperatorAttested";
            StudioRules.Event(db, task.ProductionId, "ManualTaskAttested", $"{task.Kind}: {task.Evidence}"); await db.SaveChangesAsync(); return Results.Ok(task);
        });
        api.MapPost("/productions/{id:guid}/canonical/{artifactId:guid}", async (Guid id, Guid artifactId, EditorialDb db) =>
        {
            await StudioRules.Editable(db, id); var w = await StudioRules.Workspace(db, id);
            if (!await db.Set<MediaArtifact>().AnyAsync(a => a.Id == artifactId && a.ProductionId == id && a.Kind == "Video" && a.Status == "Valid" && a.Origin == "OperatorAttested")) throw new StudioException(409, "Selecciona un vídeo válido de NotebookLM con procedencia declarada.");
            if (await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == id && t.Status != "Completed")) throw new StudioException(409, "Completa las tareas de NotebookLM antes de seleccionar el vídeo canónico.");
            w.CanonicalArtifactId = artifactId; w.Stage = "QualityReview"; w.ApprovedAtUtc = null;
            StudioRules.Event(db, id, "CanonicalArtifactSelected", artifactId.ToString()); await db.SaveChangesAsync(); return Results.Ok(w);
        });
        api.MapGet("/productions/{id:guid}/quality", async (Guid id, EditorialDb db, MediaStorage storage) => Results.Ok(await QualityGate.Check(db, storage, id)));
        api.MapPost("/productions/{id:guid}/review", async (Guid id, ReviewInput input, EditorialDb db, MediaStorage storage) =>
        {
            await StudioRules.Editable(db, id); var w = await StudioRules.Workspace(db, id); var check = await QualityGate.Check(db, storage, id);
            if (input.Approved && !check.Ready) throw new StudioException(409, string.Join(" ", check.Failures));
            if (w.CanonicalArtifactId is null) throw new StudioException(409, "Falta el vídeo canónico.");
            var evidence = StudioRules.Text(input.Evidence, 4000, true); db.Add(new QualityReview { ProductionId = id, ArtifactId = w.CanonicalArtifactId.Value, Approved = input.Approved, Evidence = evidence });
            w.ApprovedAtUtc = input.Approved ? DateTime.UtcNow : null; w.ApprovalEvidence = evidence; w.Stage = input.Approved ? "ReadyForUpload" : "QualityReview";
            StudioRules.Event(db, id, input.Approved ? "EditorialApproval" : "EditorialRejection", evidence); await db.SaveChangesAsync(); return Results.Ok(w);
        });
        api.MapGet("/overview", async (EditorialDb db) => Results.Ok(new {
            topics = await db.Set<EditorialTopic>().CountAsync(t => t.Status == "Approved" && t.ProductionId == null),
            pendingTasks = await db.Set<ManualNotebookTask>().Where(t => t.Status == "Pending" && db.Productions.Any(p => p.Id == t.ProductionId && p.Status != ProductionStatus.Cancelled)).ToListAsync(),
            workspaces = await db.Set<ProductionWorkspace>().ToListAsync(),
            jobs = await db.Set<BackgroundJob>().OrderByDescending(j => j.CreatedAtUtc).Take(100).ToListAsync(),
            slots = await db.Set<ScheduleSlot>().OrderBy(s => s.PublishAtUtc).ToListAsync(), uploads = await db.Set<YouTubeUpload>().ToListAsync()
        }));
        api.MapGet("/jobs", async (EditorialDb db) => await db.Set<BackgroundJob>().OrderByDescending(j => j.CreatedAtUtc).Take(200).ToListAsync());
        api.MapPost("/jobs/{id:guid}/retry", async (Guid id, EditorialDb db) =>
        {
            var job = await db.Set<BackgroundJob>().FindAsync(id) ?? throw new StudioException(404, "Trabajo inexistente.");
            if (job.Status != "Failed" || job.Kind == "CreateNotebook") throw new StudioException(409, "La operación exige conciliación o no admite reintento en este estado.");
            if (job.Kind == "SyncNotebookSources" && await db.Set<NotebookSourceBinding>().AnyAsync(b => b.ExternalSourceId == "" && db.Set<ResearchSource>().Any(s => s.Id == b.ResearchSourceId && s.ProductionId == job.ProductionId))) throw new StudioException(409, "Hay una fuente ambigua sin identificador; concíliala manualmente.");
            if (job.ProductionId is not null) await StudioRules.EditableJob(db, job.ProductionId.Value);
            job.Status = "Queued"; job.Attempts = 0; job.AvailableAtUtc = DateTime.UtcNow; job.LastError = ""; await db.SaveChangesAsync(); return Results.Ok(job);
        });
        api.MapPost("/jobs/{id:guid}/cancel", async (Guid id, EditorialDb db) =>
        {
            var job = await db.Set<BackgroundJob>().FindAsync(id) ?? throw new StudioException(404, "Trabajo inexistente.");
            if (job.Status == "Running" || job.LeaseUntilUtc > DateTime.UtcNow) throw new StudioException(409, "Espera a que termine el paso en curso; no se repetirá sin conciliación.");
            if (job.Status != "Succeeded") job.Status = "Cancelled"; await db.SaveChangesAsync(); return Results.Ok(job);
        });
        api.MapPost("/jobs/{id:guid}/resolve", async (Guid id, TaskCompletion input, EditorialDb db) =>
        {
            var job = await db.Set<BackgroundJob>().FindAsync(id) ?? throw new StudioException(404, "Trabajo inexistente.");
            if (job.Status != "NeedsAttention" || !input.Attested) throw new StudioException(409, "La conciliación requiere una declaración explícita sobre una operación ambigua.");
            var evidence = StudioRules.Text(input.Evidence, 4000, true);
            if (job.ProductionId is Guid productionId)
            {
                await StudioRules.EditableJob(db, productionId);
                var kind = job.Kind == "CreateNotebook" ? "CreateNotebook" : job.Kind == "SyncNotebookSources" ? "AddNotebookSources" : "";
                if (kind.Length == 0 || !await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == productionId && t.Kind == kind && t.Status == "Completed")) throw new StudioException(409, "Asocia y verifica el notebook, o completa la tarea manual de fuentes, antes de conciliar el trabajo.");
            }
            job.Status = "ResolvedManually"; StudioRules.Event(db, job.ProductionId, "AmbiguousJobResolvedByOperator", $"{job.Id}: {evidence}"); await db.SaveChangesAsync(); return Results.Ok(job);
        });
    }

    public static async Task<ProductionWorkspace> OpenWorkspace(EditorialDb db, Production p, Guid? topicId)
    {
        var existing = await db.Set<ProductionWorkspace>().FindAsync(p.Id); if (existing is not null) return existing;
        var niche = await db.Niches.FindAsync(p.ContentNicheId); var ns = await db.Set<NicheSettings>().FindAsync(p.ContentNicheId); var cs = await db.Set<ChannelSettings>().FindAsync(p.ChannelId);
        var w = new ProductionWorkspace { Id = p.Id, TopicId = topicId, Objective = p.Topic, YouTubeTitle = p.Topic.Length > 100 ? p.Topic[..100] : p.Topic, Audience = ns?.Audience ?? cs?.Audience ?? "", Language = cs?.Language ?? "es", Visibility = cs?.Visibility ?? "private",
            EditorialSnapshot = JsonSerializer.Serialize(new { niche = niche?.ToResponse(), nicheSettings = ns, channelSettings = cs }) };
        db.Add(w); db.Add(new EditorialConfiguration { ProductionId = p.Id });
        StudioRules.Event(db, p.Id, "WorkspaceOpened", "Snapshot editorial congelado"); return w;
    }
}

public record TopicInput(Guid ContentNicheId, string Title, string Origin, string EvergreenAssessment, int Priority);
public record TopicDecision(string Status, int Priority, string EvergreenAssessment);
public record TopicProductionInput(DateOnly PlannedDate);
public record WorkspaceInput(string Objective, string VideoInstructions, string Language, string Audience, string YouTubeTitle, string YouTubeDescription, string Tags, string CategoryId, bool? MadeForKids, bool ContainsSyntheticMedia, string Visibility, DateTime? PublishAtUtc);
public record SourceInput(string Title, string Kind, string Url, string Notes, string Classification, string Provenance, bool Included, Guid? ArtifactId);
public record InclusionInput(bool Included);
public record TaskCompletion(bool Attested, string Evidence);
public record ReviewInput(bool Approved, string Evidence);
