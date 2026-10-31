using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class EditorialEndpoints
{
    public static void MapEditorialIntelligence(this WebApplication app)
    {
        var api = app.MapGroup("/api/studio/editorial");
        api.MapGet("/catalog", async (EditorialDb db) => Results.Ok(new {
            archetypes = await EditorialIntelligence.Library(db), archetypeVersions = await db.Set<NarrativeDefinition>().ToDictionaryAsync(n => n.Id, n => n.Version), styles = EditorialIntelligence.Styles, languages = EditorialIntelligence.Languages,
            fields = EditorialIntelligence.FieldKeys, reviewCriteria = EditorialIntelligence.ReviewCriteria,
            capabilities = new[] {
                new { option = "EXPLAINER", formats = new[] { "EXPLAINER" }, languages = "Local catalog subset of documented languages", accountRequirement = "Acceso a NotebookLM y cuota disponible", availability = "Documented", verificationStatus = "AccountUnverified", documentation = EditorialIntelligence.Documentation, lastReviewed = "2026-10-31" },
                new { option = "SHORT", formats = new[] { "SHORT" }, languages = "Documented Short languages; never automatic default", accountRequirement = "Acceso a NotebookLM y cuota disponible", availability = "Documented", verificationStatus = "AccountUnverified", documentation = EditorialIntelligence.Documentation, lastReviewed = "2026-10-31" },
                new { option = "CINEMATIC", formats = new[] { "CINEMATIC" }, languages = "en", accountRequirement = "Google AI Ultra, 18+ y disponibilidad observada en la cuenta", availability = "Restricted", verificationStatus = "AccountUnverified", documentation = EditorialIntelligence.Documentation, lastReviewed = "2026-10-31" },
                new { option = "VISUAL_STYLES", formats = new[] { "EXPLAINER" }, languages = "Explainer languages", accountRequirement = "18+ y disponibilidad observada en la cuenta", availability = "Documented", verificationStatus = "AccountUnverified", documentation = EditorialIntelligence.Documentation, lastReviewed = "2026-10-31" },
                new { option = "PRODUCER_NOTES", formats = new[] { "EXPLAINER", "SHORT", "CINEMATIC" }, languages = "According to format", accountRequirement = "Google AI Ultra y acceso de edición al notebook; confirmar despliegue", availability = "Restricted", verificationStatus = "AccountUnverified", documentation = EditorialIntelligence.Documentation, lastReviewed = "2026-10-31" }
            }
        }));
        api.MapPut("/archetypes/{key}", async (string key, NarrativeWrite input, EditorialDb db) => {
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Z][A-Z0-9_]{2,60}$") || input.Definition is null || input.Definition.Id != key) throw new StudioException(400, "Identificador de arquetipo no válido.");
            foreach (var value in new[] { input.Definition.Name, input.Definition.Description, input.Definition.Purpose, input.Definition.Structure, input.Definition.SuitableTopics, input.Definition.VisualGuidance, input.Definition.Weaknesses, input.Definition.SteeringTemplate }) StudioRules.Text(value, 12000, true);
            StudioRules.Text(input.Definition.RecommendationCriteria, 12000);
            var row = await db.Set<NarrativeDefinition>().FindAsync(key);
            if ((row?.Version ?? 0) != input.Version) throw new StudioException(409, "El arquetipo cambió. Recarga la biblioteca.");
            if (row is null) { row = new NarrativeDefinition { Id = key, Version = 0 }; db.Add(row); }
            row.Version++; row.DefinitionJson = JsonSerializer.Serialize(input.Definition, EditorialIntelligence.Json);
            StudioRules.Event(db, null, "NarrativeDefinitionUpdated", key); await db.SaveChangesAsync(); return Results.Ok(new { row.Version, definition = input.Definition });
        });
        api.MapGet("/defaults/{scope}/{id:guid}", async (string scope, Guid id, EditorialDb db) => {
            await ValidateScope(scope, id, db); var row = await Scope(db, scope, id);
            return Results.Ok(new { version = row?.Version ?? 0, draft = EditorialIntelligence.Read(row) });
        });
        api.MapPut("/defaults/{scope}/{id:guid}", async (string scope, Guid id, EditorialWrite input, EditorialDb db) => {
            await ValidateScope(scope, id, db); EditorialIntelligence.ValidateDraft(input.Draft);
            if (input.Draft.SourceIds.Count > 0 || input.Draft.OutputEdits.Count > 0) throw new StudioException(400, "Las fuentes y salidas editadas pertenecen al episodio, no a los valores predeterminados.");
            var row = await Scope(db, scope, id);
            if ((row?.Version ?? 0) != input.Version) throw new StudioException(409, "Los valores predeterminados cambiaron. Recarga antes de guardar.");
            if (row is null) { row = new EditorialConfiguration { ChannelId = scope == "channel" ? id : null, ContentNicheId = scope == "niche" ? id : null, Version = 0 }; db.Add(row); }
            row.Version++; row.DraftJson = JsonSerializer.Serialize(input.Draft, EditorialIntelligence.Json);
            StudioRules.Event(db, null, "EditorialDefaultsUpdated", $"{scope}:{id}:v{row.Version}"); await db.SaveChangesAsync(); return Results.Ok(new { row.Version, draft = input.Draft });
        });
        api.MapGet("/productions/{id:guid}", async (Guid id, EditorialDb db) => Results.Ok(await EditorialWorkflow.Preview(db, id)));
        api.MapGet("/productions/{id:guid}/progress", async (Guid id, EditorialDb db) => {
            if (!await db.Productions.AnyAsync(p => p.Id == id)) throw new StudioException(404, "Producción inexistente.");
            var c = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
            var w = await db.Set<ProductionWorkspace>().FindAsync(id);
            if (c is null || w is null) return Results.Ok(new { review = (EditorialAssessment?)null, events = Array.Empty<StudioEvent>() });
            var review = await db.Set<EditorialAssessment>().Where(x => x.ProductionId == id && x.SnapshotId == c!.ActiveSnapshotId && x.ArtifactId == w!.CanonicalArtifactId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
            var events = await db.Set<StudioEvent>().Where(e => e.ProductionId == id && (e.Kind == "EditorialSettingsAttested" || e.Kind == "ProducerNotesApproved")).OrderByDescending(e => e.CreatedAtUtc).ToListAsync();
            return Results.Ok(new { review, events });
        });
        api.MapPut("/productions/{id:guid}", async (Guid id, EditorialWrite input, EditorialDb db) => {
            await StudioRules.Editable(db, id); await StudioRules.Workspace(db, id); EditorialIntelligence.ValidateDraft(input.Draft);
            var row = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
            if ((row?.Version ?? 0) != input.Version) throw new StudioException(409, "Otra sesión modificó la dirección editorial. Recarga y conserva tus cambios antes de reintentar.");
            if (row?.ActiveSnapshotId is not null || await db.Set<SourcePackage>().AnyAsync(x => x.ProductionId == id) && row is null) throw new StudioException(409, "El paquete está congelado. Abre una revisión antes de generar o crea otra producción para conservar el historial.");
            if (row is null) { row = new EditorialConfiguration { ProductionId = id, Version = 0 }; db.Add(row); }
            row.DraftJson = JsonSerializer.Serialize(input.Draft, EditorialIntelligence.Json); row.Version++;
            var w = await StudioRules.Workspace(db, id); w.ApprovedAtUtc = null; w.ApprovalEvidence = ""; if (w.Stage == "ReadyForUpload") w.Stage = "QualityReview";
            StudioRules.Event(db, id, "EditorialDraftSaved", $"v{row.Version}"); await db.SaveChangesAsync();
            var preview = await EditorialWorkflow.Preview(db, id, false);
            if (EditorialIntelligence.Compatibility(preview.Effective, input.Draft).Count > 0) throw new StudioException(400, string.Join(" ", EditorialIntelligence.Compatibility(preview.Effective, input.Draft)));
            if (!preview.Archetypes.Any(a => a.Id == preview.Effective["archetype"])) throw new StudioException(400, "Arquetipo desconocido.");
            return Results.Ok(preview);
        });
        api.MapPost("/productions/{id:guid}/approve", async (Guid id, EditorialFreeze input, EditorialDb db) => Results.Ok(await EditorialWorkflow.Freeze(db, id, input)));
        api.MapPost("/productions/{id:guid}/revision", async (Guid id, EditorialFreeze input, EditorialDb db) => {
            await StudioRules.Editable(db, id); var row = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
            if (row is null || row.Version != input.Version || row.ActiveSnapshotId is null) throw new StudioException(409, "No hay un paquete editorial vigente con esa versión.");
            if (await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == id && t.Status == "Completed") || await db.Set<MediaArtifact>().AnyAsync(a => a.ProductionId == id && a.Kind == "Video")) throw new StudioException(409, "Ya hay trabajo generado o tareas realizadas. Crea otra producción para conservar la evidencia congelada.");
            row.ActiveSnapshotId = null; row.Version++; var w = await StudioRules.Workspace(db, id); w.ApprovedAtUtc = null; w.ApprovalEvidence = ""; w.Stage = "Research";
            StudioRules.Event(db, id, "EditorialRevisionOpened", $"v{row.Version}; snapshots anteriores conservados"); await db.SaveChangesAsync(); return Results.Ok(await EditorialWorkflow.Preview(db, id, false));
        });
        api.MapGet("/productions/{id:guid}/recommendation", async (Guid id, EditorialDb db) => {
            var preview = await EditorialWorkflow.Preview(db, id); var p = await db.Productions.FindAsync(id);
            var recentIds = await db.Productions.Where(x => x.Id != id && x.Status != ProductionStatus.Cancelled).OrderByDescending(x => x.CreatedAtUtc).Take(20).Select(x => x.Id).ToListAsync();
            var recent = (await db.Set<EditorialConfiguration>().Where(x => x.ProductionId != null && recentIds.Contains(x.ProductionId.Value)).ToListAsync()).Select(x => EditorialIntelligence.Read(x).Fields.GetValueOrDefault("archetype", "")).ToArray();
            return Results.Ok(EditorialIntelligence.Recommend(preview.Effective, p!.Topic, preview.Sources.Count, recent, preview.Archetypes));
        });
        api.MapGet("/productions/{id:guid}/export", async (Guid id, EditorialDb db) => {
            var preview = await EditorialWorkflow.Preview(db, id);
            return Results.File(Encoding.UTF8.GetBytes(EditorialWorkflow.Markdown(preview)), "text/markdown; charset=utf-8", $"editorial-{id}-v{preview.Version}.md");
        });
        api.MapPost("/productions/{id:guid}/actual-settings", async (Guid id, EditorialEvidence input, EditorialDb db) => {
            await StudioRules.Editable(db, id); var row = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
            if (row?.ActiveSnapshotId is null || row.Version != input.Version || !input.Attested || input.SourceIds is null) throw new StudioException(409, "Aprueba el paquete vigente y declara los ajustes utilizados.");
            var p = await EditorialWorkflow.Preview(db, id); var evidence = StudioRules.Text(input.Evidence, 12000, true);
            if (input.Format != p.Effective["format"] || input.Language != p.Effective["language"] || input.VisualStyle != p.Effective["visualStyle"] || !input.SourceIds.Order().SequenceEqual(p.Sources.Select(s => s.Id).Order())) throw new StudioException(409, "Los ajustes reales difieren del paquete. No se pueden aprobar como equivalentes: prepara otra revisión o producción.");
            if (p.Draft.RequestProducerNotes && !await db.Set<StudioEvent>().AnyAsync(e => e.ProductionId == id && e.Kind == "ProducerNotesApproved" && e.Detail.Contains(row.ActiveSnapshotId.Value.ToString()))) throw new StudioException(409, "Registra la aprobación de las notas del productor reales antes de confirmar los ajustes.");
            StudioRules.Event(db, id, "EditorialSettingsAttested", JsonSerializer.Serialize(new { snapshotId = row.ActiveSnapshotId, input.Format, input.Language, input.VisualStyle, input.SourceIds, evidence, verification = "OperatorAttestedNotProviderVerified", atUtc = DateTime.UtcNow }, EditorialIntelligence.Json));
            await db.SaveChangesAsync(); return Results.Ok(new { verification = "OperatorAttested", providerVerified = false });
        });
        api.MapPost("/productions/{id:guid}/producer-notes", async (Guid id, ProducerNotesInput input, EditorialDb db) => {
            await StudioRules.Editable(db, id); var row = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
            if (row?.ActiveSnapshotId is null || row.Version != input.Version || !input.Approved) throw new StudioException(409, "Aprueba el paquete y confirma las notas revisadas.");
            var preview = await EditorialWorkflow.Preview(db, id);
            if (!preview.Draft.RequestProducerNotes) throw new StudioException(409, "Este paquete no solicita revisión de notas del productor.");
            StudioRules.Text(input.Notes, 12000, true); StudioRules.Text(input.Feedback, 12000); StudioRules.Text(input.Evidence, 4000, true);
            StudioRules.Event(db, id, "ProducerNotesApproved", JsonSerializer.Serialize(new { snapshotId = row.ActiveSnapshotId, input.Notes, input.Feedback, input.Evidence, atUtc = DateTime.UtcNow }));
            await db.SaveChangesAsync(); return Results.Ok(new { approved = true, providerVerified = false });
        });
        api.MapPost("/productions/{id:guid}/review", async (Guid id, EditorialReviewInput input, EditorialDb db) => {
            await StudioRules.Editable(db, id); var row = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
            if (row?.ActiveSnapshotId is not Guid snapshot || row.Version != input.Version) throw new StudioException(409, "La revisión requiere el paquete editorial vigente.");
            var w = await StudioRules.Workspace(db, id);
            if (w.CanonicalArtifactId != input.ArtifactId || !await db.Set<MediaArtifact>().AnyAsync(a => a.Id == input.ArtifactId && a.ProductionId == id && a.Status == "Valid" && a.Kind == "Video" && a.Origin == "OperatorAttested")) throw new StudioException(409, "Selecciona el vídeo canónico original, inspeccionado y declarado.");
            if (input.Evidence is null || input.Evidence.Keys.Any(k => !EditorialIntelligence.ReviewCriteria.Contains(k))) throw new StudioException(400, "Criterio de revisión desconocido.");
            foreach (var value in input.Evidence.Values) StudioRules.Text(value, 4000, true);
            if (input.Approved && EditorialIntelligence.ReviewCriteria.Any(k => !input.Evidence.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))) throw new StudioException(409, "Aporta evidencia para cada criterio editorial; no hay aprobaciones automáticas de contenido.");
            if (input.Approved && !await db.Set<StudioEvent>().AnyAsync(e => e.ProductionId == id && e.Kind == "EditorialSettingsAttested" && e.Detail.Contains(snapshot.ToString()))) throw new StudioException(409, "Falta declarar los ajustes realmente utilizados en NotebookLM.");
            db.Add(new EditorialAssessment { ProductionId = id, SnapshotId = snapshot, ArtifactId = input.ArtifactId, Approved = input.Approved, EvidenceJson = JsonSerializer.Serialize(input.Evidence) });
            w.ApprovedAtUtc = null; w.ApprovalEvidence = ""; w.Stage = "QualityReview";
            StudioRules.Event(db, id, "DetailedEditorialReview", input.Approved ? "Aprobada por el operador con evidencias" : "Rechazada por el operador"); await db.SaveChangesAsync(); return Results.Ok(new { approved = input.Approved, verification = "HumanEvidence" });
        });
        api.MapGet("/variety", async (EditorialDb db) => {
            var profiles = await db.Set<EditorialConfiguration>().AsNoTracking().ToListAsync();
            var productions = await db.Productions.OrderByDescending(p => p.CreatedAtUtc).Take(100).ToListAsync();
            var workspaces = await db.Set<ProductionWorkspace>().ToListAsync();
            var snapshots = await db.Set<EditorialPackageSnapshot>().AsNoTracking().ToListAsync();
            var episodes = productions.Select(p => {
                var c = profiles.SingleOrDefault(x => x.ProductionId == p.Id);
                var f = EditorialIntelligence.Inherit(new() { ["archetype"] = "EXPLAINER", ["visualStyle"] = "AUTOMATIC" }, EditorialIntelligence.Read(profiles.SingleOrDefault(x => x.ChannelId == p.ChannelId)).Fields, EditorialIntelligence.Read(profiles.SingleOrDefault(x => x.ContentNicheId == p.ContentNicheId)).Fields, EditorialIntelligence.Read(c).Fields);
                if (c?.ActiveSnapshotId is Guid active) f = JsonSerializer.Deserialize<EditorialPreview>(snapshots.Single(x => x.Id == active).PayloadJson, EditorialIntelligence.Json)!.Effective;
                return new EditorialVarietyEpisode(p.Id, p.Topic, p.ContentNicheId, p.NicheNameSnapshot, p.PlannedDate, workspaces.SingleOrDefault(w => w.Id == p.Id)?.Stage ?? "Research", c is null ? "Unconfigured" : f["archetype"], c is null ? "Unconfigured" : f["visualStyle"], f.GetValueOrDefault("centralQuestion", ""), c is not null);
            }).ToArray();
            return Results.Ok(new { episodes, archetypeNames = (await EditorialIntelligence.Library(db)).ToDictionary(n => n.Id, n => n.Name), archetypes = episodes.Where(e => e.Configured).GroupBy(e => e.Archetype).Select(g => new { key = g.Key, count = g.Count() }), visualStyles = episodes.Where(e => e.Configured).GroupBy(e => e.VisualStyle).Select(g => new { key = g.Key, count = g.Count() }), niches = episodes.GroupBy(e => e.ContentNicheId).Select(g => new { nicheId = g.Key, count = g.Count(), completed = g.Count(x => x.Stage == "Published") }), repeatedQuestions = episodes.Where(e => !string.IsNullOrWhiteSpace(e.Question)).GroupBy(e => string.Join(' ', EditorialIntelligence.Tokens(e.Question).Order())).Where(g => g.Count() > 1).Select(g => new { question = g.First().Question, productionIds = g.Select(e => e.Id) }), opportunities = episodes.Count(e => !e.Configured) > 0 ? new[] { "Completar la dirección editorial de los episodios sin configurar antes de proponer variedad." } : new[] { "Comparar preguntas y aportaciones antes de repetir un tema; no rotar formatos artificialmente." }, guidance = "La variedad es orientativa. Una serie puede repetir un enfoque con una justificación; no se cambia el calendario ni se certifica monetización. Muestra: hasta 100 producciones recientes." });
        });
    }
    private static Task<EditorialConfiguration?> Scope(EditorialDb db, string scope, Guid id) => scope == "channel" ? db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ChannelId == id) : db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ContentNicheId == id);
    private static async Task ValidateScope(string scope, Guid id, EditorialDb db)
    {
        StudioRules.Choice(scope, "channel", "niche");
        if (scope == "channel" ? !await db.Channels.AnyAsync(x => x.Id == id) : !await db.Niches.AnyAsync(x => x.Id == id)) throw new StudioException(404, "Configuración inexistente.");
    }
}
public record NarrativeWrite(long Version, Narrative Definition);
public record ProducerNotesInput(long Version, string Notes, string Feedback, string Evidence, bool Approved);
public record EditorialVarietyEpisode(Guid Id, string Topic, Guid ContentNicheId, string NicheNameSnapshot, DateOnly PlannedDate, string Stage, string Archetype, string VisualStyle, string Question, bool Configured);
