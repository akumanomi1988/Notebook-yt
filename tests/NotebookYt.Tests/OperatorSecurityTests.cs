using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace NotebookYt.Tests;

public class OperatorSecurityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AuthenticationAndCsrfProtectPrivateApi()
    {
        var salt = RandomNumberGenerator.GetBytes(16); const string password = "controlled-fixture-password";
        var hash = $"100000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32))}";
        await using var factory = fixture.CreateFactory().WithWebHostBuilder(builder => builder.UseSetting("Security:RequireAuthentication", "true").UseSetting("Security:PasswordHash", hash));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/productions")).StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session"); var token = session.GetProperty("csrfToken").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { password })).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { password })).StatusCode);
        session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/productions")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/studio/google/callback?code=test&state=invalid")).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/channel", new { name = "Attack", timeZone = "Etc/UTC" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/productions")).StatusCode);
    }
}
