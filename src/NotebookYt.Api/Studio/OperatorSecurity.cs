using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace NotebookYt.Api.Studio;

public static class OperatorSecurity
{
    public static void AddOperatorSecurity(this WebApplicationBuilder builder)
    {
        var keys = builder.Configuration["Security:KeyDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookYt", "keys");
        Directory.CreateDirectory(keys);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var protection = builder.Services.AddDataProtection().SetApplicationName("NotebookYt").PersistKeysToFileSystem(new DirectoryInfo(keys));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        builder.Services.AddAuthentication("Operator").AddCookie("Operator", options =>
        {
            options.Cookie.Name = "NotebookYt.Operator"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        });
        builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "NotebookYt.Csrf"; o.Cookie.SameSite = SameSiteMode.Strict; });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", limiter => { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }).RejectionStatusCode = 429);
    }
    public static void UseOperatorSecurity(this WebApplication app)
    {
        app.UseRateLimiter(); app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 1024 * 1024;
                if (context.Request.ContentLength > 1024 * 1024) { await Results.Problem(statusCode: 413, detail: "La petición JSON supera 1 MB.").ExecuteAsync(context); return; }
            }
            var required = app.Configuration.GetValue("Security:RequireAuthentication", true);
            var authPath = context.Request.Path.StartsWithSegments("/api/auth");
            if (required && context.Request.Path.StartsWithSegments("/api") && !authPath && context.User.Identity?.IsAuthenticated != true)
            { await Results.Problem(statusCode: 401, detail: "Inicia sesión para acceder al centro editorial.").ExecuteAsync(context); return; }
            if (required && context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, detail: "La sesión del formulario caducó. Recarga la página.").ExecuteAsync(context); return; }
            }
            context.Response.Headers.XContentTypeOptions = "nosniff"; context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.MapGet("/api/auth/session", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new {
            required = app.Configuration.GetValue("Security:RequireAuthentication", true), authenticated = context.User.Identity?.IsAuthenticated == true,
            configured = !string.IsNullOrWhiteSpace(app.Configuration["Security:PasswordHash"]), csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
        }));
        app.MapPost("/api/auth/login", async (LoginInput input, HttpContext context) =>
        {
            var expected = app.Configuration["Security:PasswordHash"];
            if (string.IsNullOrWhiteSpace(expected)) return Results.Problem(statusCode: 503, detail: "Configura Security__PasswordHash en el servidor antes de iniciar sesión.");
            if (!VerifyPassword(input.Password, expected)) return Results.Problem(statusCode: 401, detail: "Credenciales incorrectas.");
            await context.SignInAsync("Operator", new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "LocalOperator"), new Claim(ClaimTypes.Role, "Administrator")], "Operator")));
            return Results.Ok(new { authenticated = true });
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", async (HttpContext context) => { await context.SignOutAsync("Operator"); return Results.Ok(new { authenticated = false }); });
    }
    public static bool VerifyPassword(string? password, string encoded)
    {
        try
        {
            if (password is null || password.Length > 512) return false;
            var parts = encoded.Split(':'); if (parts.Length != 3) return false;
            var iterations = int.Parse(parts[0]); if (iterations is < 100000 or > 2000000) return false;
            var salt = Convert.FromBase64String(parts[1]); var digest = Convert.FromBase64String(parts[2]);
            if (salt.Length < 16 || digest.Length != 32) return false;
            return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32), digest);
        }
        catch (FormatException) { return false; }
    }
}
public record LoginInput(string Password);
