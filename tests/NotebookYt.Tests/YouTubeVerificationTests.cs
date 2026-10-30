using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class YouTubeVerificationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task RemoteVerificationComparesApprovedMetadataAndRejectsOtherChannels(bool changedTitle, bool wrongChannel, bool missingDisclosure)
    {
        using var provider = new HttpClient(new ControlledGoogleHandler(request => {
            Assert.Contains("processingDetails", request.RequestUri!.Query);
            var status = new Dictionary<string, object> { ["privacyStatus"] = "private", ["uploadStatus"] = "processed", ["selfDeclaredMadeForKids"] = false };
            if (!missingDisclosure) status["containsSyntheticMedia"] = true;
            return Task.FromResult(ControlledGoogleHandler.Json(JsonSerializer.Serialize(new { items = new[] { new {
                snippet = new { channelId = wrongChannel ? "other-channel" : "authorized-channel", title = changedTitle ? "Changed title" : "Approved title", description = "Approved description", tags = new[] { "science" }, categoryId = "28", defaultLanguage = "es" },
                status,
                processingDetails = new { processingStatus = "succeeded" }
            } } })));
        }));
        await using var factory = fixture.CreateFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IHttpClientFactory>(new ControlledClientFactory(provider))));
        using var client = factory.CreateClient();
        await client.PutAsJsonAsync("/api/channel", new { name = "Verification fixture", timeZone = "Europe/Madrid" });
        var week = await client.GetFromJsonAsync<JsonElement>("/api/week");
        var created = await client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[0].GetProperty("id").GetGuid(), topic = "Remote verification", plannedDate = "2027-01-01" });
        var production = await created.Content.ReadFromJsonAsync<JsonElement>(); var productionId = production.GetProperty("id").GetGuid();
        Guid uploadId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EditorialDb>(); var google = scope.ServiceProvider.GetRequiredService<GoogleAuthorization>();
            var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "YouTube");
            if (connection is null) { connection = new GoogleConnection { Kind = "YouTube" }; db.Add(connection); }
            connection.Status = "Connected"; connection.ExternalChannelId = "authorized-channel"; connection.DisplayName = "Controlled channel";
            connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-token", "", DateTime.UtcNow.AddHours(1))));
            var artifact = new MediaArtifact { ProductionId = productionId, Kind = "Video", Status = "PendingInspection", Origin = "Unverified", SizeBytes = 1, Sha256 = Guid.NewGuid().ToString("N"), StorageKey = "controlled", OriginalName = "controlled.mp4" }; db.Add(artifact);
            var upload = new YouTubeUpload { ProductionId = productionId, ArtifactId = artifact.Id, VideoId = "controlled01", MetadataSnapshot = JsonSerializer.Serialize(new PublicationSnapshot("Approved title", "Approved description", ["science"], "28", "es", false, true, "private", null)) }; db.Add(upload); uploadId = upload.Id;
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync($"/api/studio/uploads/{uploadId}/verification");
        Assert.Equal(wrongChannel ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        if (!wrongChannel)
        {
            var report = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (missingDisclosure)
            {
                Assert.Equal(JsonValueKind.Null, report.GetProperty("matchesApprovedMetadata").ValueKind);
                Assert.Equal(JsonValueKind.Null, report.GetProperty("checks").GetProperty("syntheticMedia").ValueKind);
                Assert.Equal("syntheticMedia", report.GetProperty("unverifiedFields")[0].GetString());
            }
            else Assert.Equal(!changedTitle, report.GetProperty("matchesApprovedMetadata").GetBoolean());
            Assert.Equal("private", report.GetProperty("privacy").GetString());
            Assert.Equal("succeeded", report.GetProperty("processingStatus").GetString());
            Assert.DoesNotContain("controlled-token", await response.Content.ReadAsStringAsync());
        }
    }
}
