using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotebookYt.Api;
using NotebookYt.Api.Studio;
using Npgsql;

namespace NotebookYt.Tests;

public class StudioIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private HttpClient Client => fixture.Client;
    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}"); return JsonDocument.Parse(text).RootElement.Clone();
    }
    private async Task<(Guid Production, Guid Channel, Guid Niche)> Initialize()
    {
        var channel = await Body(await Client.PutAsJsonAsync("/api/channel", new { name = "Studio test", timeZone = "Europe/Madrid" }));
        var week = await Body(await Client.GetAsync("/api/week")); var niche = week[0].GetProperty("id").GetGuid();
        var production = await Body(await Client.PostAsJsonAsync("/api/productions", new { contentNicheId = niche, topic = "Controlled studio test", plannedDate = "2027-02-01" }));
        return (production.GetProperty("id").GetGuid(), channel.GetProperty("id").GetGuid(), niche);
    }
    private EditorialDb Context() => new(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);

    [Fact]
    public async Task CorruptImportsAreRejectedAndCannotBecomeCanonical()
    {
        var (id, _, _) = await Initialize(); var path = $"/api/studio/productions/{id}";
        using var fakePdf = new ByteArrayContent(Encoding.UTF8.GetBytes("Not a PDF despite its MIME parameters"));
        fakePdf.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf") { CharSet = "utf-8" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync(path + "/artifacts?kind=Document&name=test.pdf", fakePdf)).StatusCode);
        using var invalidName = new ByteArrayContent(Encoding.UTF8.GetBytes("Test content")); invalidName.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync(path + "/artifacts?kind=Document&name=invalid%00name.txt", invalidName)).StatusCode);
        await Body(await Client.PutAsJsonAsync($"/api/productions/{id}/notebook", new { url = "https://notebooklm.google.com/notebook/corrupt-test" }));
        using var badContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Corrupt controlled media fixture")); badContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        var response = await Client.PostAsync(path + "/artifacts?kind=Video&name=corrupt.mp4&attested=true&evidence=Controlled-test-attestation-not-genuine-NotebookLM-evidence", badContent);
        var imported = await Body(response); var artifactId = imported.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync(path + "/canonical/" + artifactId, null)).StatusCode);
        var worker = new StudioWorker(fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<StudioWorker>.Instance);
        await worker.Tick(CancellationToken.None);
        await using var db = Context(); var artifact = await db.Set<MediaArtifact>().FindAsync(artifactId); Assert.Equal("Rejected", artifact!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync($"/api/studio/artifacts/{artifactId}/file")).StatusCode);
    }

    [Fact]
    public async Task SourcesPackagesTasksAndSnapshotsAreDurableAndGated()
    {
        var (id, _, _) = await Initialize(); var path = $"/api/studio/productions/{id}";
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync(path + "/package", null)).StatusCode);
        var source = new { title = "Operator research", kind = "Note", url = "", notes = "Controlled research note, no fabricated citation", classification = "Primary", provenance = "Test fixture authored for this test", included = true, artifactId = (Guid?)null };
        await Body(await Client.PostAsJsonAsync(path + "/sources", source));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(path + "/sources", source)).StatusCode);
        await EditorialTestSupport.Prepare(Client, id);
        var package = await Body(await Client.PostAsync(path + "/package", null)); var second = await Body(await Client.PostAsync(path + "/package", null));
        Assert.Equal(package.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(path + "/sources", source)).StatusCode);
        var detail = await Body(await Client.GetAsync(path)); Assert.Equal(8, detail.GetProperty("tasks").GetArrayLength());
        Assert.Equal(StudioRules.ManualSteps.Select(s => s.Kind), detail.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("kind").GetString()));
        var tasks = detail.GetProperty("tasks").EnumerateArray().ToDictionary(t => t.GetProperty("kind").GetString()!, t => t.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/tasks/{tasks["StartVideoGeneration"]}/complete", new { attested = true, evidence = "Not yet allowed" })).StatusCode);
        await Body(await Client.PutAsJsonAsync($"/api/productions/{id}/notebook", new { url = "https://notebooklm.google.com/notebook/test-studio" }));
        await EditorialTestSupport.Attest(Client, id);
        foreach (var step in StudioRules.ManualSteps.Take(7)) await Body(await Client.PostAsJsonAsync($"/api/studio/tasks/{tasks[step.Kind]}/complete", new { attested = true, evidence = "Controlled operator attestation, not provider verification" }));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/tasks/{tasks["ImportNotebookVideo"]}/complete", new { attested = true, evidence = "No file" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/productions/{id}/notebook", new { url = "https://notebooklm.google.com/notebook/different" })).StatusCode);
        var quality = await Body(await Client.GetAsync(path + "/quality")); Assert.False(quality.GetProperty("ready").GetBoolean());
        await using var context = Context(); await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        foreach (var sql in new[] { $"UPDATE \"SourcePackage\" SET \"Markdown\"='Changed' WHERE \"ProductionId\"='{id}'", $"UPDATE \"ProductionWorkspace\" SET \"EditorialSnapshot\"='changed' WHERE \"Id\"='{id}'" })
        {
            await using var command = new NpgsqlCommand(sql, connection); var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync()); Assert.Equal("23514", error.SqlState);
        }
        await using var restarted = fixture.CreateFactory(); using var restartedClient = await restarted.CreateLocalClientAsync();
        var persisted = await Body(await restartedClient.GetAsync(path)); Assert.Equal(package.GetProperty("sha256").GetString(), persisted.GetProperty("packages")[0].GetProperty("sha256").GetString());
    }
    [Fact]
    public async Task TopicAndProductionIdempotencyPreventDuplicates()
    {
        var (_, _, niche) = await Initialize();
        var topic = await Body(await Client.PostAsJsonAsync("/api/studio/topics", new { contentNicheId = niche, title = "Evergreen test " + Guid.NewGuid(), origin = "Manual", evergreenAssessment = "Timeless evidence", priority = 50 }));
        var id = topic.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/studio/topics/{id}/production", new { plannedDate = "2027-03-01" })).StatusCode);
        await Body(await Client.PutAsJsonAsync($"/api/studio/topics/{id}", new { status = "Approved", priority = 50, evergreenAssessment = "Timeless" }));
        var productions = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Client.PostAsJsonAsync($"/api/studio/topics/{id}/production", new { plannedDate = "2027-03-01" })));
        var ids = await Task.WhenAll(productions.Select(async r => (await Body(r)).GetProperty("id").GetGuid())); Assert.Single(ids.Distinct());
        var key = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Create(string title)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/productions") { Content = JsonContent.Create(new { contentNicheId = niche, topic = title, plannedDate = "2027-03-01" }) }; request.Headers.Add("Idempotency-Key", key); return await Client.SendAsync(request);
        }
        var first = await Body(await Create("Idempotent topic")); var again = await Body(await Create("Idempotent topic"));
        Assert.Equal(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid()); Assert.Equal(HttpStatusCode.Conflict, (await Create("Different topic")).StatusCode);
    }
    [Fact]
    public async Task SchedulerIsIdempotentAndWorkerRecoversExpiredLeases()
    {
        var (id, channel, _) = await Initialize(); await using var db = Context();
        var settings = await db.Set<ChannelSettings>().FindAsync(channel);
        if (settings is null) { settings = new ChannelSettings { Id = channel }; db.Add(settings); }
        settings.SchedulingEnabled = true; settings.HorizonDays = 7; settings.LeadDays = 0; await db.SaveChangesAsync();
        await EditorialScheduler.Maintain(db, new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc)); var count = await db.Set<ScheduleSlot>().CountAsync();
        await EditorialScheduler.Maintain(db, new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc)); Assert.Equal(count, await db.Set<ScheduleSlot>().CountAsync()); Assert.Equal(7, count);
        settings.SchedulingEnabled = false;
        var job = new BackgroundJob { Kind = "CreateNotebook", ProductionId = id, Status = "Running", LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-1), LeaseOwner = Guid.NewGuid(), IdempotencyKey = "expired:" + Guid.NewGuid() }; db.Add(job); await db.SaveChangesAsync();
        var worker = new StudioWorker(fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<StudioWorker>.Instance);
        await worker.Tick(CancellationToken.None); await db.Entry(job).ReloadAsync(); Assert.Equal("NeedsAttention", job.Status);
    }
}
