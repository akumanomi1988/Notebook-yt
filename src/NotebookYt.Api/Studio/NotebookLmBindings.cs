namespace NotebookYt.Api.Studio;

public static class NotebookLmBindings
{
    private static readonly HashSet<string> OfficialHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "notebooklm.google.com",
        "notebook.google.com",
        "notebook.cloud.google.com"
    };

    public static bool MatchesNotebook(string? url, string notebookId)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(notebookId) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || !uri.IsDefaultPort || !OfficialHosts.Contains(uri.Host))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "notebook", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[index + 1], notebookId, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
