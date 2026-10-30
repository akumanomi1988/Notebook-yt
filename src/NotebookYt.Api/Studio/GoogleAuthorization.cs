using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public record GoogleTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

public sealed class GoogleAuthorization(EditorialDb db, IHttpClientFactory clients, IDataProtectionProvider protection, IConfiguration configuration)
{
    private IDataProtector Protector => protection.CreateProtector("GoogleCredentials.v1");
    public string Protect(string value) => Protector.Protect(value);
    public string Unprotect(string value) => Protector.Unprotect(value);
    public HttpClient Client => clients.CreateClient("Google");
    public string RedirectUri => configuration["Google:RedirectUri"] ?? "http://127.0.0.1:5173/api/studio/google/callback";
    public bool Configured => !string.IsNullOrWhiteSpace(configuration["Google:ClientId"]) && !string.IsNullOrWhiteSpace(configuration["Google:ClientSecret"]);
    public async Task<string> AccessToken(string kind, CancellationToken ct)
    {
        var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == kind && c.Status == "Connected", ct) ?? throw new StudioException(409, "Conecta la cuenta de Google correspondiente.");
        var tokens = JsonSerializer.Deserialize<GoogleTokens>(Unprotect(connection.ProtectedTokens))!;
        if (tokens.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2)) return tokens.AccessToken;
        if (tokens.RefreshToken.Length == 0) throw new StudioException(409, "Vuelve a autorizar la cuenta para obtener acceso sin conexión.");
        var fresh = await Exchange(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.RefreshToken }, tokens.RefreshToken, ct);
        connection.ProtectedTokens = Protect(JsonSerializer.Serialize(fresh)); await db.SaveChangesAsync(ct); return fresh.AccessToken;
    }
    public async Task<GoogleTokens> Exchange(Dictionary<string, string> parameters, string oldRefreshToken, CancellationToken ct)
    {
        if (!Configured) throw new StudioException(503, "Faltan las credenciales OAuth del servidor.");
        parameters["client_id"] = configuration["Google:ClientId"]!; parameters["client_secret"] = configuration["Google:ClientSecret"]!;
        using var response = await Client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(parameters), ct);
        if (!response.IsSuccessStatusCode) throw new StudioException(409, "Google rechazó la autorización. Reconecta la cuenta y revisa permisos y licencia.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return new(body.GetProperty("access_token").GetString()!, body.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString()! : oldRefreshToken, DateTime.UtcNow.AddSeconds(body.GetProperty("expires_in").GetInt32()));
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string url, string kind, HttpContent? content, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessToken(kind, ct));
        try { return await Client.SendAsync(request, ct); }
        finally { request.Dispose(); }
    }
}

public static class GoogleEndpoints
{
    public static void MapGoogleAuthorization(this WebApplication app)
    {
        app.MapGet("/api/studio/integrations", async (EditorialDb db, GoogleAuthorization google) => Results.Ok(new {
            oauthConfigured = google.Configured,
            connections = await db.Set<GoogleConnection>().Select(c => new { c.Id, c.Kind, c.DisplayName, c.ExternalChannelId, c.Status, c.LastVerifiedAtUtc }).ToListAsync(),
            capabilities = new[] {
                new { operation = "NotebookManagement", documented = true, implementation = "OfficialEnterpriseAdapter", liveVerified = await db.Set<ProductionWorkspace>().AnyAsync(w => w.NotebookVerifiedAtUtc != null), fallback = "ManualWithEvidence" },
                new { operation = "SourceManagement", documented = true, implementation = "OfficialEnterpriseAdapter", liveVerified = await db.Set<NotebookSourceBinding>().AnyAsync(b => b.Status == "Verified"), fallback = "ManualWithEvidence" },
                new { operation = "VideoOverviewGeneration", documented = false, implementation = "ManualOnly", liveVerified = false, fallback = "OfficialNotebookInterface" },
                new { operation = "VideoDownload", documented = false, implementation = "ManualOnly", liveVerified = false, fallback = "OfficialDownloadAndImport" },
                new { operation = "YouTubeResumableUpload", documented = true, implementation = "OfficialYouTubeAdapter", liveVerified = await db.Set<YouTubeUpload>().AnyAsync(u => u.VideoId != "" && u.LastVerifiedAtUtc != null), fallback = "ConnectGoogleAccount" }
            }
        }));
        app.MapPost("/api/studio/google/connect/{kind}", async (string kind, HttpContext context, EditorialDb db, GoogleAuthorization google) =>
        {
            StudioRules.Choice(kind, "YouTube", "NotebookEnterprise"); if (!google.Configured) throw new StudioException(503, "Configura Google__ClientId y Google__ClientSecret en el servidor.");
            var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)); var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
            var attempt = new OAuthAttempt { StateHash = StudioRules.Hash(state), Kind = kind, ProtectedVerifier = google.Protect(verifier), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) }; db.Add(attempt); await db.SaveChangesAsync();
            context.Response.Cookies.Append("NotebookYt.OAuth", state, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromMinutes(10), Path = "/api/studio/google" });
            var scope = kind == "YouTube" ? "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.force-ssl" : "https://www.googleapis.com/auth/cloud-platform";
            var url = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?> {
                ["client_id"] = app.Configuration["Google:ClientId"], ["redirect_uri"] = google.RedirectUri, ["response_type"] = "code", ["scope"] = scope,
                ["state"] = state, ["access_type"] = "offline", ["prompt"] = "consent", ["code_challenge_method"] = "S256", ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) });
            return Results.Ok(new { authorizationUrl = url });
        });
        app.MapGet("/api/studio/google/callback", async (string? code, string? state, HttpContext context, EditorialDb db, GoogleAuthorization google, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) || state.Length > 100 || !context.Request.Cookies.TryGetValue("NotebookYt.OAuth", out var cookie) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(cookie))) throw new StudioException(400, "La correlación OAuth no es válida o la autorización fue denegada.");
            await using var transaction = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74819321)", ct);
            var hash = StudioRules.Hash(state); var attempt = await db.Set<OAuthAttempt>().SingleOrDefaultAsync(a => a.StateHash == hash, ct);
            if (attempt is null || attempt.Used || attempt.ExpiresAtUtc < DateTime.UtcNow) throw new StudioException(400, "La autorización OAuth ha caducado o ya se utilizó.");
            attempt.Used = true; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            var tokens = await google.Exchange(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = google.RedirectUri, ["code_verifier"] = google.Unprotect(attempt.ProtectedVerifier) }, "", ct);
            await using var save = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74819321)", ct);
            var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == attempt.Kind, ct);
            if (connection is null) { connection = new GoogleConnection { Kind = attempt.Kind }; db.Add(connection); }
            connection.ProtectedTokens = google.Protect(JsonSerializer.Serialize(tokens)); connection.Status = "Connected";
            await db.SaveChangesAsync(ct);
            if (attempt.Kind == "YouTube")
            {
                using var response = await google.Send(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true", "YouTube", null, ct);
                if (!response.IsSuccessStatusCode) throw new StudioException(409, "No se pudo identificar el canal autorizado.");
                var channels = await response.Content.ReadFromJsonAsync<JsonElement>(ct); var items = channels.GetProperty("items"); if (items.GetArrayLength() != 1) throw new StudioException(409, "Autoriza exactamente un canal de YouTube.");
                connection.ExternalChannelId = items[0].GetProperty("id").GetString()!; connection.DisplayName = items[0].GetProperty("snippet").GetProperty("title").GetString()!; connection.LastVerifiedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct); await save.CommitAsync(ct); context.Response.Cookies.Delete("NotebookYt.OAuth", new CookieOptions { Path = "/api/studio/google" }); return Results.Redirect("/");
        });
        app.MapPost("/api/studio/google/disconnect/{kind}", async (string kind, EditorialDb db) =>
        {
            var connection = await db.Set<GoogleConnection>().SingleOrDefaultAsync(c => c.Kind == kind); if (connection is not null) { connection.Status = "Disconnected"; connection.ProtectedTokens = ""; await db.SaveChangesAsync(); }
            return Results.Ok(new { disconnected = true });
        });
    }
}
