using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotebookYt.Api.Studio;

public sealed record NotebookLmCliResult(
    bool Success,
    string Code,
    string? Json,
    int? ExitCode,
    bool OutcomeUnknown = false);

public interface INotebookLmCliRunner
{
    Task<NotebookLmCliResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null,
        bool mutating = false, CancellationToken cancellationToken = default);
}

public interface INotebookLmProvider
{
    Task<NotebookLmCliResult> CheckAuthenticationAsync(CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> ListProfilesAsync(CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> GetUsageAsync(CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> ListNotebooksAsync(CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> GetNotebookAsync(string notebookId, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> CreateNotebookAsync(string title, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> CopyNotebookAsync(string notebookId, string title, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> ListSourcesAsync(string notebookId, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> AddSourceAsync(string notebookId, string url, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> ListArtifactsAsync(string notebookId, string type = "all", CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> AskAsync(string notebookId, string prompt, IReadOnlyList<string> sourceIds, string? conversationId = null, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> StartResearchAsync(string notebookId, string query, string mode, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> ResearchStatusAsync(string notebookId, string? taskId, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> GenerateVideoAsync(string notebookId, string prompt, string language, string style,
        string? customStyle, IReadOnlyList<string> sourceIds, CancellationToken cancellationToken = default);
    Task<NotebookLmCliResult> DownloadVideoAsync(string notebookId, string artifactId, string outputPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Isolated adapter for the community-maintained notebooklm-py CLI. It deliberately invokes only the
/// pinned local executable, never a shell, and never logs stdout, stderr, cookies, or provider payloads.
/// </summary>
public sealed class NotebookLmCliRunner(IConfiguration configuration, IWebHostEnvironment environment) : INotebookLmCliRunner
{
    private static readonly SemaphoreSlim AccountGate = new(1, 1);
    private const int MaxOutputCharacters = 4_000_000;
    private static readonly SemaphoreSlim VersionGate = new(1, 1);
    private static readonly Dictionary<string, bool> VersionMatches = new(StringComparer.OrdinalIgnoreCase);

    public async Task<NotebookLmCliResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null,
        bool mutating = false, CancellationToken cancellationToken = default)
    {
        var executable = configuration["NotebookLmCli:ExecutablePath"];
        var home = configuration["NotebookLmCli:HomeDirectory"];
        var profile = configuration["NotebookLmCli:Profile"] ?? "operator";
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(home))
            return new(false, "CLI_NOT_CONFIGURED", null, null);
        if (profile.Length > 80 || !Regex.IsMatch(profile, "^[A-Za-z0-9_.-]+$"))
            return new(false, "INVALID_PROFILE", null, null);
        var expectedVersion = configuration["NotebookLmCli:ExpectedVersion"] ?? "0.8.6";
        if (!await CheckPinnedVersion(executable, expectedVersion, cancellationToken))
            return new(false, "CLI_VERSION_MISMATCH", null, null);

        var timeoutSeconds = int.TryParse(configuration["NotebookLmCli:TimeoutSeconds"], out var configuredTimeout)
            ? Math.Clamp(configuredTimeout, 5, 1800) : 120;
        await AccountGate.WaitAsync(cancellationToken);
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = environment.ContentRootPath
            };
            start.ArgumentList.Add("--profile");
            start.ArgumentList.Add(profile);
            start.ArgumentList.Add("--quiet");
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["NOTEBOOKLM_HOME"] = Path.GetFullPath(home);
            start.Environment.Remove("NOTEBOOKLM_AUTH_JSON");
            start.Environment.Remove("NOTEBOOKLM_DEBUG_RPC");

            using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            try
            {
                if (!process.Start()) return new(false, "PROCESS_START_FAILED", null, null);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new(false, "CLI_NOT_FOUND", null, null);
            }

            var stdoutTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
            var stderrTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (cancellationToken.IsCancellationRequested) throw;
                return new(false, mutating ? "OUTCOME_UNKNOWN" : "TIMEOUT", null, null, mutating);
            }

            var output = await stdoutTask;
            await stderrTask; // Never return or persist provider diagnostics verbatim.
            if (output.ExceededLimit) return new(false, "OUTPUT_TOO_LARGE", null, process.ExitCode);
            if (process.ExitCode != 0) return new(false, ReadProviderCode(output.Value) ?? "PROVIDER_ERROR", null, process.ExitCode, mutating);
            if (!TryValidateJson(output.Value)) return new(false, "INVALID_JSON_RESPONSE", null, process.ExitCode);
            return new(true, "OK", output.Value, process.ExitCode);
        }
        finally { AccountGate.Release(); }
    }

    private static async Task<(string Value, bool ExceededLimit)> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var output = new StringBuilder(); var buffer = new char[8192]; var tooLarge = false; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (!tooLarge && output.Length + count <= MaxOutputCharacters) output.Append(buffer, 0, count);
            else tooLarge = true;
        }
        return (output.ToString(), tooLarge);
    }

    private static async Task<bool> CheckPinnedVersion(string executable, string expected, CancellationToken ct)
    {
        var key = Path.GetFullPath(executable) + "|" + expected;
        if (VersionMatches.TryGetValue(key, out var known)) return known;
        await VersionGate.WaitAsync(ct);
        try
        {
            if (VersionMatches.TryGetValue(key, out var cached)) return cached;
            try
            {
                var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                info.ArgumentList.Add("--version");
                using var process = Process.Start(info);
                if (process is null) { VersionMatches[key] = false; return false; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                _ = await process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var matches = process.ExitCode == 0 && Regex.IsMatch(stdout, $@"(?<![0-9]){Regex.Escape(expected)}(?![0-9])");
                VersionMatches[key] = matches; return matches;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
            { VersionMatches[key] = false; return false; }
        }
        finally { VersionGate.Release(); }
    }

    private static string? ReadProviderCode(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            {
                var value = code.GetString();
                return value is not null && Regex.IsMatch(value, "^[A-Z0-9_]{1,80}$") ? value : null;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return null;
    }

    private static bool TryValidateJson(string output)
    {
        try { using var _ = JsonDocument.Parse(output); return true; }
        catch (JsonException) { return false; }
    }
}

public sealed class NotebookLmCliProvider(INotebookLmCliRunner runner) : INotebookLmProvider
{
    private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,160}$", RegexOptions.Compiled);
    private static readonly Regex SafeLanguage = new("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,3}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Styles = new(StringComparer.OrdinalIgnoreCase)
        { "auto", "classic", "whiteboard", "watercolor", "retro-print", "heritage", "paper-craft", "kawaii", "anime", "custom" };

    public Task<NotebookLmCliResult> CheckAuthenticationAsync(CancellationToken cancellationToken = default) =>
        Json(["auth", "check", "--test", "--json"], ct: cancellationToken);
    public Task<NotebookLmCliResult> ListProfilesAsync(CancellationToken cancellationToken = default) =>
        Json(["profile", "list", "--json"], ct: cancellationToken);
    public Task<NotebookLmCliResult> GetUsageAsync(CancellationToken cancellationToken = default) =>
        Json(["usage", "--json"], ct: cancellationToken);
    public Task<NotebookLmCliResult> ListNotebooksAsync(CancellationToken cancellationToken = default) =>
        Json(["list", "--json"], ct: cancellationToken);
    public Task<NotebookLmCliResult> GetNotebookAsync(string notebookId, CancellationToken cancellationToken = default) =>
        Json(["metadata", "--notebook", Id(notebookId), "--json"], ct: cancellationToken);
    public Task<NotebookLmCliResult> CreateNotebookAsync(string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) return Task.FromResult(new NotebookLmCliResult(false, "INVALID_TITLE", null, null));
        return Json(["create", title.Trim(), "--json"], mutating: true, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> CopyNotebookAsync(string notebookId, string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) return Task.FromResult(new NotebookLmCliResult(false, "INVALID_TITLE", null, null));
        return Json(["copy", title.Trim(), "--notebook", Id(notebookId), "--json"], mutating: true, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> ListSourcesAsync(string notebookId, CancellationToken cancellationToken = default) =>
        Json(["source", "list", "--notebook", Id(notebookId), "--json"], ct: cancellationToken);
    public async Task<NotebookLmCliResult> AddSourceAsync(string notebookId, string url, CancellationToken cancellationToken = default)
    {
        if (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0 || uri.IsLoopback || IsNonPublicAddress(uri.Host))
            return new(false, "INVALID_SOURCE_URL", null, null);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(IsNonPublicAddress)) return new(false, "INVALID_SOURCE_URL", null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (System.Net.Sockets.SocketException) { return new(false, "SOURCE_HOST_UNRESOLVED", null, null); }
        return await Json(["source", "add", url, "--type", "url", "--notebook", Id(notebookId), "--json"], mutating: true, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> ListArtifactsAsync(string notebookId, string type = "all", CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "all", "video", "audio", "report", "slide-deck", "quiz", "flashcard", "infographic", "data-table", "mind-map" };
        if (!allowed.Contains(type)) return Task.FromResult(new NotebookLmCliResult(false, "INVALID_ARTIFACT_TYPE", null, null));
        return Json(["artifact", "list", "--notebook", Id(notebookId), "--type", type.ToLowerInvariant(), "--json"], ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> AskAsync(string notebookId, string prompt, IReadOnlyList<string> sourceIds, string? conversationId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 20_000) return Task.FromResult(new NotebookLmCliResult(false, "INVALID_PROMPT", null, null));
        if (conversationId is not null && !SafeId.IsMatch(conversationId)) return Task.FromResult(new NotebookLmCliResult(false, "INVALID_CONVERSATION_ID", null, null));
        // notebooklm-py documents --new as deleting the notebook's current
        // server-side conversation and all its turns. Preserve operator chat
        // history and continue the notebook's existing conversation instead.
        var args = new List<string> { "ask", "--prompt-file", "-", "--notebook", Id(notebookId), "--json" };
        if (conversationId is not null) { args.Add("--conversation-id"); args.Add(conversationId); }
        foreach (var sourceId in sourceIds) { args.Add("--source"); args.Add(Id(sourceId)); }
        return Json(args, prompt, mutating: true, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> StartResearchAsync(string notebookId, string query, string mode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2000 || mode is not ("fast" or "deep"))
            return Task.FromResult(new NotebookLmCliResult(false, "INVALID_RESEARCH_REQUEST", null, null));
        return Json(["source", "add-research", "--prompt-file", "-", "--notebook", Id(notebookId), "--mode", mode, "--no-wait", "--json"], query, mutating: true, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> ResearchStatusAsync(string notebookId, string? taskId, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "research", "status", "--notebook", Id(notebookId), "--json" };
        if (!string.IsNullOrWhiteSpace(taskId)) { args.Add("--run-id"); args.Add(Id(taskId)); }
        return Json(args, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> GenerateVideoAsync(string notebookId, string prompt, string language, string style,
        string? customStyle, IReadOnlyList<string> sourceIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 20_000 || !SafeLanguage.IsMatch(language) || !Styles.Contains(style) ||
            (style.Equals("custom", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(customStyle)) ||
            (customStyle?.Length ?? 0) > 1000)
            return Task.FromResult(new NotebookLmCliResult(false, "INVALID_VIDEO_CONFIGURATION", null, null));
        var args = new List<string> { "generate", "video", "--prompt-file", "-", "--notebook", Id(notebookId),
            "--format", "explainer", "--language", language, "--style", style.ToLowerInvariant(), "--no-wait", "--json" };
        if (style.Equals("custom", StringComparison.OrdinalIgnoreCase)) { args.Add("--style-prompt"); args.Add(customStyle!); }
        foreach (var sourceId in sourceIds) { args.Add("--source"); args.Add(Id(sourceId)); }
        return Json(args, prompt, mutating: true, ct: cancellationToken);
    }
    public Task<NotebookLmCliResult> DownloadVideoAsync(string notebookId, string artifactId, string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (!SafeId.IsMatch(artifactId) || string.IsNullOrWhiteSpace(outputPath))
            return Task.FromResult(new NotebookLmCliResult(false, "INVALID_ARTIFACT", null, null));
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath)) return Task.FromResult(new NotebookLmCliResult(false, "FILE_ALREADY_EXISTS", null, null));
        return Json(["download", "video", fullPath, "--notebook", Id(notebookId), "--artifact", artifactId, "--no-clobber", "--json"], mutating: true, ct: cancellationToken);
    }

    private Task<NotebookLmCliResult> Json(IReadOnlyList<string> args, string? input = null, bool mutating = false, CancellationToken ct = default) =>
        runner.RunAsync(args, input, mutating, ct);
    private static string Id(string value) => SafeId.IsMatch(value) ? value : throw new ArgumentException("NotebookLM identifiers must be provider IDs.");
    private static bool IsNonPublicAddress(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (IsNonPublicAddress(address)) return true;
        return false;
    }
    private static bool IsNonPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224 || (b[0] == 169 && b[1] == 254) ||
                   (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
        }
        var bytes = address.GetAddressBytes();
        return address.IsIPv6UniqueLocal || (bytes[0] & 0xe0) != 0x20 || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }
}

public sealed record NotebookLmQuotaAssessment(string Status, bool? IsExhausted, string? ActiveWindow,
    string? ResetAtUtc, bool? VideoAvailable, JsonElement? Evidence);

public static class NotebookLmQuotaManager
{
    public static NotebookLmQuotaAssessment Assess(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson)) return new("unavailable", null, null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(usageJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new("invalid_response", null, null, null, null, null);
            string? Text(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            bool? Bool(string name) => root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
            bool? video = null;
            if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray())
                    if (action.ValueKind == JsonValueKind.Object && action.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String &&
                        (kind.GetString()!.Equals("video_overview", StringComparison.OrdinalIgnoreCase) || kind.GetString()!.Equals("video", StringComparison.OrdinalIgnoreCase)) &&
                        action.TryGetProperty("has_sufficient_quota", out var available) && available.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        video = available.GetBoolean();
            }
            string? reset = null;
            if (root.TryGetProperty("windows", out var windows) && windows.ValueKind == JsonValueKind.Array)
                foreach (var window in windows.EnumerateArray())
                    if (window.ValueKind == JsonValueKind.Object && Text("active_window") is string active && window.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == active &&
                        window.TryGetProperty("resets_at", out var value) && value.ValueKind == JsonValueKind.String) reset = value.GetString();
            return new(Text("status") ?? "unknown", Bool("is_exhausted"), Text("active_window"), reset, video, root.Clone());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new("invalid_response", null, null, null, null, null); }
    }
}
