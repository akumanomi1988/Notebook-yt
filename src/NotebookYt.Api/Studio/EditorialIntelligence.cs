using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class EditorialConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ChannelId { get; set; }
    public Guid? ContentNicheId { get; set; }
    public Guid? ProductionId { get; set; }
    public long Version { get; set; } = 1;
    public string DraftJson { get; set; } = "{}";
    public Guid? ActiveSnapshotId { get; set; }
}
public sealed class EditorialPackageSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public Guid SourcePackageId { get; set; }
    public long ConfigurationVersion { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Sha256 { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class NarrativeDefinition
{
    public string Id { get; set; } = "";
    public long Version { get; set; } = 1;
    public string DefinitionJson { get; set; } = "{}";
}
public sealed class EditorialAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid ArtifactId { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public bool Approved { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed record Narrative(string Id, string Name, string Description, string Purpose, string Structure, string SuitableTopics, string VisualGuidance, string Weaknesses, string SteeringTemplate, string RecommendationCriteria);
public sealed class EditorialDraft
{
    public Dictionary<string, string> Fields { get; set; } = [];
    public List<Guid> SourceIds { get; set; } = [];
    public Dictionary<string, string> OutputEdits { get; set; } = [];
    public string SimilarityAcknowledgement { get; set; } = "";
    public string SimilarityFingerprint { get; set; } = "";
    public string RecommendationDecision { get; set; } = "Pending";
    public string RecommendationRationale { get; set; } = "";
    public string ProducerNotesAvailability { get; set; } = "Unverified";
    public string ProducerNotesEvidence { get; set; } = "";
    public string CinematicAvailability { get; set; } = "Unverified";
    public string CinematicEvidence { get; set; } = "";
    public string StylesAvailability { get; set; } = "Unverified";
    public string StylesEvidence { get; set; } = "";
    public bool RequestProducerNotes { get; set; }
    public string ProducerNotes { get; set; } = "";
    public string ReviewedProducerNotes { get; set; } = "";
    public string RevisionFeedback { get; set; } = "";
    public bool ProducerNotesApproved { get; set; }
}
public record EditorialWrite(long Version, EditorialDraft Draft);
public record EditorialFreeze(long Version, string PreviewHash);
public record EditorialEvidence(long Version, string Evidence, bool Attested, string Format, string Language, string VisualStyle, List<Guid> SourceIds);
public record EditorialReviewInput(long Version, Guid ArtifactId, bool Approved, Dictionary<string, string> Evidence);
public record SimilarEpisode(Guid ProductionId, string Topic, double Score, string[] Overlap, string[] Differences, string Evidence);

public static class EditorialIntelligence
{
    public const string Documentation = "https://support.google.com/gemininotebook/answer/16454555?hl=en";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] FieldKeys = ["positioning", "contentFamily", "audience", "knowledgeLevel", "educationalObjectives", "guidelines", "prohibitedPractices", "sourceCriteria", "originalityStandards", "humanReview", "metadataPreferences", "subjectsIncluded", "subjectsExcluded", "approachesIncluded", "approachesExcluded", "evergreenCriteria", "objective", "centralQuestion", "thesis", "contribution", "archetype", "structure", "hook", "evidencePlan", "tone", "pacing", "depth", "visualDirection", "format", "language", "visualStyle", "customVisualStyle", "steeringInstructions", "restrictions", "qualityRequirements", "desiredAspectRatio", "minimumDuration", "maximumDuration", "seriesRationale"];
    public static readonly string[] ReviewCriteria = ["answersQuestion", "narrativeProgression", "educationalValue", "naturalNarration", "sourceFidelity", "appropriateVisuals", "differentiation", "artifactsAndErrors", "brandStandards", "rightsAndDisclosures"];
    public static readonly string[] Styles = ["AUTOMATIC", "CLASSIC", "WHITEBOARD", "WATERCOLOR", "RETRO_PRINT", "HERITAGE", "PAPER_CRAFT", "KAWAII", "ANIME", "CUSTOM"];
    public static readonly Dictionary<string, string> Languages = new()
    {
        ["es-ES"] = "Español (España)", ["es-419"] = "Español (Latinoamérica)", ["es-MX"] = "Español (México)", ["en"] = "Inglés", ["fr-FR"] = "Francés europeo", ["fr-CA"] = "Francés (Canadá)", ["de"] = "Alemán", ["it"] = "Italiano", ["pt-BR"] = "Portugués (Brasil)", ["pt-PT"] = "Portugués (Portugal)", ["ca"] = "Catalán", ["eu"] = "Euskera", ["gl"] = "Gallego", ["ja"] = "Japonés", ["ko"] = "Coreano", ["zh-Hans"] = "Chino simplificado", ["zh-Hant"] = "Chino tradicional", ["ar-EG"] = "Árabe egipcio", ["ar"] = "Árabe estándar", ["hi"] = "Hindi", ["uk"] = "Ucraniano", ["nl"] = "Neerlandés", ["pl"] = "Polaco", ["sv"] = "Sueco", ["tr"] = "Turco", ["vi"] = "Vietnamita",
        ["af"] = "Afrikáans", ["sq"] = "Albanés", ["am"] = "Amárico", ["hy"] = "Armenio", ["az"] = "Azerí", ["be"] = "Bielorruso", ["bn"] = "Bengalí", ["bg"] = "Búlgaro", ["my"] = "Birmano", ["ceb"] = "Cebuano", ["hr"] = "Croata", ["cs"] = "Checo", ["da"] = "Danés", ["et"] = "Estonio", ["fil"] = "Filipino", ["fi"] = "Finés", ["ka"] = "Georgiano", ["el"] = "Griego", ["gu"] = "Guyaratí", ["ht"] = "Criollo haitiano", ["he"] = "Hebreo", ["hu"] = "Húngaro", ["is"] = "Islandés", ["id"] = "Indonesio", ["jv"] = "Javanés", ["kn"] = "Canarés", ["kok"] = "Konkani", ["la"] = "Latín", ["lv"] = "Letón", ["lt"] = "Lituano", ["mk"] = "Macedonio", ["mai"] = "Maithili", ["ms"] = "Malayo", ["ml"] = "Malayalam", ["mr"] = "Maratí", ["ne"] = "Nepalí", ["nb"] = "Noruego bokmål", ["nn"] = "Noruego nynorsk", ["or"] = "Oriya", ["ps"] = "Pastún", ["fa"] = "Persa", ["pa"] = "Panyabí", ["ro"] = "Rumano", ["ru"] = "Ruso", ["sr-Cyrl"] = "Serbio cirílico", ["sd"] = "Sindhi", ["si"] = "Cingalés", ["sk"] = "Eslovaco", ["sl"] = "Esloveno", ["sw"] = "Suajili", ["ta"] = "Tamil", ["te"] = "Telugu", ["th"] = "Tailandés", ["ur"] = "Urdu"
    };
    public static readonly string[] ExplainerOnlyLanguages = ["my", "ht", "kok", "la", "mai", "nb", "nn", "or", "ps", "sd", "th"];
    public static readonly Narrative[] Presets = [
        new("INVESTIGATION", "Investigación", "Contrasta explicaciones para responder una pregunta.", "Razonar con evidencias", "Pregunta → contexto → hipótesis → pruebas → conclusión razonada", "Preguntas abiertas con varias explicaciones", "Relaciona cada hipótesis con sus pruebas", "No fabricar misterio ni falsa equivalencia", "Investiga {question}; distingue hipótesis, pruebas y límites.", "pregunta, evidencia, por qué"),
        new("EXPLAINER", "Explicación", "Explica cómo funciona un concepto o mecanismo.", "Comprender mecanismos", "Problema → fundamentos → mecanismo → ejemplos → implicaciones", "Procesos y conceptos", "Diagramas causales y ejemplos progresivos", "Evita enumeraciones sin explicar relaciones", "Explica {question} para {audience} con ejemplos respaldados.", "cómo, funciona, proceso"),
        new("HISTORICAL_STORY", "Relato histórico", "Conecta decisiones, cambios y consecuencias.", "Comprender el cambio histórico", "Origen → puntos de inflexión → decisiones → consecuencias → legado", "Historia y evolución documentada", "Cronología y mapas con contexto", "Evita anacronismos y atribuciones inventadas", "Narra {question} distinguiendo hechos e interpretaciones.", "historia, origen, evolución"),
        new("COMPARISON", "Comparación", "Compara alternativas con criterios explícitos.", "Comprender ventajas y límites", "Alternativas → criterios → ventajas y límites → compromisos → conclusión", "Alternativas comparables", "Matrices y ejemplos equivalentes", "No forzar un ganador ni mezclar criterios", "Compara {question} con criterios verificables.", "frente, diferencias, comparar"),
        new("MYTHS_AND_EVIDENCE", "Mitos y evidencias", "Examina afirmaciones populares con rigor.", "Distinguir afirmaciones y pruebas", "Creencia → evidencia → objeciones → hallazgos → comprensión corregida", "Afirmaciones contrastables", "Diferencia afirmación y evidencia visualmente", "No repetir mitos sin contexto ni sobregeneralizar", "Examina {question}; indica qué sabemos y qué no.", "mito, cierto, creencia"),
        new("CASE_STUDY", "Estudio de caso", "Analiza un caso real sin extrapolar en exceso.", "Aprender de decisiones concretas", "Contexto → reto → decisiones → resultados → lecciones", "Casos documentados", "Relaciona acciones y resultados con fuentes", "Una anécdota no demuestra una regla general", "Analiza {question} y los límites de extrapolación.", "caso, experiencia, decisiones"),
        new("FUTURE_SCENARIOS", "Escenarios de futuro", "Explora posibilidades condicionales.", "Comprender incertidumbres", "Situación actual → impulsores → escenarios → incertidumbres → implicaciones", "Tendencias con incertidumbre explícita", "Escenarios diferenciados de predicciones", "No presentar especulación como hecho", "Explora {question} como escenarios condicionales, no predicciones.", "futuro, podría, escenarios"),
        new("CUSTOM", "Personalizado", "Define tu propio enfoque y estructura.", "Adaptar la narración al tema", "", "Cualquier tema con una estructura justificada", "Dirección visual explícita", "No confundir variación estética con aportación original", "Desarrolla {question} siguiendo la estructura editorial aprobada.", "")
    ];
    public static void Configure(ModelBuilder model)
    {
        var e = model.Entity<EditorialConfiguration>();
        e.HasIndex(x => x.ChannelId).IsUnique(); e.HasIndex(x => x.ContentNicheId).IsUnique(); e.HasIndex(x => x.ProductionId).IsUnique();
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ContentNiche>().WithMany().HasForeignKey(x => x.ContentNicheId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_EditorialConfiguration_Scope", "num_nonnulls(\"ChannelId\", \"ContentNicheId\", \"ProductionId\") = 1 AND \"Version\" > 0"));
        var s = model.Entity<EditorialPackageSnapshot>(); s.HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<SourcePackage>().WithMany().HasForeignKey(x => x.SourcePackageId).OnDelete(DeleteBehavior.Restrict);
        s.HasIndex(x => new { x.ProductionId, x.ConfigurationVersion }).IsUnique();
        e.HasOne<EditorialPackageSnapshot>().WithMany().HasForeignKey(x => x.ActiveSnapshotId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NarrativeDefinition>().Property(x => x.Version).IsConcurrencyToken();
        var r = model.Entity<EditorialAssessment>(); r.HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<EditorialPackageSnapshot>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<MediaArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
    }
    public static EditorialDraft Read(EditorialConfiguration? c) => c is null ? new() : JsonSerializer.Deserialize<EditorialDraft>(c.DraftJson, Json)!;
    public static void ValidateDraft(EditorialDraft? d)
    {
        if (d is null || d.Fields is null || d.SourceIds is null || d.OutputEdits is null || d.SourceIds.Count > 100 || d.SourceIds.Distinct().Count() != d.SourceIds.Count) throw new StudioException(400, "Configuración editorial incompleta o fuentes duplicadas.");
        foreach (var (key, value) in d.Fields) { if (!FieldKeys.Contains(key)) throw new StudioException(400, "Campo editorial desconocido."); StudioRules.Text(value, 12000); }
        if (d.Fields.TryGetValue("format", out var format)) StudioRules.Choice(format, "EXPLAINER", "SHORT", "CINEMATIC");
        if (d.Fields.TryGetValue("visualStyle", out var style)) StudioRules.Choice(style, Styles);
        if (d.Fields.TryGetValue("language", out var language) && !Languages.ContainsKey(language)) throw new StudioException(400, "Idioma no incluido en el catálogo documentado local.");
        foreach (var (key, value) in d.OutputEdits) { StudioRules.Choice(key, "research", "document", "steering", "operator"); StudioRules.Text(value, 40000); }
        StudioRules.Choice(d.RecommendationDecision, "Pending", "Accepted", "Modified", "Rejected");
        foreach (var v in new[] { d.ProducerNotesAvailability, d.CinematicAvailability, d.StylesAvailability }) StudioRules.Choice(v, "Unverified", "Available", "Unavailable");
        foreach (var v in new[] { d.SimilarityAcknowledgement, d.SimilarityFingerprint, d.RecommendationRationale, d.ProducerNotesEvidence, d.CinematicEvidence, d.StylesEvidence, d.ProducerNotes, d.ReviewedProducerNotes, d.RevisionFeedback }) StudioRules.Text(v, 12000);
    }
    public static Dictionary<string, string> Inherit(params Dictionary<string, string>[] levels)
    {
        var result = new Dictionary<string, string>(); foreach (var level in levels) foreach (var (key, value) in level) result[key] = value; return result;
    }
    public static List<string> Compatibility(Dictionary<string, string> f, EditorialDraft d)
    {
        var errors = new List<string>(); string V(string key) => f.GetValueOrDefault(key, "");
        if (V("format") is not ("EXPLAINER" or "SHORT" or "CINEMATIC")) errors.Add("Formato de NotebookLM no admitido.");
        if (!Languages.ContainsKey(V("language"))) errors.Add("Selecciona un idioma documentado en el catálogo local.");
        if (V("format") == "SHORT" && ExplainerOnlyLanguages.Contains(V("language"))) errors.Add("Ese idioma no figura en el catálogo documentado de Short; usa Explainer o un idioma compatible.");
        if (!Styles.Contains(V("visualStyle"))) errors.Add("Estilo visual no admitido.");
        if (V("format") == "CINEMATIC" && (V("language") != "en" || d.CinematicAvailability != "Available" || string.IsNullOrWhiteSpace(d.CinematicEvidence))) errors.Add("Cinematic exige inglés y evidencia de acceso Ultra y mayoría de edad en la cuenta.");
        if (V("format") != "EXPLAINER" && V("visualStyle") != "AUTOMATIC") errors.Add("Short y Cinematic no admiten selección de estilo visual.");
        if (V("visualStyle") == "CUSTOM" && string.IsNullOrWhiteSpace(V("customVisualStyle"))) errors.Add("Describe el estilo visual personalizado.");
        if (V("visualStyle") != "AUTOMATIC" && (d.StylesAvailability == "Unavailable" || (d.StylesAvailability == "Available" && string.IsNullOrWhiteSpace(d.StylesEvidence)))) errors.Add("El estilo requiere acceso de mayores de 18 años; registra evidencia o deja la disponibilidad sin verificar.");
        if (d.RequestProducerNotes && (d.ProducerNotesAvailability != "Available" || string.IsNullOrWhiteSpace(d.ProducerNotesEvidence))) errors.Add("Las notas del productor requieren acceso Ultra confirmado por el operador con evidencia.");
        if (!int.TryParse(V("minimumDuration"), out var min) || !int.TryParse(V("maximumDuration"), out var max) || min < 1 || max < min || max > 14400) errors.Add("Revisa el objetivo local de duración.");
        if (V("desiredAspectRatio") is not ("16:9" or "9:16" or "Any")) errors.Add("Selecciona un objetivo local de orientación válido.");
        return errors;
    }
    public static async Task<Narrative[]> Library(EditorialDb db)
    {
        var edits = await db.Set<NarrativeDefinition>().AsNoTracking().ToListAsync();
        return Presets.Select(p => edits.Any(e => e.Id == p.Id) ? JsonSerializer.Deserialize<Narrative>(edits.Single(e => e.Id == p.Id).DefinitionJson, Json)! : p).Concat(edits.Where(e => !Presets.Any(p => p.Id == e.Id)).Select(e => JsonSerializer.Deserialize<Narrative>(e.DefinitionJson, Json)!)).ToArray();
    }
    public static HashSet<string> Tokens(string value) => Regex.Matches(string.Concat(value.Normalize(NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)).ToLowerInvariant(), @"[a-z0-9]{3,}").Select(m => m.Value).Where(v => !new[] { "para", "como", "los", "las", "del", "una", "que", "con", "por", "este", "esta", "sobre" }.Contains(v)).ToHashSet();
    public static double Similarity(string a, string b)
    {
        var left = Tokens(a); var right = Tokens(b); var union = left.Union(right).Count(); return union == 0 ? 0 : (double)left.Intersect(right).Count() / union;
    }
    public static object Recommend(Dictionary<string, string> f, string topic, int sourceCount, string[] recent, Narrative[]? library = null)
    {
        var text = topic + " " + string.Join(' ', new[] { "centralQuestion", "subjectsIncluded", "evidencePlan", "contribution", "educationalObjectives" }.Select(k => f.GetValueOrDefault(k, "")));
        var ranked = (library ?? Presets).Where(p => p.Id != "CUSTOM").Select(p => new { preset = p, score = p.RecommendationCriteria.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Count(term => Tokens(term).Count > 0 && Tokens(term).IsSubsetOf(Tokens(text))) }).OrderByDescending(x => x.score).ThenBy(x => x.preset.Id == "EXPLAINER" ? 0 : 1).ToArray();
        var best = ranked[0].preset;
        var concerns = new List<string>();
        if (recent.Count(x => x == best.Id) >= 3) concerns.Add("Enfoque repetido recientemente; puede ser adecuado para una serie. Justifica tu decisión, no es un bloqueo automático.");
        if (sourceCount == 0) concerns.Add("Selecciona fuentes antes de desarrollar afirmaciones; esta recomendación no acredita evidencia.");
        return new { method = "DeterministicLexicalRules", aiAssisted = false, archetype = best.Id, alternative = ranked[1].preset.Id, structure = best.Structure, angle = $"Responder a {f.GetValueOrDefault("centralQuestion", topic)} delimitando lo que permiten las fuentes.", contribution = "Construir una explicación propia con un ejemplo contrastado y explicitar sus límites; requiere desarrollo del operador.", visualStyle = f.GetValueOrDefault("depth", "").Contains("avanz", StringComparison.OrdinalIgnoreCase) ? "CLASSIC" : "WHITEBOARD", steering = best.SteeringTemplate.Replace("{question}", f.GetValueOrDefault("centralQuestion", topic)).Replace("{audience}", f.GetValueOrDefault("audience", "público general")), explanation = $"Regla léxica orientativa: {best.Description} Fuentes seleccionadas: {sourceCount}. Público: {f.GetValueOrDefault("audience", "sin definir")}; nivel: {f.GetValueOrDefault("knowledgeLevel", "sin definir")}; profundidad: {f.GetValueOrDefault("depth", "sin definir")}. No se ha analizado semánticamente el contenido ni la complejidad real.", concerns };
    }
    public static Dictionary<string, string> Compose(Dictionary<string, string> f, Narrative narrative, List<ResearchSource> sources, EditorialDraft draft)
    {
        string V(string key) => f.GetValueOrDefault(key, "");
        var citations = string.Join("\n", sources.Select(s => $"- [{s.Id}] {s.Title}\n  URL: {s.Url}; documento: {s.ArtifactId}\n  Procedencia declarada: {s.Provenance}\n  Registrada UTC: {s.RegisteredAtUtc:O}\n  Notas del operador (no verificadas automáticamente): {s.Notes}"));
        var outline = $"Objetivo: {V("objective")}\nPregunta central: {V("centralQuestion")}\nPremisa a contrastar: {V("thesis")}\nAportación original prevista: {V("contribution")}\nPúblico: {V("audience")}\nConocimientos previos: {V("knowledgeLevel")}\nObjetivos educativos: {V("educationalObjectives")}\nEnfoque: {narrative.Name}\nEstructura: {V("structure")}\nApertura: {V("hook")}\nPlan de evidencia: {V("evidencePlan")}";
        var steering = $"{outline}\nTono: {V("tone")}; ritmo: {V("pacing")}; profundidad: {V("depth")}\nVisuales: {V("visualDirection")}\nEstilo solicitado: {V("visualStyle")} {V("customVisualStyle")}\nMatices y restricciones: {V("restrictions")}\nNo incluir: {V("subjectsExcluded")}\nEnfoques preferidos: {V("approachesIncluded")}\nEnfoques excluidos: {V("approachesExcluded")}\nNormas: {V("guidelines")}\nPrácticas prohibidas: {V("prohibitedPractices")}\nRequisitos de calidad: {V("qualityRequirements")}\nInstrucciones adicionales: {V("steeringInstructions")}\n{narrative.SteeringTemplate.Replace("{question}", V("centralQuestion")).Replace("{audience}", V("audience"))}\nCierra respondiendo la pregunta sin introducir datos nuevos. Atribuye las afirmaciones a las fuentes seleccionadas. No inventes citas ni cifras.";
        var result = new Dictionary<string, string>
        {
            ["research"] = $"# Plan de investigación\n{outline}\nCriterios de fuentes: {V("sourceCriteria")}\nVerifica autoría, fecha, derechos, alcance y contradicciones antes de narrar. Las URLs registradas no implican verificación factual.\n## Fuentes seleccionadas\n{citations}",
            ["document"] = $"# Documento editorial original\nInstrucciones y síntesis planificada del operador, no una fuente factual independiente. Ninguna premisa constituye un hallazgo verificado.\n{outline}\nPosicionamiento: {V("positioning")}\nFamilia: {V("contentFamily")}\nOriginalidad: {V("originalityStandards")}\nDiferencias de serie: {V("seriesRationale")}\n## Referencias y notas aportadas\n{citations}\n## Límites\nLas notas anteriores requieren contraste con sus originales. No constituyen citas textuales certificadas.",
            ["steering"] = steering,
            ["operator"] = $"# Pasos en la interfaz oficial\n1. Abre o crea un notebook privado en https://notebook.google.com/.\n2. Incorpora el documento editorial y las fuentes seleccionadas ({sources.Count}); comprueba su procesamiento.\n3. Marca únicamente los identificadores de fuente del paquete; concílialos con los nombres reales del notebook. No se ha aplicado esta selección remotamente.\n4. Abre Studio → Video Overview. Selecciona formato {V("format")} e idioma {Languages.GetValueOrDefault(V("language"), V("language"))}.\n5. " + (V("format") == "EXPLAINER" ? $"Selecciona estilo {V("visualStyle")}. Si es Custom, introduce: {V("customVisualStyle")}. Confirma que el estilo esté disponible en tu cuenta." : "No selecciones estilo visual: este formato no ofrece ese control.") + $"\n6. Copia las instrucciones de enfoque. Revisa compatibilidad y disponibilidad reales.\n7. " + (draft.RequestProducerNotes ? $"Activa Review producer notes (Ultra), genera las notas y revísalas antes de Proceed. Notas solicitadas: {draft.ProducerNotes}" : "No solicites notas del productor si tu cuenta no las ofrece.") + $"\n8. Registra aquí los ajustes realmente usados y evidencia. Genera en NotebookLM, espera el resultado y reprodúcelo. Una declaración local no demuestra éxito remoto.\n9. Descarga con Download e importa el original para inspección técnica y revisión editorial.\n\nObjetivos locales, no parámetros de NotebookLM: orientación {V("desiredAspectRatio")}, duración {V("minimumDuration")}–{V("maximumDuration")} segundos. Se comprueban sobre el archivo, no se garantizan al generar.\nDocumentación: {Documentation}\nRevisada: 2026-10-31. Disponibilidad de cuenta: declarada por operador, no verificada por API."
        };
        foreach (var (key, value) in draft.OutputEdits) result[key] = value;
        return result;
    }
}
