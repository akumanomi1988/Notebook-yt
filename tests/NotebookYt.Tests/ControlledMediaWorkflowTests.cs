using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class ControlledMediaWorkflowTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}"); return JsonDocument.Parse(text).RootElement.Clone();
    }
    [Fact]
    public Task ControlledOperatorAttestationTestsQualityAndThumbnailPlumbingNotGenuineOrigin() => RunWorkflow();

    internal async Task RunWorkflow()
    {
        var client = fixture.Client;
        await Json(await client.PutAsJsonAsync("/api/channel", new { name = "Controlled media workflow", timeZone = "Etc/UTC" }));
        var week = await Json(await client.GetAsync("/api/week")); var created = await Json(await client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[0].GetProperty("id").GetGuid(), topic = "Synthetic technical acceptance test ONLY", plannedDate = "2027-01-01" }));
        var id = created.GetProperty("id").GetGuid(); var path = $"/api/studio/productions/{id}";
        await Json(await client.PostAsJsonAsync(path + "/sources", new { title = "Controlled test notes", kind = "Note", url = "", notes = "Synthetic fixture workflow, never live NotebookLM acceptance", classification = "Primary", provenance = "Test authored notes", included = true, artifactId = (Guid?)null }));
        await Json(await client.PostAsync(path + "/package", null));
        await Json(await client.PutAsJsonAsync($"/api/productions/{id}/notebook", new { url = "https://notebooklm.google.com/notebook/controlled-test-only" }));
        var detail = await Json(await client.GetAsync(path)); var tasks = detail.GetProperty("tasks").EnumerateArray().ToDictionary(t => t.GetProperty("kind").GetString()!, t => t.GetProperty("id").GetGuid());
        foreach (var step in StudioRules.ManualSteps.Take(7))
            await Json(await client.PostAsJsonAsync($"/api/studio/tasks/{tasks[step.Kind]}/complete", new { attested = true, evidence = "Controlled test double for an operator statement; NOT real NotebookLM evidence" }));
        using var scope = fixture.Factory.Services.CreateScope(); var storage = scope.ServiceProvider.GetRequiredService<MediaStorage>(); Directory.CreateDirectory(storage.Root);
        var synthetic = Path.Combine(storage.Root, "synthetic-test-input.mp4");
        await storage.Run("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=5", "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo", "-t", "2", "-c:v", "libx264", "-c:a", "aac", "-pix_fmt", "yuv420p", synthetic], CancellationToken.None);
        using var content = new StreamContent(File.OpenRead(synthetic)); content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        var artifact = await Json(await client.PostAsync(path + "/artifacts?kind=Video&name=synthetic-test-only.mp4&attested=false&evidence=Synthetic-technical-test-NOT-NotebookLM-content", content)); var artifactId = artifact.GetProperty("id").GetGuid();
        await using (var recoveryDb = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options))
        {
            var expired = await recoveryDb.Set<BackgroundJob>().SingleAsync(j => j.ResourceId == artifactId);
            expired.Status = "Running"; expired.LeaseOwner = Guid.NewGuid(); expired.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-10);
            await recoveryDb.SaveChangesAsync();
        }
        var worker = new StudioWorker(fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<StudioWorker>.Instance); await worker.Tick(CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + "/canonical/" + artifactId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/studio/tasks/{tasks["ImportNotebookVideo"]}/complete", new { attested = true, evidence = "Missing origin evidence" })).StatusCode);
        await Json(await client.PostAsJsonAsync($"/api/studio/artifacts/{artifactId}/attest", new { attested = true, evidence = "Controlled test double of an operator statement; NOT genuine NotebookLM origin evidence" }));
        await Json(await client.PostAsJsonAsync($"/api/studio/tasks/{tasks["ImportNotebookVideo"]}/complete", new { attested = true, evidence = "Controlled plumbing test only" }));
        await Json(await client.PostAsync(path + "/canonical/" + artifactId, null));
        var thumbnail = await Json(await client.PostAsJsonAsync($"/api/studio/artifacts/{artifactId}/thumbnail", new { seconds = 0.5, cropX = 0.2, cropY = 0.7, text = "CONTROLLED TEST ONLY" }));
        Assert.Equal("Valid", thumbnail.GetProperty("status").GetString()); Assert.Equal("DerivedNotebookFrame", thumbnail.GetProperty("origin").GetString());
        var quality = await Json(await client.GetAsync(path + "/quality")); Assert.False(quality.GetProperty("ready").GetBoolean());
        detail = await Json(await client.GetAsync(path)); var metadata = detail.GetProperty("workspace").EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()); metadata["youTubeDescription"] = "Synthetic technical fixture, not NotebookLM content and never to be published."; metadata["madeForKids"] = false;
        await Json(await client.PutAsJsonAsync(path, metadata));
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var channel = await db.Channels.SingleAsync(); db.Add(new ChannelSettings { Id = channel.Id, MinimumDurationSeconds = 1 }); await db.SaveChangesAsync();
        quality = await Json(await client.GetAsync(path + "/quality")); Assert.True(quality.GetProperty("ready").GetBoolean());
        metadata["publishAtUtc"] = DateTime.UtcNow.AddDays(2); metadata["visibility"] = "private";
        var conflictingSchedule = await client.PutAsJsonAsync(path, metadata);
        Assert.Equal(HttpStatusCode.Conflict, conflictingSchedule.StatusCode);
        Assert.Contains("public", await conflictingSchedule.Content.ReadAsStringAsync());
        detail = await Json(await client.GetAsync(path));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("workspace").GetProperty("publishAtUtc").ValueKind);
        await Json(await client.PostAsJsonAsync(path + "/review", new { approved = true, evidence = "Test-only approval for a controlled fixture, not a real production" }));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/upload", new { approved = true, evidence = "Must fail without Google authorization" })).StatusCode);
        var file = await client.GetAsync($"/api/studio/artifacts/{artifactId}/file"); Assert.Equal("video/mp4", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artifact.GetProperty("sha256").GetString(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await file.Content.ReadAsByteArrayAsync())));
    }
}
