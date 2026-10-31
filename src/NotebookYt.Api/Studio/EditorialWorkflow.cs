using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed record EditorialPreview(long Version, EditorialDraft Draft, Dictionary<string, string> Effective, Dictionary<string, string> Origins, Narrative[] Archetypes, List<ResearchSource> Sources, List<SimilarEpisode> Related, string SimilarityFingerprint, Dictionary<string, string> Outputs, List<string> Failures, List<string> Warnings, string PreviewHash, bool Frozen = false, Dictionary<string, string>? Inherited = null);

public static class EditorialWorkflow
{
    public static async Task<EditorialPreview> Preview(EditorialDb db, Guid id, bool frozen = true)
    {
        var p = await db.Productions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new StudioException(404, "Producción inexistente.");
        var profile = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
        if (frozen && profile?.ActiveSnapshotId is Guid snapshotId)
        {
            var snapshot = await db.Set<EditorialPackageSnapshot>().FindAsync(snapshotId) ?? throw new StudioException(409, "No se encuentra el snapshot aprobado.");
            return JsonSerializer.Deserialize<EditorialPreview>(snapshot.PayloadJson, EditorialIntelligence.Json)! with { Frozen = true };
        }
        var channel = await db.Set<ChannelSettings>().FindAsync(p.ChannelId);
        var niche = await db.Niches.FindAsync(p.ContentNicheId);
        var nicheSettings = await db.Set<NicheSettings>().FindAsync(p.ContentNicheId);
        var workspace = await db.Set<ProductionWorkspace>().FindAsync(id);
        var cd = EditorialIntelligence.Read(await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ChannelId == p.ChannelId));
        var nd = EditorialIntelligence.Read(await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ContentNicheId == p.ContentNicheId));
        var draft = EditorialIntelligence.Read(profile);
        var baseline = new Dictionary<string, string>
        {
            ["positioning"] = channel?.Description ?? "NewSphere Descubre", ["contentFamily"] = "Evergreen / NewSphere Descubre", ["audience"] = channel?.Audience ?? "Público general adulto", ["tone"] = channel?.Tone ?? "Claro y riguroso", ["guidelines"] = channel?.Guidelines ?? "",
            ["language"] = channel?.Language is null or "es" ? "es-ES" : channel.Language, ["archetype"] = "EXPLAINER", ["format"] = "EXPLAINER", ["visualStyle"] = "AUTOMATIC", ["desiredAspectRatio"] = "16:9", ["minimumDuration"] = "300", ["maximumDuration"] = "480",
            ["knowledgeLevel"] = "Introducción", ["humanReview"] = "Revisión humana con evidencia antes de publicar", ["sourceCriteria"] = "Priorizar fuentes primarias; registrar autoría, fecha, derechos y límites.", ["originalityStandards"] = "Aportación explicativa propia, contraste y ejemplos documentados; no basta cambiar el aspecto.", ["objective"] = workspace?.Objective ?? p.Topic,
            ["steeringInstructions"] = workspace?.VideoInstructions ?? "", ["prohibitedPractices"] = "No inventar hechos, citas ni fuentes. No presentar imágenes de IA como evidencia documental."
        };
        var nicheBase = new Dictionary<string, string> { ["subjectsIncluded"] = nicheSettings?.InclusionRules ?? niche?.Description ?? "", ["subjectsExcluded"] = nicheSettings?.ExclusionRules ?? "", ["approachesIncluded"] = nicheSettings?.VideoApproach ?? niche?.Guidance ?? "" };
        if (!string.IsNullOrWhiteSpace(nicheSettings?.Audience)) nicheBase["audience"] = nicheSettings.Audience;
        var inherited = EditorialIntelligence.Inherit(baseline, cd.Fields, nicheBase, nd.Fields);
        var f = EditorialIntelligence.Inherit(inherited, draft.Fields);
        var origins = baseline.Keys.ToDictionary(k => k, _ => "Channel");
        origins["objective"] = "Workspace"; origins["steeringInstructions"] = "Workspace";
        foreach (var k in cd.Fields.Keys) origins[k] = "Channel";
        foreach (var k in nicheBase.Keys.Concat(nd.Fields.Keys)) origins[k] = "Subniche";
        foreach (var k in draft.Fields.Keys) origins[k] = "ProductionOverride";
        var library = await EditorialIntelligence.Library(db); var narrative = library.SingleOrDefault(x => x.Id == f["archetype"]);
        var errors = EditorialIntelligence.Compatibility(f, draft);
        if (narrative is null) { errors.Add("Arquetipo desconocido."); narrative = library[1]; }
        if (!f.ContainsKey("structure")) { f["structure"] = narrative.Structure; origins["structure"] = "Archetype"; }
        if (!f.ContainsKey("visualDirection")) { f["visualDirection"] = narrative.VisualGuidance; origins["visualDirection"] = "Archetype"; }
        var allSources = await db.Set<ResearchSource>().Where(s => s.ProductionId == id).OrderBy(s => s.Id).ToListAsync();
        var sources = allSources.Where(s => draft.SourceIds.Contains(s.Id) && s.Included).ToList();
        if (draft.SourceIds.Any(s => !allSources.Any(a => a.Id == s && a.Included))) errors.Add("Hay fuentes seleccionadas ajenas a esta producción o excluidas del paquete.");
        if (sources.Count == 0 || sources.Any(s => string.IsNullOrWhiteSpace(s.Provenance))) errors.Add("Selecciona al menos una fuente con procedencia registrada.");
        foreach (var key in new[] { "objective", "centralQuestion", "thesis", "contribution", "audience", "structure", "hook", "evidencePlan", "visualDirection", "qualityRequirements" })
            if (string.IsNullOrWhiteSpace(f.GetValueOrDefault(key))) errors.Add($"Completa {FieldLabel(key)}.");
        if (f.GetValueOrDefault("centralQuestion", "").Trim().Length < 12) errors.Add("Concreta la pregunta central (al menos 12 caracteres; es una comprobación de longitud, no semántica).");
        var previous = await db.Productions.AsNoTracking().Where(x => x.ChannelId == p.ChannelId && x.Id != id && x.Status != ProductionStatus.Cancelled).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync();
        var ids = previous.Select(x => x.Id).ToArray();
        var profiles = await db.Set<EditorialConfiguration>().AsNoTracking().Where(x => x.ProductionId != null && ids.Contains(x.ProductionId.Value)).ToListAsync();
        var snapshotIds = profiles.Where(x => x.ActiveSnapshotId != null).Select(x => x.ActiveSnapshotId!.Value).ToArray();
        var snapshots = await db.Set<EditorialPackageSnapshot>().AsNoTracking().Where(x => snapshotIds.Contains(x.Id)).ToListAsync();
        var related = new List<SimilarEpisode>();
        foreach (var other in previous)
        {
            var otherProfile = profiles.SingleOrDefault(x => x.ProductionId == other.Id); var of = EditorialIntelligence.Read(otherProfile).Fields;
            if (otherProfile?.ActiveSnapshotId is Guid active) of = JsonSerializer.Deserialize<EditorialPreview>(snapshots.Single(x => x.Id == active).PayloadJson, EditorialIntelligence.Json)!.Effective;
            var overlaps = new List<string>();
            var score = EditorialIntelligence.Similarity(p.Topic, other.Topic);
            if (score >= .3) overlaps.Add("Tema/título");
            foreach (var key in new[] { "centralQuestion", "thesis", "contribution", "hook", "structure", "evidencePlan" })
            {
                var match = EditorialIntelligence.Similarity(f.GetValueOrDefault(key, ""), of.GetValueOrDefault(key, ""));
                if (match >= .5) { overlaps.Add(FieldLabel(key)); score = Math.Max(score, match); }
            }
            if (overlaps.Count > 0) related.Add(new(other.Id, other.Topic, Math.Round(score, 3), overlaps.ToArray(), new[] { "centralQuestion", "contribution", "archetype", "visualStyle" }.Where(k => f.GetValueOrDefault(k) != of.GetValueOrDefault(k)).Select(FieldLabel).ToArray(), "Coincidencia léxica Jaccard sobre metadatos disponibles; no demuestra duplicación factual ni requisitos de YouTube."));
        }
        var fingerprint = StudioRules.Hash(JsonSerializer.Serialize(related.OrderBy(x => x.ProductionId), EditorialIntelligence.Json));
        if (related.Count > 0 && (string.IsNullOrWhiteSpace(draft.SimilarityAcknowledgement) || draft.SimilarityFingerprint != fingerprint)) errors.Add("Revisa las coincidencias y registra una justificación actualizada; la continuidad de una serie es válida.");
        if (draft.RecommendationDecision is "Modified" or "Rejected" && string.IsNullOrWhiteSpace(draft.RecommendationRationale)) errors.Add("Explica por qué adaptas o rechazas la recomendación.");
        var warnings = new List<string> { "Composición determinista; no se ha realizado análisis semántico de fuentes ni verificación remota de ajustes.", "Duración y orientación son objetivos locales, no controles garantizados de NotebookLM." };
        if (draft.OutputEdits.Count > 0) warnings.Add("Hay bloques editados manualmente. Comprueba su coherencia con la configuración y el manifiesto de fuentes; no se ha verificado su contenido factual.");
        if (draft.StylesAvailability == "Unverified" && f["visualStyle"] != "AUTOMATIC") warnings.Add("Confirma en la interfaz oficial que tu cuenta ofrece el estilo solicitado (18+).");
        if (profiles.Count(x => EditorialIntelligence.Read(x).Fields.GetValueOrDefault("archetype") == f["archetype"]) >= 3) warnings.Add("Arquetipo frecuente: sugerencia de variedad, no fallo ni rotación obligatoria.");
        var inheritedNarrative = library.SingleOrDefault(x => x.Id == inherited["archetype"]) ?? library[1];
        inherited.TryAdd("structure", inheritedNarrative.Structure); inherited.TryAdd("visualDirection", inheritedNarrative.VisualGuidance);
        var preview = new EditorialPreview(profile?.Version ?? 0, draft, f, origins, library, sources, related.OrderByDescending(x => x.Score).ToList(), fingerprint, EditorialIntelligence.Compose(f, narrative, sources, draft), errors, warnings, "", Inherited: inherited);
        return preview with { PreviewHash = StudioRules.Hash(JsonSerializer.Serialize(preview, EditorialIntelligence.Json)) };
    }
    public static string FieldLabel(string key) => key switch { "objective" => "objetivo editorial", "centralQuestion" => "pregunta central", "thesis" => "premisa", "contribution" => "aportación original", "audience" => "público", "structure" => "estructura narrativa", "hook" => "apertura", "evidencePlan" => "plan de evidencias", "visualDirection" => "dirección visual", "qualityRequirements" => "criterios de calidad", "archetype" => "arquetipo", "visualStyle" => "estilo visual", _ => key };
    public static string Markdown(EditorialPreview preview) => string.Join("\n\n---\n\n", preview.Outputs.Values) + "\n\n## Manifiesto de configuración y fuentes seleccionadas\nEste bloque se compone de los datos estructurados y no se sustituye al editar las salidas.\n" + JsonSerializer.Serialize(new { schemaVersion = 1, configurationVersion = preview.Version, effective = preview.Effective, sources = preview.Sources.Select(s => new { s.Id, s.Title, s.Kind, s.Url, s.ArtifactId, s.Provenance, s.RegisteredAtUtc }) }, EditorialIntelligence.Json);
    public static async Task<SourcePackage> Freeze(EditorialDb db, Guid id, EditorialFreeze input)
    {
        await StudioRules.Editable(db, id); var profile = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id) ?? throw new StudioException(409, "Guarda primero la dirección editorial.");
        if (profile.Version != input.Version) throw new StudioException(409, "La revisión editorial cambió. Recarga antes de aprobar.");
        if (profile.ActiveSnapshotId is Guid active) return await db.Set<SourcePackage>().FindAsync((await db.Set<EditorialPackageSnapshot>().FindAsync(active))!.SourcePackageId) ?? throw new StudioException(409, "Paquete no disponible.");
        var preview = await Preview(db, id, false);
        if (input.PreviewHash != preview.PreviewHash) throw new StudioException(409, "El contexto, las fuentes o los ajustes cambiaron. Revisa la vista previa.");
        if (preview.Failures.Count > 0) throw new StudioException(409, string.Join(" ", preview.Failures));
        var version = (await db.Set<SourcePackage>().Where(x => x.ProductionId == id).MaxAsync(x => (int?)x.Version) ?? 0) + 1;
        var markdown = Markdown(preview);
        var package = new SourcePackage { ProductionId = id, Version = version, Markdown = markdown, Sha256 = StudioRules.Hash(markdown) };
        var payload = JsonSerializer.Serialize(preview, EditorialIntelligence.Json);
        var snapshot = new EditorialPackageSnapshot { ProductionId = id, SourcePackageId = package.Id, ConfigurationVersion = profile.Version, PayloadJson = payload, Sha256 = StudioRules.Hash(payload) };
        db.Add(package); db.Add(snapshot); profile.ActiveSnapshotId = snapshot.Id;
        foreach (var step in StudioRules.ManualSteps)
            if (!await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == id && t.Kind == step.Kind)) db.Add(new ManualNotebookTask { ProductionId = id, Kind = step.Kind, Instructions = step.Kind == "ConfigureVideoOverview" ? preview.Outputs["operator"] : step.Instruction, ExpectedResult = step.Expected });
        var w = await StudioRules.Workspace(db, id);
        w.Language = preview.Effective["language"]; w.Audience = preview.Effective["audience"]; w.Objective = preview.Effective["objective"];
        w.Stage = "NotebookPending"; w.ApprovedAtUtc = null;
        StudioRules.Event(db, id, "EditorialPackageApproved", JsonSerializer.Serialize(new { snapshot.Id, snapshot.Sha256, packageId = package.Id, version }));
        StudioRules.Event(db, id, "SourcePackageFrozen", package.Sha256);
        await db.SaveChangesAsync(); return package;
    }
    public static async Task<List<string>> ReviewFailures(EditorialDb db, Guid id, Guid? artifactId)
    {
        var result = new List<string>(); var profile = await db.Set<EditorialConfiguration>().SingleOrDefaultAsync(x => x.ProductionId == id);
        if (profile is null) return result; // Legacy packages retain their original approval contract.
        if (profile.ActiveSnapshotId is not Guid snapshotId) { result.Add("Falta aprobar el paquete editorial vigente."); return result; }
        var snapshot = await db.Set<EditorialPackageSnapshot>().FindAsync(snapshotId);
        if (snapshot is null || StudioRules.Hash(snapshot.PayloadJson) != snapshot.Sha256) { result.Add("El snapshot editorial no supera la comprobación de integridad."); return result; }
        var package = await db.Set<SourcePackage>().FindAsync(snapshot.SourcePackageId);
        if (package is null || StudioRules.Hash(package.Markdown) != package.Sha256) result.Add("El paquete de fuentes no supera la comprobación de integridad.");
        var latest = await db.Set<EditorialAssessment>().Where(x => x.ProductionId == id && x.SnapshotId == snapshotId && x.ArtifactId == artifactId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync();
        if (latest?.Approved != true) result.Add("Falta la revisión editorial detallada del vídeo y paquete actuales.");
        var preview = JsonSerializer.Deserialize<EditorialPreview>(snapshot.PayloadJson, EditorialIntelligence.Json)!;
        var artifact = artifactId is null ? null : await db.Set<MediaArtifact>().FindAsync(artifactId);
        if (artifact is not null)
        {
            if (preview.Effective["desiredAspectRatio"] == "16:9" && Math.Abs((double)artifact.Width / Math.Max(artifact.Height, 1) - 16d / 9) > .03) result.Add("El archivo no cumple el objetivo horizontal 16:9.");
            if (preview.Effective["desiredAspectRatio"] == "9:16" && Math.Abs((double)artifact.Width / Math.Max(artifact.Height, 1) - 9d / 16) > .03) result.Add("El archivo no cumple el objetivo vertical 9:16.");
            if (int.TryParse(preview.Effective["minimumDuration"], out var min) && artifact.DurationSeconds < min || int.TryParse(preview.Effective["maximumDuration"], out var max) && artifact.DurationSeconds > max) result.Add("El archivo no cumple el intervalo de duración editorial aprobado.");
        }
        return result;
    }
}
