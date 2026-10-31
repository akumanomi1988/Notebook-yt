using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class EditorialIntelligenceTests
{
    [Fact]
    public void InheritancePreservesExplicitOverridesWithoutMutatingDefaults()
    {
        var channel = new Dictionary<string, string> { ["tone"] = "Neutral", ["language"] = "es-ES" };
        var niche = new Dictionary<string, string> { ["tone"] = "Analítico" };
        var result = EditorialIntelligence.Inherit(channel, niche, new() { ["tone"] = "Cercano" });
        Assert.Equal("Cercano", result["tone"]); Assert.Equal("es-ES", result["language"]); Assert.Equal("Neutral", channel["tone"]);
    }
    [Theory]
    [InlineData("INVESTIGATION")][InlineData("EXPLAINER")][InlineData("HISTORICAL_STORY")][InlineData("COMPARISON")][InlineData("MYTHS_AND_EVIDENCE")][InlineData("CASE_STUDY")][InlineData("FUTURE_SCENARIOS")][InlineData("CUSTOM")]
    public void LibraryHasStableEditableDefinitions(string id)
    {
        var n = Assert.Single(EditorialIntelligence.Presets, n => n.Id == id);
        Assert.NotEmpty(n.Name); Assert.NotEmpty(n.Purpose); Assert.NotEmpty(n.VisualGuidance); Assert.NotEmpty(n.SteeringTemplate);
    }
    [Theory]
    [InlineData("CINEMATIC", "es-ES", "AUTOMATIC", false)]
    [InlineData("CINEMATIC", "en", "AUTOMATIC", true)]
    [InlineData("SHORT", "es-ES", "CUSTOM", false)]
    [InlineData("SHORT", "es-ES", "WHITEBOARD", false)]
    [InlineData("EXPLAINER", "es-ES", "CUSTOM", true)]
    [InlineData("INVALID", "es-ES", "AUTOMATIC", false)]
    public void NotebookOptionsEnforceDocumentedCompatibility(string format, string language, string style, bool valid)
    {
        var fields = new Dictionary<string, string> { ["format"] = format, ["language"] = language, ["visualStyle"] = style, ["customVisualStyle"] = "Diagramas claros", ["minimumDuration"] = "300", ["maximumDuration"] = "480", ["desiredAspectRatio"] = "16:9" };
        var draft = new EditorialDraft { CinematicAvailability = "Available", CinematicEvidence = "Operator observed Ultra entitlement and 18+ account" };
        Assert.Equal(valid, EditorialIntelligence.Compatibility(fields, draft).Count == 0);
        draft.CinematicAvailability = "Unverified";
        if (format == "CINEMATIC") Assert.NotEmpty(EditorialIntelligence.Compatibility(fields, draft));
    }
    [Fact]
    public void ProducerNotesNeedAccountEvidenceAndCustomStyleNeedsDescription()
    {
        var d = new EditorialDraft { RequestProducerNotes = true };
        var f = new Dictionary<string, string> { ["format"] = "EXPLAINER", ["language"] = "es-ES", ["visualStyle"] = "CUSTOM", ["minimumDuration"] = "1", ["maximumDuration"] = "10", ["desiredAspectRatio"] = "16:9" };
        Assert.Contains(EditorialIntelligence.Compatibility(f, d), e => e.Contains("Ultra"));
        Assert.Contains(EditorialIntelligence.Compatibility(f, d), e => e.Contains("personalizado"));
    }
    [Fact]
    public void LexicalComparisonDistinguishesRelatedSubjectsWithoutPenalizingOnlyAnArchetype()
    {
        Assert.True(EditorialIntelligence.Similarity("Cómo funcionan los satélites de observación", "Satélites de observación de la Tierra") > .3);
        Assert.Equal(0, EditorialIntelligence.Similarity("Satélites", "Historia medieval"));
        Assert.Equal(0, EditorialIntelligence.Similarity("", ""));
        Assert.Equal(1, EditorialIntelligence.Similarity("Árboles", "árboles"));
        Assert.Equal(1, EditorialIntelligence.Similarity("observación", "observacion"));
    }
    [Fact]
    public void ComposerIncludesOnlySelectedSourcesAndSeparatesManualEdits()
    {
        var source = new ResearchSource { Title = "ESA notes", Notes = "Operator-supplied material", Provenance = "Official source" };
        var draft = new EditorialDraft { OutputEdits = new() { ["steering"] = "Manual final prompt" } };
        var f = new Dictionary<string, string> { ["centralQuestion"] = "¿Cómo observan los satélites?", ["format"] = "EXPLAINER", ["language"] = "es-ES" };
        var result = EditorialIntelligence.Compose(f, EditorialIntelligence.Presets[1], [source], draft);
        Assert.Contains(source.Id.ToString(), result["research"]); Assert.Contains("no una fuente factual", result["document"]);
        Assert.Equal("Manual final prompt", result["steering"]); Assert.Equal("¿Cómo observan los satélites?", f["centralQuestion"]);
        Assert.Contains("No se ha aplicado", result["operator"]);
    }
    [Fact]
    public void RecommendationIsExplicitlyHeuristicAndNeverSelectsShortAutomatically()
    {
        var json = JsonSerializer.Serialize(EditorialIntelligence.Recommend([], "Cómo funciona un satélite", 2, []), EditorialIntelligence.Json);
        Assert.Contains("DeterministicLexicalRules", json); Assert.Contains("EXPLAINER", json); Assert.DoesNotContain("\"format\":\"SHORT\"", json);
    }
}

internal static class EditorialTestSupport
{
    public static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(EditorialIntelligence.Json))!;
    }
    public static async Task<EditorialPreview> Prepare(HttpClient client, Guid id, bool approve = true)
    {
        var path = $"/api/studio/editorial/productions/{id}";
        var p = await Read<EditorialPreview>(await client.GetAsync(path));
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/studio/productions/{id}");
        p.Draft.SourceIds = detail.GetProperty("sources").EnumerateArray().Where(s => s.GetProperty("included").GetBoolean()).Select(s => s.GetProperty("id").GetGuid()).ToList();
        foreach (var key in new[] { "centralQuestion", "thesis", "contribution", "hook", "evidencePlan", "qualityRequirements" }) p.Draft.Fields[key] = $"Controlled evidence for {key}, production {id}; not a genuine provider acceptance";
        p.Draft.Fields["minimumDuration"] = "1"; p.Draft.Fields["maximumDuration"] = "14400";
        p = await Read<EditorialPreview>(await client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft)));
        if (p.Related.Count > 0)
        {
            p.Draft.SimilarityAcknowledgement = "A controlled recurring series test; explicit differences reviewed."; p.Draft.SimilarityFingerprint = p.SimilarityFingerprint;
            p = await Read<EditorialPreview>(await client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft)));
        }
        Assert.Empty(p.Failures);
        if (approve) await Read<SourcePackage>(await client.PostAsJsonAsync(path + "/approve", new EditorialFreeze(p.Version, p.PreviewHash)));
        return p;
    }
    public static async Task Attest(HttpClient client, Guid id)
    {
        var path = $"/api/studio/editorial/productions/{id}"; var p = await Read<EditorialPreview>(await client.GetAsync(path));
        await Read<JsonElement>(await client.PostAsJsonAsync(path + "/actual-settings", new EditorialEvidence(p.Version, "Controlled operator testimony; not provider verification", true, p.Effective["format"], p.Effective["language"], p.Effective["visualStyle"], p.Sources.Select(s => s.Id).ToList())));
    }
    public static async Task Review(HttpClient client, Guid id, Guid artifactId)
    {
        var path = $"/api/studio/editorial/productions/{id}"; var p = await Read<EditorialPreview>(await client.GetAsync(path));
        await Read<JsonElement>(await client.PostAsJsonAsync(path + "/review", new EditorialReviewInput(p.Version, artifactId, true, EditorialIntelligence.ReviewCriteria.ToDictionary(k => k, k => "Controlled reviewer evidence: " + k))));
    }
}

public class EditorialWorkflowTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private HttpClient Client => fixture.Client;
    private async Task<(Guid Id, Guid Channel, Guid Niche)> Create()
    {
        var ch = await EditorialTestSupport.Read<JsonElement>(await Client.PutAsJsonAsync("/api/channel", new { name = "Editorial integration", timeZone = "Europe/Madrid" }));
        var week = await Client.GetFromJsonAsync<JsonElement>("/api/week"); var niche = week[0].GetProperty("id").GetGuid();
        var p = await EditorialTestSupport.Read<JsonElement>(await Client.PostAsJsonAsync("/api/productions", new { contentNicheId = niche, topic = "Orbital mechanisms " + Guid.NewGuid(), plannedDate = "2027-01-05" }));
        var id = p.GetProperty("id").GetGuid();
        await EditorialTestSupport.Read<JsonElement>(await Client.PostAsJsonAsync($"/api/studio/productions/{id}/sources", new { title = "Controlled note", kind = "Note", notes = "Research evidence authored for integration only", url = "", classification = "Primary", provenance = "Test author", included = true, artifactId = (Guid?)null }));
        return (id, ch.GetProperty("id").GetGuid(), niche);
    }
    [Fact]
    public async Task DefaultsOverrideFreezeRevisionAndRestartPreserveEvidence()
    {
        var (id, channel, niche) = await Create();
        async Task SetDefault(string scope, Guid key, string tone) {
            var path = $"/api/studio/editorial/defaults/{scope}/{key}";
            var old = await Client.GetFromJsonAsync<EditorialWrite>(path);
            await EditorialTestSupport.Read<EditorialWrite>(await Client.PutAsJsonAsync(path, new EditorialWrite(old!.Version, new() { Fields = new() { ["tone"] = tone } })));
        }
        await SetDefault("channel", channel, "Canal"); await SetDefault("niche", niche, "Nicho");
        var path = $"/api/studio/editorial/productions/{id}";
        var p = await Client.GetFromJsonAsync<EditorialPreview>(path); Assert.Equal("Nicho", p!.Effective["tone"]);
        p.Draft.Fields["tone"] = "Episodio";
        p.Draft.Fields["language"] = "fr-FR";
        p = await EditorialTestSupport.Read<EditorialPreview>(await Client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft)));
        Assert.Equal("ProductionOverride", p.Origins["tone"]);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(path + "/approve", new EditorialFreeze(p.Version, "stale"))).StatusCode);
        p = await EditorialTestSupport.Prepare(Client, id);
        var frozen = await Client.GetFromJsonAsync<EditorialPreview>(path); Assert.True(frozen!.Frozen);
        var frozenWorkspace = await Client.GetFromJsonAsync<JsonElement>($"/api/studio/productions/{id}");
        Assert.Equal("fr-FR", frozenWorkspace.GetProperty("workspace").GetProperty("language").GetString());
        await SetDefault("channel", channel, "Changed after freeze"); Assert.Equal("Episodio", (await Client.GetFromJsonAsync<EditorialPreview>(path))!.Effective["tone"]);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft))).StatusCode);
        var oldPackage = await Client.GetFromJsonAsync<JsonElement>($"/api/studio/productions/{id}");
        await EditorialTestSupport.Read<EditorialPreview>(await Client.PostAsJsonAsync(path + "/revision", new EditorialFreeze(p.Version, p.PreviewHash)));
        await EditorialTestSupport.Prepare(Client, id);
        var current = await Client.GetFromJsonAsync<JsonElement>($"/api/studio/productions/{id}"); Assert.Equal(2, current.GetProperty("packages").GetArrayLength());
        Assert.Contains(current.GetProperty("packages").EnumerateArray(), x => x.GetProperty("sha256").GetString() == oldPackage.GetProperty("packages")[0].GetProperty("sha256").GetString());
        using var restarted = fixture.CreateFactory(); using var client = restarted.CreateClient(); var restored = await client.GetFromJsonAsync<EditorialPreview>(path);
        Assert.True(restored!.Frozen); Assert.Equal("Episodio", restored.Effective["tone"]);
        Assert.Contains("paquete", string.Join(' ', await EditorialWorkflow.ReviewFailures(Context(), id, null)), StringComparison.OrdinalIgnoreCase);
    }
    private EditorialDb Context() => new(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
    [Fact]
    public async Task ConcurrentEditsUnknownFieldsAndSourceMixingAreRejected()
    {
        var (id, _, _) = await Create(); var path = $"/api/studio/editorial/productions/{id}";
        var p = await Client.GetFromJsonAsync<EditorialPreview>(path);
        var updates = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Client.PutAsJsonAsync(path, new EditorialWrite(p!.Version, p.Draft))));
        Assert.Single(updates, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(updates, r => r.StatusCode == HttpStatusCode.Conflict);
        p = await Client.GetFromJsonAsync<EditorialPreview>(path); p!.Draft.Fields["format"] = "CINEMATIC";
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft))).StatusCode);
        p = await Client.GetFromJsonAsync<EditorialPreview>(path); p!.Draft.SourceIds = [Guid.NewGuid()];
        p = await EditorialTestSupport.Read<EditorialPreview>(await Client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft)));
        Assert.Contains(p.Failures, f => f.Contains("ajenas"));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(path + "/approve", new EditorialFreeze(p.Version, p.PreviewHash))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/productions/{id}/package", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/studio/editorial/productions/{Guid.NewGuid()}")).StatusCode);
    }
    [Fact]
    public async Task DetailedReviewCannotBeInventedWithoutAnImportedCanonicalVideo()
    {
        var (id, _, _) = await Create(); var p = await EditorialTestSupport.Prepare(Client, id);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/editorial/productions/{id}/review", new EditorialReviewInput(p.Version, Guid.NewGuid(), true, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/productions/{id}/upload", new { approved = true, evidence = "Cannot bypass gates" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/editorial/productions/{id}/actual-settings", new EditorialEvidence(p.Version, "Mismatch", true, "SHORT", "es-ES", "AUTOMATIC", []))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/productions/{id}", new { topic = "Unreviewed replacement", plannedDate = "2027-01-05" })).StatusCode);
    }

    [Fact]
    public async Task CustomNarrativeDefinitionPersistsAndChangesTheCatalogWithoutChangingOldSnapshots()
    {
        var (id, _, _) = await Create(); var p = await EditorialTestSupport.Prepare(Client, id);
        var definition = EditorialIntelligence.Presets[1] with { Id = "CUSTOM_MECHANISM", Name = "Mecanismo contrastado", Structure = "Pregunta → modelo → ejemplo → límites", RecommendationCriteria = "" };
        await EditorialTestSupport.Read<JsonElement>(await Client.PutAsJsonAsync("/api/studio/editorial/archetypes/CUSTOM_MECHANISM", new NarrativeWrite(0, definition)));
        using var host = fixture.CreateFactory(); using var client = host.CreateClient();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/studio/editorial/catalog");
        Assert.Contains(catalog.GetProperty("archetypes").EnumerateArray(), x => x.GetProperty("id").GetString() == "CUSTOM_MECHANISM");
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync("/api/studio/editorial/archetypes/CUSTOM_MECHANISM", new NarrativeWrite(0, definition))).StatusCode);
        var frozen = await client.GetFromJsonAsync<EditorialPreview>($"/api/studio/editorial/productions/{id}");
        Assert.DoesNotContain(frozen!.Archetypes, n => n.Id == "CUSTOM_MECHANISM");
        Assert.Equal(p.PreviewHash, frozen.PreviewHash);
    }

    [Fact]
    public async Task ProducerNotesAreAccountRestrictedAndActualApprovalIsRecordedSeparatelyFromTheFrozenRequest()
    {
        var (id, _, _) = await Create(); var p = await EditorialTestSupport.Prepare(Client, id, false);
        var path = $"/api/studio/editorial/productions/{id}";
        p.Draft.RequestProducerNotes = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft))).StatusCode);
        p.Draft.ProducerNotesAvailability = "Available"; p.Draft.ProducerNotesEvidence = "Controlled testimony of Ultra account option; not live acceptance";
        p = await EditorialTestSupport.Read<EditorialPreview>(await Client.PutAsJsonAsync(path, new EditorialWrite(p.Version, p.Draft)));
        await EditorialTestSupport.Read<SourcePackage>(await Client.PostAsJsonAsync(path + "/approve", new EditorialFreeze(p.Version, p.PreviewHash)));
        var actual = new EditorialEvidence(p.Version, "Controlled settings testimony", true, p.Effective["format"], p.Effective["language"], p.Effective["visualStyle"], p.Sources.Select(s => s.Id).ToList());
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(path + "/actual-settings", actual)).StatusCode);
        await EditorialTestSupport.Read<JsonElement>(await Client.PostAsJsonAsync(path + "/producer-notes", new ProducerNotesInput(p.Version, "Controlled notes, never actual Google content", "Clarify source attribution", "Controlled operator testimony", true)));
        await EditorialTestSupport.Read<JsonElement>(await Client.PostAsJsonAsync(path + "/actual-settings", actual));
        var frozen = await Client.GetFromJsonAsync<EditorialPreview>(path); Assert.Equal(p.PreviewHash, frozen!.PreviewHash);
        var progress = await Client.GetFromJsonAsync<JsonElement>(path + "/progress"); Assert.Equal(2, progress.GetProperty("events").GetArrayLength());
        Assert.False(frozen.Draft.ProducerNotesApproved); // Actual review never rewrites the frozen request.
    }
}
