using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public sealed class NotebookLmCliTests
{
    [Fact]
    public async Task PrivateOrLoopbackSourceUrlsAreRejectedBeforeCallingNotebookLm()
    {
        var runner = new RecordingRunner(); var provider = new NotebookLmCliProvider(runner);
        foreach (var url in new[] { "https://127.0.0.1/admin", "https://192.168.1.20/source", "https://localhost/source", "https://service.local/source", "http://example.org/source" })
        {
            var result = await provider.AddSourceAsync("notebook_1", url);
            Assert.Equal("INVALID_SOURCE_URL", result.Code);
        }
        Assert.Empty(runner.Arguments);
    }

    [Fact]
    public async Task VideoRequestUsesOnlyExplainerAndExplicitSourceSelection()
    {
        var runner = new RecordingRunner(); var provider = new NotebookLmCliProvider(runner);
        var result = await provider.GenerateVideoAsync("book_1", "Answer the question", "es-ES", "classic", null, ["source_1"]);
        Assert.True(result.Success);
        Assert.Contains("explainer", runner.Arguments[0]);
        Assert.Contains("--source", runner.Arguments[0]);
        Assert.Contains("source_1", runner.Arguments[0]);
        Assert.DoesNotContain("cinematic", runner.Arguments[0]);
    }

    [Fact]
    public async Task ExpertQueryNeverDeletesExistingNotebookConversation()
    {
        var runner = new RecordingRunner(); var provider = new NotebookLmCliProvider(runner);
        var result = await provider.AskAsync("book_1", "Assess this production claim", ["source_1"]);

        Assert.True(result.Success);
        Assert.Contains("ask", runner.Arguments[0]);
        Assert.Contains("--notebook", runner.Arguments[0]);
        Assert.Contains("book_1", runner.Arguments[0]);
        Assert.Contains("--source", runner.Arguments[0]);
        Assert.DoesNotContain("--new", runner.Arguments[0]);
    }

    [Fact]
    public async Task ExpertCanResumeItsPersistedConversationWithoutStartingOrDeletingAChat()
    {
        var runner = new RecordingRunner(); var provider = new NotebookLmCliProvider(runner);
        var result = await provider.AskAsync("book_1", "Continue this approved stage", ["source_1"], "conversation_1");

        Assert.True(result.Success);
        Assert.Contains("--conversation-id", runner.Arguments[0]);
        Assert.Contains("conversation_1", runner.Arguments[0]);
        Assert.DoesNotContain("--new", runner.Arguments[0]);
    }

    [Fact]
    public async Task IsolatedExpertContextUsesNonDestructiveNotebookCopyCommand()
    {
        var runner = new RecordingRunner(); var provider = new NotebookLmCliProvider(runner);
        var result = await provider.CopyNotebookAsync("expert_book", "NotebookYt isolated a91c");

        Assert.True(result.Success);
        Assert.Equal(new[] { "copy", "NotebookYt isolated a91c", "--notebook", "expert_book", "--json" }, runner.Arguments[0]);
        Assert.DoesNotContain("--new", runner.Arguments[0]);
    }

    [Fact]
    public async Task InvalidLanguageOrCustomStyleWithoutPromptDoesNotCallCli()
    {
        var runner = new RecordingRunner(); var provider = new NotebookLmCliProvider(runner);
        Assert.Equal("INVALID_VIDEO_CONFIGURATION", (await provider.GenerateVideoAsync("book_1", "prompt", "es", "custom", null, ["source_1"])).Code);
        Assert.Equal("INVALID_VIDEO_CONFIGURATION", (await provider.GenerateVideoAsync("book_1", "prompt", "bad language", "classic", null, ["source_1"])).Code);
        Assert.Empty(runner.Arguments);
    }

    [Fact]
    public void UsageWithMissingFieldsRemainsUnknownInsteadOfZero()
    {
        var assessment = NotebookLmQuotaManager.Assess("""{"status":"ok","actions":[{"kind":"video_overview"}],"windows":[]}""");
        Assert.Null(assessment.VideoAvailable);
        Assert.Null(assessment.IsExhausted);
        Assert.Null(assessment.ResetAtUtc);
    }

    [Theory]
    [InlineData("https://notebooklm.google.com/notebook/notebook_1", "notebook_1", true)]
    [InlineData("https://notebook.cloud.google.com/global/notebook/notebook_1?project=123", "notebook_1", true)]
    [InlineData("https://notebooklm.google.com/notebook/notebook_10", "notebook_1", false)]
    [InlineData("https://notebooklm.google.com/notebook/prefix_notebook_1", "notebook_1", false)]
    [InlineData("https://notebooklm.google.com/wrong/notebook_1", "notebook_1", false)]
    [InlineData("https://example.com/notebook/notebook_1", "notebook_1", false)]
    [InlineData("https://notebooklm.google.com/notebook/notebook_1.evil", "notebook_1", false)]
    [InlineData("http://notebooklm.google.com/notebook/notebook_1", "notebook_1", false)]
    public void NotebookBindingRequiresAnExactIdSegmentOnAnOfficialHttpsHost(string url, string id, bool expected) =>
        Assert.Equal(expected, NotebookLmBindings.MatchesNotebook(url, id));

    private sealed class RecordingRunner : INotebookLmCliRunner
    {
        public List<string[]> Arguments { get; } = [];
        public Task<NotebookLmCliResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null, bool mutating = false, CancellationToken cancellationToken = default)
        {
            Arguments.Add(arguments.ToArray());
            return Task.FromResult(new NotebookLmCliResult(true, "OK", "{}", 0));
        }
    }
}
