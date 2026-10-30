using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public sealed class ControlledGoogleHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
public sealed class ControlledClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }

public class GoogleAdapterContractTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task PublishingAppliesThumbnailAndFuturePrivateScheduleUsingOfficialContracts()
    {
        await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Publishing contract", timeZone = "Etc/UTC" }); var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week");
        var created = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[4].GetProperty("id").GetGuid(), topic = "Publishing contract", plannedDate = "2027-01-05" }); var p = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = p.GetProperty("id").GetGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = fixture.StorageRoot }).Build(); var storage = new MediaStorage(config);
        var saved = await storage.Save(new MemoryStream(new byte[] { 255, 216, 255, 1, 2, 3 }), 100, CancellationToken.None);
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var thumbnail = new MediaArtifact { ProductionId = id, Kind = "Thumbnail", Status = "Valid", MimeType = "image/jpeg", StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length }; db.Add(thumbnail);
        var future = DateTime.UtcNow.AddDays(1); var upload = new YouTubeUpload { ProductionId = id, ArtifactId = thumbnail.Id, VideoId = "controlledvideo03", MetadataSnapshot = JsonSerializer.Serialize(new PublicationSnapshot("Approved title", "Approved description", ["test"], "27", "es", false, true, "public", future)) }; db.Add(upload);
        var requests = 0;
        using var client = new HttpClient(new ControlledGoogleHandler(async request => {
            requests++;
            if (request.Method == HttpMethod.Post)
            {
                Assert.Contains("/thumbnails/set", request.RequestUri!.AbsolutePath); Assert.Equal("image/jpeg", request.Content!.Headers.ContentType?.MediaType); Assert.Equal(saved.Length, (await request.Content.ReadAsByteArrayAsync()).LongLength);
            }
            else
            {
                Assert.Equal(HttpMethod.Put, request.Method); Assert.Contains("part=snippet,status", request.RequestUri!.Query); var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Approved title", body.GetProperty("snippet").GetProperty("title").GetString()); Assert.Equal("private", body.GetProperty("status").GetProperty("privacyStatus").GetString()); Assert.Equal(future, body.GetProperty("status").GetProperty("publishAt").GetDateTime());
            }
            return ControlledGoogleHandler.Json("{}");
        }));
        var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
        var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "YouTube"); if (connection is null) { connection = new GoogleConnection { Kind = "YouTube" }; db.Add(connection); }
        connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-test-token", "", DateTime.UtcNow.AddHours(1)))); await db.SaveChangesAsync();
        await new YouTubePublishing(db, google, storage).Publish(new BackgroundJob { ResourceId = upload.Id, ProductionId = id }, CancellationToken.None);
        Assert.Equal(2, requests); Assert.True(await db.Set<BackgroundJob>().AnyAsync(j => j.Kind == "VerifyYouTube" && j.ResourceId == upload.Id)); Assert.NotEqual("Published", upload.Status);
    }

    [Fact]
    public async Task DocumentSourceUsesOfficialRawUploadProtocol()
    {
        await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Document source fixture", timeZone = "Etc/UTC" }); var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week");
        var created = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[5].GetProperty("id").GetGuid(), topic = "Document source contract", plannedDate = "2027-01-06" }); var p = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = p.GetProperty("id").GetGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = fixture.StorageRoot, ["NotebookEnterprise:ProjectNumber"] = "123456" }).Build(); var storage = new MediaStorage(config);
        var bytes = Encoding.ASCII.GetBytes("%PDF-controlled HTTP contract fixture"); var saved = await storage.Save(new MemoryStream(bytes), 1000, CancellationToken.None);
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options); var channel = await db.Channels.SingleAsync(); var settings = await db.Set<ChannelSettings>().FindAsync(channel.Id);
        if (settings is null) { settings = new ChannelSettings { Id = channel.Id }; db.Add(settings); } settings.NotebookProduct = "Enterprise"; settings.EnterpriseLicenseAttested = true;
        db.Add(new NotebookReference { ProductionId = id, Url = "https://notebook.cloud.google.com/global/notebook/document-contract?project=123456" });
        var document = new MediaArtifact { ProductionId = id, Kind = "Document", Status = "Valid", MimeType = "application/pdf", StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length }; db.Add(document);
        db.Add(new ResearchSource { ProductionId = id, Kind = "Document", Title = "Controlled document", ArtifactId = document.Id, Fingerprint = StudioRules.Hash(Guid.NewGuid().ToString()), Provenance = "Test authored contract bytes" });
        var uploads = 0;
        using var client = new HttpClient(new ControlledGoogleHandler(async request => {
            if (request.Method == HttpMethod.Post)
            {
                uploads++; Assert.Equal("https://global-discoveryengine.googleapis.com/upload/v1alpha/projects/123456/locations/global/notebooks/document-contract/sources:uploadFile", request.RequestUri!.AbsoluteUri);
                Assert.Equal("raw", request.Headers.GetValues("X-Goog-Upload-Protocol").Single()); Assert.Equal("application/pdf", request.Content!.Headers.ContentType?.MediaType); Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync());
                return ControlledGoogleHandler.Json("{\"sourceId\":{\"id\":\"controlled-document-source\"}}");
            }
            return ControlledGoogleHandler.Json(request.RequestUri!.AbsolutePath.Contains("/sources/") ? "{\"settings\":{\"status\":\"SOURCE_STATUS_COMPLETE\"}}" : "{\"notebookId\":\"document-contract\"}");
        }));
        var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
        var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "NotebookEnterprise"); if (connection is null) { connection = new GoogleConnection { Kind = "NotebookEnterprise" }; db.Add(connection); }
        connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-test-token", "", DateTime.UtcNow.AddHours(1)))); await db.SaveChangesAsync();
        await new NotebookIntegration(db, google, config, storage).Sync(new BackgroundJob { ProductionId = id }, CancellationToken.None); Assert.Equal(1, uploads);
    }
    [Fact]
    public async Task TokenRefreshUsesOfficialFormAndProtectedPersistence()
    {
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var requests = 0;
        using var client = new HttpClient(new ControlledGoogleHandler(async request => {
            requests++; Assert.Equal("https://oauth2.googleapis.com/token", request.RequestUri!.AbsoluteUri); Assert.Equal(HttpMethod.Post, request.Method);
            var form = await request.Content!.ReadAsStringAsync(); Assert.Contains("grant_type=refresh_token", form); Assert.Contains("refresh_token=controlled-refresh-token", form);
            return ControlledGoogleHandler.Json("{\"access_token\":\"controlled-new-token\",\"expires_in\":3600}");
        }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Google:ClientId"] = "controlled-test-client", ["Google:ClientSecret"] = "controlled-test-credential-not-a-real-secret" }).Build();
        var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
        var connection = new GoogleConnection { Kind = "RefreshContract", ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("expired-test-token", "controlled-refresh-token", DateTime.UtcNow.AddMinutes(-1)))) }; db.Add(connection); await db.SaveChangesAsync();
        Assert.Equal("controlled-new-token", await google.AccessToken("RefreshContract", CancellationToken.None)); Assert.Equal("controlled-new-token", await google.AccessToken("RefreshContract", CancellationToken.None));
        Assert.Equal(1, requests); Assert.DoesNotContain("controlled-new-token", connection.ProtectedTokens);
    }

    [Fact]
    public async Task PublishedStateRequiresAuthoritativeProcessedPublicVideoEvidence()
    {
        await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Publication fixture", timeZone = "Etc/UTC" }); var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week");
        var response = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[3].GetProperty("id").GetGuid(), topic = "Publication evidence contract", plannedDate = "2027-01-04" }); var p = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = p.GetProperty("id").GetGuid();
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var artifact = new MediaArtifact { ProductionId = id, SizeBytes = 1, Kind = "Video", Status = "PendingInspection", Sha256 = StudioRules.Hash("publication-contract"), StorageKey = StudioRules.Hash("publication-contract") + ".blob" }; db.Add(artifact);
        var upload = new YouTubeUpload { ProductionId = id, ArtifactId = artifact.Id, VideoId = "controlledvideo02", MetadataSnapshot = JsonSerializer.Serialize(new PublicationSnapshot("Test", "Test", [], "27", "es", false, true, "public", null)) }; db.Add(upload);
        var calls = 0;
        using var client = new HttpClient(new ControlledGoogleHandler(request => {
            calls++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Contains("part=status,snippet", request.RequestUri!.Query);
            return Task.FromResult(ControlledGoogleHandler.Json(calls == 1 ? "{\"items\":[{\"snippet\":{\"channelId\":\"controlled-channel\"},\"status\":{\"uploadStatus\":\"uploaded\",\"privacyStatus\":\"private\"}}]}" : "{\"items\":[{\"snippet\":{\"channelId\":\"controlled-channel\"},\"status\":{\"uploadStatus\":\"processed\",\"privacyStatus\":\"public\"}}]}"));
        }));
        var config = new ConfigurationBuilder().Build(); var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
        var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "YouTube"); if (connection is null) { connection = new GoogleConnection { Kind = "YouTube" }; db.Add(connection); }
        connection.ExternalChannelId = "controlled-channel"; connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-test-token", "", DateTime.UtcNow.AddHours(1)))); await db.SaveChangesAsync();
        var adapter = new YouTubePublishing(db, google, new MediaStorage(config)); var job = new BackgroundJob { ResourceId = upload.Id, ProductionId = id, Kind = "VerifyYouTube" };
        await adapter.Verify(job, CancellationToken.None); Assert.Equal("Uploaded", upload.Status); Assert.NotEqual("Published", (await db.Set<ProductionWorkspace>().FindAsync(id))!.Stage);
        await adapter.Verify(job, CancellationToken.None); Assert.Equal("Published", upload.Status);
    }
    [Fact]
    public async Task SourceProcessingRetriesQueryExistingIdentifierWithoutDuplicateRegistration()
    {
        await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Source contract fixture", timeZone = "Etc/UTC" }); var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week");
        var created = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[2].GetProperty("id").GetGuid(), topic = "Sources contract", plannedDate = "2027-01-03" }); var p = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = p.GetProperty("id").GetGuid();
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var channel = await db.Channels.SingleAsync(); var settings = await db.Set<ChannelSettings>().FindAsync(channel.Id);
        if (settings is null) { settings = new ChannelSettings { Id = channel.Id }; db.Add(settings); }
        settings.NotebookProduct = "Enterprise"; settings.EnterpriseLicenseAttested = true;
        db.Add(new NotebookReference { ProductionId = id, Url = "https://notebook.cloud.google.com/global/notebook/source-contract?project=123456" });
        db.Add(new ResearchSource { ProductionId = id, Kind = "Note", Title = "Controlled source", Notes = "Test content", Provenance = "Test fixture", Fingerprint = StudioRules.Hash(Guid.NewGuid().ToString()) });
        var registrations = 0; var retrievals = 0;
        using var client = new HttpClient(new ControlledGoogleHandler(async request => {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sources:batchCreate"))
            {
                registrations++; var body = await request.Content!.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Test content", body.GetProperty("userContents")[0].GetProperty("textContent").GetProperty("content").GetString());
                return ControlledGoogleHandler.Json("{\"sources\":[{\"sourceId\":{\"id\":\"controlled-source\"}}]}");
            }
            if (request.RequestUri.AbsolutePath.Contains("/sources/"))
            {
                retrievals++; return ControlledGoogleHandler.Json(retrievals == 1 ? "{\"settings\":{\"status\":\"SOURCE_STATUS_PROCESSING\"}}" : "{\"settings\":{\"status\":\"SOURCE_STATUS_COMPLETE\"}}");
            }
            return ControlledGoogleHandler.Json("{\"notebookId\":\"source-contract\"}");
        }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["NotebookEnterprise:ProjectNumber"] = "123456" }).Build();
        var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
        var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "NotebookEnterprise"); if (connection is null) { connection = new GoogleConnection { Kind = "NotebookEnterprise" }; db.Add(connection); }
        connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-test-token", "", DateTime.UtcNow.AddHours(1)))); await db.SaveChangesAsync();
        var adapter = new NotebookIntegration(db, google, config, new MediaStorage(config)); var job = new BackgroundJob { Kind = "SyncNotebookSources", ProductionId = id };
        var pending = await Assert.ThrowsAsync<StudioException>(() => adapter.Sync(job, CancellationToken.None)); Assert.Equal(503, pending.Status);
        await adapter.Sync(job, CancellationToken.None); Assert.Equal(1, registrations); Assert.Equal(2, retrievals);
        Assert.Equal("Verified", (await db.Set<NotebookSourceBinding>().SingleAsync(b => db.Set<ResearchSource>().Any(s => s.Id == b.ResearchSourceId && s.ProductionId == id))).Status);
    }
    [Fact]
    public async Task OfficialNotebookCreationPersistsProviderIdentifierWithoutLiveCalls()
    {
        await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Contract fixture", timeZone = "Etc/UTC" });
        var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week"); var niche = week[0].GetProperty("id").GetGuid();
        var created = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = niche, topic = "Notebook contract", plannedDate = "2027-01-01" }); var p = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = p.GetProperty("id").GetGuid();
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var channel = await db.Channels.SingleAsync(); var settings = await db.Set<ChannelSettings>().FindAsync(channel.Id);
        if (settings is null) { settings = new ChannelSettings { Id = channel.Id }; db.Add(settings); }
        settings.NotebookProduct = "Enterprise"; settings.EnterpriseLicenseAttested = true;
        var requests = 0;
        using var client = new HttpClient(new ControlledGoogleHandler(async request => {
            requests++; Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("https://global-discoveryengine.googleapis.com/v1alpha/projects/123456/locations/global/notebooks", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal("Notebook contract", json.RootElement.GetProperty("title").GetString());
            return ControlledGoogleHandler.Json("{\"notebookId\":\"controlled-notebook-id\",\"name\":\"projects/123456/locations/global/notebooks/controlled-notebook-id\"}");
        }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["NotebookEnterprise:ProjectNumber"] = "123456", ["NotebookEnterprise:Region"] = "global" }).Build();
        var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
        var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "NotebookEnterprise"); if (connection is null) { connection = new GoogleConnection { Kind = "NotebookEnterprise" }; db.Add(connection); }
        connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-test-token", "", DateTime.UtcNow.AddHours(1)))); await db.SaveChangesAsync();
        var adapter = new NotebookIntegration(db, google, config, new MediaStorage(config)); var job = new BackgroundJob { ProductionId = id, Kind = "CreateNotebook" };
        await adapter.Create(job, CancellationToken.None); await adapter.Create(job, CancellationToken.None);
        Assert.Equal(1, requests); Assert.Contains("controlled-notebook-id", (await db.NotebookReferences.SingleAsync(n => n.ProductionId == id)).Url);
    }
    [Fact]
    public async Task ResumableYouTubeSessionConfirmsVideoAndNeverStartsDuplicateUpload()
    {
        await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Contract fixture", timeZone = "Etc/UTC" }); var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week");
        var created = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = week[1].GetProperty("id").GetGuid(), topic = "YouTube contract", plannedDate = "2027-01-02" }); var p = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = p.GetProperty("id").GetGuid();
        var root = Path.Combine(Path.GetTempPath(), "youtube-contract-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = root }).Build(); var storage = new MediaStorage(config);
        try
        {
            var saved = await storage.Save(new MemoryStream(new byte[300000]), 1000000, CancellationToken.None);
            await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
            // Bytes are deliberately not a real video: this is an HTTP contract test, not media or origin verification.
            var artifact = new MediaArtifact { ProductionId = id, StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length, Kind = "Video", Status = "PendingInspection", Origin = "Unverified" }; db.Add(artifact);
            var upload = new YouTubeUpload { ProductionId = id, ArtifactId = artifact.Id, MetadataSnapshot = JsonSerializer.Serialize(new PublicationSnapshot("Contract title", "Contract description", [], "27", "es", false, true, "private", null)) }; db.Add(upload);
            var postCount = 0; var putCount = 0;
            using var client = new HttpClient(new ControlledGoogleHandler(async request => {
                if (request.Method == HttpMethod.Post)
                {
                    postCount++; Assert.Contains("uploadType=resumable", request.RequestUri!.Query); var data = await request.Content!.ReadFromJsonAsync<JsonElement>(); Assert.Equal("private", data.GetProperty("status").GetProperty("privacyStatus").GetString());
                    var response = new HttpResponseMessage(HttpStatusCode.OK); response.Headers.Location = new Uri("https://www.googleapis.com/upload/youtube/v3/videos?upload_id=controlled-session"); return response;
                }
                Assert.Equal(HttpMethod.Put, request.Method); putCount++;
                if (request.Content!.Headers.ContentRange!.From is null) return new HttpResponseMessage((HttpStatusCode)308);
                Assert.Equal(300000, (await request.Content.ReadAsByteArrayAsync()).Length); return ControlledGoogleHandler.Json("{\"id\":\"testvideo01\"}", HttpStatusCode.Created);
            }));
            var google = new GoogleAuthorization(db, new ControlledClientFactory(client), new EphemeralDataProtectionProvider(), config);
            var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == "YouTube"); if (connection is null) { connection = new GoogleConnection { Kind = "YouTube" }; db.Add(connection); }
            connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(new GoogleTokens("controlled-test-token", "", DateTime.UtcNow.AddHours(1)))); await db.SaveChangesAsync();
            var adapter = new YouTubePublishing(db, google, storage); var job = new BackgroundJob { ProductionId = id, ResourceId = upload.Id, Kind = "UploadYouTube" };
            await adapter.Process(job, CancellationToken.None); await adapter.Process(job, CancellationToken.None);
            Assert.Equal(1, postCount); Assert.Equal(2, putCount); Assert.Equal("Uploaded", upload.Status); Assert.Equal("testvideo01", upload.VideoId);
        }
        finally { foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
