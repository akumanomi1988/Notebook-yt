using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class LocalAccessSecurityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("http://127.0.0.1:5080", 5080)]
    [InlineData("http://127.0.0.1:6100", 6100)]
    public void Binding_uses_only_configured_ipv4_loopback(string value, int expectedPort)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosting:Urls"] = value
        }).Build();

        Assert.Equal(expectedPort, LocalRequestSecurity.GetLoopbackPort(configuration));
    }

    [Theory]
    [InlineData("http://0.0.0.0:5080")]
    [InlineData("http://192.168.1.4:5080")]
    [InlineData("https://127.0.0.1:5080")]
    [InlineData("http://127.0.0.1:5080;http://0.0.0.0:8080")]
    public void Binding_rejects_non_loopback_or_ambiguous_urls(string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosting:Urls"] = value
        }).Build();

        Assert.Throws<InvalidOperationException>(() => LocalRequestSecurity.GetLoopbackPort(configuration));
    }

    [Theory]
    [InlineData("127.0.0.1:5080", true)]
    [InlineData("127.0.0.1:5173", false)]
    [InlineData("localhost:5080", false)]
    [InlineData("192.168.1.4:5080", false)]
    [InlineData("evil.example:5080", false)]
    [InlineData("127.0.0.1", false)]
    public void Host_validation_accepts_only_configured_ipv4_loopback(string value, bool expected)
    {
        Assert.Equal(expected, LocalRequestSecurity.IsAllowedHost(new HostString(value), 5080));
    }

    [Fact]
    public async Task LocalApiWorksWithoutOperatorLoginAndRequiresCsrfForWrites()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        client.BaseAddress = new Uri("http://127.0.0.1:5080");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/productions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/session")).StatusCode);

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/security/csrf");
        var token = csrf.GetProperty("csrfToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var noToken = await client.PutAsJsonAsync("/api/channel", new { name = "NewSphere", timeZone = "Etc/UTC" });
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5080");
        using var sameOrigin = await client.PutAsJsonAsync("/api/channel", new { name = "NewSphere", timeZone = "Etc/UTC" });
        Assert.Equal(HttpStatusCode.OK, sameOrigin.StatusCode);

        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        using var crossOrigin = await client.PutAsJsonAsync("/api/channel", new { name = "Blocked", timeZone = "Etc/UTC" });
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);

        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5080");
        client.DefaultRequestHeaders.Host = "evil.example:5080";
        using var hostileHost = await client.GetAsync("/api/productions");
        Assert.Equal(HttpStatusCode.BadRequest, hostileHost.StatusCode);
    }
}
