using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NotebookYt.Tests;

public static class LocalApiClient
{
    public static async Task<HttpClient> CreateLocalClientAsync(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/security/csrf");
        var token = csrf.GetProperty("csrfToken").GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            client.Dispose();
            throw new InvalidOperationException("The local API did not return an antiforgery token.");
        }
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        return client;
    }
}
