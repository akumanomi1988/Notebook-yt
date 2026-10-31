using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using System.Net;

namespace NotebookYt.Api.Studio;

public static class LocalRequestSecurity
{
    public static int GetLoopbackPort(IConfiguration configuration)
    {
        var value = configuration["Hosting:Urls"] ?? "http://127.0.0.1:5080";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !IPAddress.TryParse(uri.Host, out var address) ||
            !IPAddress.Loopback.Equals(address) ||
            uri.Port is < 1 or > 65535 ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Hosting:Urls must be a single http://127.0.0.1:<port> URL.");

        return uri.Port;
    }

    public static void AddLocalRequestSecurity(this WebApplicationBuilder builder)
    {
        var keys = builder.Configuration["Security:KeyDirectory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotebookYt", "keys");
        Directory.CreateDirectory(keys);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var protection = builder.Services.AddDataProtection()
            .SetApplicationName("NotebookYt")
            .PersistKeysToFileSystem(new DirectoryInfo(keys));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "NotebookYt.Csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
    }

    public static void UseLocalRequestSecurity(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 1024 * 1024;
                if (context.Request.ContentLength > 1024 * 1024)
                {
                    await Results.Problem(statusCode: 413, detail: "La petición JSON supera 1 MB.").ExecuteAsync(context);
                    return;
                }
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "no-store";

            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method))
            {
                try
                {
                    await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    await Results.Problem(statusCode: 400, detail: "La validación de origen falló. Recarga la aplicación.").ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        });

        app.MapGet("/api/security/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken }));
    }
}
