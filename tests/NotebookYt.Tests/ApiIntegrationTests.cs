using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;
using Npgsql;

namespace NotebookYt.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private string database = "";
    private string admin = "";
    public string ConnectionString { get; private set; } = "";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "notebook-tests-" + Guid.NewGuid().ToString("N"));

    public WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:Editorial", ConnectionString).UseSetting("Storage:Root", StorageRoot)
            .UseSetting("Security:KeyDirectory", Path.Combine(StorageRoot, "keys"))
            .UseSetting("Media:ffmpegPath", Environment.GetEnvironmentVariable("TEST_FFMPEG") ?? "ffmpeg")
            .UseSetting("Media:ffprobePath", Environment.GetEnvironmentVariable("TEST_FFPROBE") ?? "ffprobe")
            .UseSetting("Security:RequireAuthentication", "false").UseSetting("Worker:Enabled", "false").UseEnvironment("Production"));

    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("NOTEBOOK_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set NOTEBOOK_TEST_POSTGRES to an isolated local PostgreSQL admin database.");
        database = "notebook_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection)) await create.ExecuteNonQueryAsync();
        ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(ConnectionString).Options);
        await db.Database.MigrateAsync();
        Factory = CreateFactory(); Client = Factory.CreateClient();
        var initial = await Client.GetAsync("/api/channel");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal("null", await initial.Content.ReadAsStringAsync());
    }

    public async Task DisposeAsync()
    {
        Client.Dispose(); await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
        var full = Path.GetFullPath(StorageRoot);
        if (Directory.Exists(full) && full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("notebook-tests-", StringComparison.Ordinal)) Directory.Delete(full, true);
    }
}

public class ApiIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private HttpClient Client => fixture.Client;
    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private async Task<JsonElement> Initialize()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Client.PutAsJsonAsync("/api/channel", new { name = "Integration", timeZone = "Europe/Madrid" })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var week = await Json(await Client.GetAsync("/api/week"));
        Assert.Equal(7, week.GetArrayLength());
        Assert.Equal(7, week.EnumerateArray().Select(n => n.GetProperty("weekday").GetInt32()).Distinct().Count());
        return week[0];
    }
    private async Task<JsonElement> Create(Guid nicheId)
    {
        var response = await Client.PostAsJsonAsync("/api/productions", new { contentNicheId = nicheId, topic = "Integration topic", plannedDate = "2026-10-30" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    [Fact]
    public async Task CompleteWorkflowPreservesSnapshotsAndPersistsAcrossHosts()
    {
        var niche = await Initialize(); var nicheId = niche.GetProperty("id").GetGuid();
        var created = await Create(nicheId); var id = created.GetProperty("id").GetGuid(); var path = $"/api/productions/{id}";
        var edit = await Client.PutAsJsonAsync(path, new { topic = "Edited topic", plannedDate = "2026-11-02" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await Json(edit);
        Assert.Equal("Edited topic", edited.GetProperty("topic").GetString());
        Assert.True(edited.GetProperty("modifiedAtUtc").GetDateTime() >= created.GetProperty("createdAtUtc").GetDateTime());
        var nicheUpdate = await Client.PutAsJsonAsync("/api/week/1", new { name = "Renamed", description = "Description", guidance = "Guidance", enabled = false });
        Assert.Equal(HttpStatusCode.OK, nicheUpdate.StatusCode);
        var detail = await Json(await Client.GetAsync(path));
        Assert.Equal(created.GetProperty("nicheNameSnapshot").GetString(), detail.GetProperty("production").GetProperty("nicheNameSnapshot").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/productions", new { contentNicheId = nicheId, topic = "Disabled", plannedDate = "2026-11-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(path + "/status", new { status = "AwaitingNotebook" })).StatusCode);
        var transitions = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Client.PostAsJsonAsync(path + "/status", new { status = "BriefReady" })));
        Assert.Single(transitions, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, transitions.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var notebook = new { url = "https://notebooklm.google.com/notebook/integration-id" };
        var references = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => Client.PutAsJsonAsync(path + "/notebook", notebook)));
        Assert.All(references, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var referenceIds = await Task.WhenAll(references.Select(async r => (await Json(r)).GetProperty("id").GetGuid()));
        Assert.Single(referenceIds.Distinct());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync(path + "/status", new { status = "Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(path, new { topic = "Forbidden", plannedDate = "2026-11-03" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(path + "/notebook", notebook)).StatusCode);
        await using var restarted = fixture.CreateFactory(); using var client = restarted.CreateClient();
        detail = await Json(await client.GetAsync(path));
        Assert.Equal(3, detail.GetProperty("history").GetArrayLength());
        Assert.Equal("Cancelled", detail.GetProperty("production").GetProperty("status").GetString());
        Assert.Equal(notebook.url, detail.GetProperty("notebook").GetProperty("url").GetString());
        var filtered = await Json(await Client.GetAsync($"/api/productions?status=Cancelled&nicheId={nicheId}"));
        Assert.Contains(filtered.EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
        await Client.PutAsJsonAsync("/api/week/1", new { name = "Niche 1", description = "", guidance = "", enabled = true });
    }

    [Fact]
    public async Task InvalidInputsHaveProblemDetails()
    {
        await Initialize();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/productions?status=99")).StatusCode);
        var missing = await Client.GetAsync($"/api/productions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        foreach (var day in new[] { 0, 8 })
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"/api/week/{day}", new { name = "Invalid", description = "", guidance = "", enabled = true })).StatusCode);
        var response = await Client.PutAsJsonAsync("/api/week/1", new { name = "Null", description = (string?)null, guidance = "", enabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task DatabaseRejectsBrokenEditorialInvariants()
    {
        var niche = await Initialize();
        var production = await Create(niche.GetProperty("id").GetGuid());
        var id = production.GetProperty("id").GetGuid();
        (string Sql, string Code)[] statements = [
            ("INSERT INTO \"Niches\" SELECT gen_random_uuid(), \"ChannelId\", \"Weekday\", \"Name\", \"Description\", \"Guidance\", \"Enabled\" FROM \"Niches\" LIMIT 1", "23505"),
            ("UPDATE \"Niches\" SET \"Weekday\"=8 WHERE \"Weekday\"=1", "23514"),
            ("DELETE FROM \"Niches\" WHERE \"Weekday\"=7; SET CONSTRAINTS ALL IMMEDIATE", "23514"),
            ("DELETE FROM \"Channels\"", "23503"),
            ("UPDATE \"Niches\" SET \"ChannelId\"=gen_random_uuid() WHERE \"Weekday\"=1", "23503"),
            ("INSERT INTO \"Channels\" (\"Id\",\"Name\",\"TimeZone\") VALUES (gen_random_uuid(),'Second','Etc/UTC')", "23505"),
            ($"UPDATE \"Productions\" SET \"Status\"='Unknown' WHERE \"Id\"='{id}'", "23514"),
            ($"UPDATE \"Productions\" SET \"NicheNameSnapshot\"='Changed' WHERE \"Id\"='{id}'", "23514"),
            ($"DELETE FROM \"Productions\" WHERE \"Id\"='{id}'", "23503")
        ];
        foreach (var (sql, code) in statements)
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            Assert.Equal(code, error.SqlState);
            await transaction.RollbackAsync();
        }
    }
}
