namespace NotebookYt.Api;

public static class Validation
{
    public static bool IsIanaTimeZone(string? value) => value is not null &&
        (value == "Etc/UTC" || TimeZoneInfo.TryConvertIanaIdToWindowsId(value, out _)) &&
        TimeZoneInfo.TryFindSystemTimeZoneById(value, out _);

    public static bool IsNotebookUrl(string? value) => value is { Length: <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host switch
        {
            "notebooklm.google.com" or "notebook.google.com" =>
                System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/notebook/[A-Za-z0-9_-]+/?$"),
            "notebook.cloud.google.com" or "notebook.cloud.google" =>
                System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/(global|us|eu)/notebook/[A-Za-z0-9_-]+/?$"),
            _ => false
        });
}
