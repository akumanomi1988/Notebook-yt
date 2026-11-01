using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public sealed class NotebookLmWorkflowTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddSourceReconciliationAllowsNotebookMarkdownSourcesWithoutUrls()
    {
        const string notebookId = "notebook_markdown_source_test";
        var provider = new RegistrationProvider("""{"notebook_id":"notebook_markdown_source_test","sources":[{"id":"guide_1","title":"research.md","type":"markdown","status":"ready"},{"id":"source_1","url":"https://example.org/previous","status":"ready"}]}""");
        await using var db = CreateDb();
        var operation = new NotebookLmOperation
        {
            Kind = "AddSource", NotebookId = notebookId, IdempotencyKey = "markdown-source-url-missing-" + Guid.NewGuid().ToString("N"),
            RequestJson = """{"url":"https://example.org/new","affectExpertKnowledge":true}"""
        };
        db.Add(operation);
        await db.SaveChangesAsync();

        var job = new BackgroundJob { Kind = "RunNotebookLmOperation", ResourceId = operation.Id, Status = "Running" };
        var processor = new NotebookLmOperationProcessor(db, provider, new MediaStorage(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = fixture.StorageRoot }).Build()));
        await processor.Execute(job, CancellationToken.None);

        Assert.Equal(1, provider.SourceAddCount);
        Assert.Equal("Completed", operation.Status);
    }

    [Fact]
    public async Task ExpertRegistrationReconcilesTheSameRealNotebookAndSpecialization()
    {
        await using var factory = fixture.CreateFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotebookLmProvider>();
            services.AddSingleton<INotebookLmProvider>(new RegistrationProvider());
        }));
        using var client = await factory.CreateLocalClientAsync();
        var notebookId = "notebook_registration_test";
        var input = new
        {
            name = "Research expert", specialization = "research", description = "Initial description",
            notebookId, notebookUrl = $"https://notebooklm.google.com/notebook/{notebookId}",
            instructions = "Use evidence.", nicheIds = Array.Empty<Guid>(), stageKeys = new[] { "research" }, enabled = true
        };

        var created = await client.PostAsJsonAsync("/api/studio/notebooklm/experts", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();

        var repeated = await client.PostAsJsonAsync("/api/studio/notebooklm/experts", new
        {
            input.name, input.specialization, description = "Reconciled description", input.notebookId,
            input.notebookUrl, input.instructions, input.nicheIds, input.stageKeys, input.enabled
        });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(createdBody.GetProperty("id").GetGuid(), repeatedBody.GetProperty("id").GetGuid());
        Assert.Equal("Reconciled description", repeatedBody.GetProperty("description").GetString());

        var experts = await client.GetFromJsonAsync<JsonElement[]>("/api/studio/notebooklm/experts");
        Assert.NotNull(experts);
        Assert.Single(experts, row => row.GetProperty("notebookId").GetString() == notebookId &&
            row.GetProperty("specialization").GetString() == "research");
    }

    [Fact]
    public async Task WorkflowRunsAdvanceOnlyAfterApprovalAndRejectBlocksLaterStages()
    {
        var channel = await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Workflow test", timeZone = "Europe/Madrid" });
        Assert.Equal(HttpStatusCode.OK, channel.StatusCode);
        var niche = (await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week"))[0].GetProperty("id").GetGuid();
        var productionResponse = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = niche, topic = "Workflow topic", plannedDate = "2026-11-01" });
        Assert.Equal(HttpStatusCode.Created, productionResponse.StatusCode);
        var productionId = (await productionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var expertId = Guid.NewGuid();
        await using (var db = CreateDb())
        {
            db.Add(new NotebookExpert
            {
                Id = expertId, Name = "Test expert", Specialization = "workflow-test", NotebookId = "real-notebook-id",
                NotebookUrl = "https://notebook.google.com/notebook/real-notebook-id", Instructions = "Use source evidence.",
                StageKeysJson = "[\"research\",\"strategy\",\"script\"]", NicheIdsJson = "[]", Enabled = true
            });
            await db.SaveChangesAsync();
        }

        var body = new
        {
            mode = "Supervised",
            steps = new[]
            {
                new { expertId, stageKey = "research", prompt = "Verify the evidence.", sourceIds = new[] { "source_a" } },
                new { expertId, stageKey = "strategy", prompt = "Recommend the audience angle.", sourceIds = new[] { "source_a" } },
                new { expertId, stageKey = "script", prompt = "Outline the explanation.", sourceIds = new[] { "source_a" } }
            }
        };
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/productions/{productionId}/expert-workflows") { Content = JsonContent.Create(body) };
        firstRequest.Headers.Add("Idempotency-Key", "workflow-test-key");
        var created = await fixture.Client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var runs = await created.Content.ReadFromJsonAsync<ExpertWorkflowRun[]>();
        Assert.NotNull(runs);
        Assert.Equal(new[] { "Pending", "WaitingForApproval", "WaitingForApproval" }, runs.Select(x => x.Status));
        Assert.Single(runs.Select(x => x.WorkflowId).Distinct());

        await using (var db = CreateDb())
        {
            Assert.Single(await db.Set<BackgroundJob>().Where(x => x.Kind == "RunNotebookLmExpertStage").ToListAsync());
            var first = await db.Set<ExpertWorkflowRun>().SingleAsync(x => x.Id == runs[0].Id);
            first.Status = "AwaitingReview"; first.Response = "Evidence checked.";
            await db.SaveChangesAsync();
        }
        var approve = await fixture.Client.PostAsJsonAsync($"/api/studio/productions/{productionId}/expert-runs/{runs[0].Id}/review",
            new { approved = true, evidence = "Checked the cited source IDs.", correctedResponse = "Evidence checked." });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var duplicateApproval = await fixture.Client.PostAsJsonAsync($"/api/studio/productions/{productionId}/expert-runs/{runs[0].Id}/review",
            new { approved = true, evidence = "Duplicate request must not advance the workflow again.", correctedResponse = "Evidence checked." });
        Assert.Equal(HttpStatusCode.Conflict, duplicateApproval.StatusCode);

        await using (var db = CreateDb())
        {
            var stages = await db.Set<ExpertWorkflowRun>().Where(x => x.WorkflowId == runs[0].WorkflowId).OrderBy(x => x.Sequence).ToListAsync();
            Assert.Equal("Approved", stages[0].Status);
            Assert.Equal("Pending", stages[1].Status);
            Assert.Equal("WaitingForApproval", stages[2].Status);
            Assert.Equal(2, await db.Set<BackgroundJob>().CountAsync(x => x.Kind == "RunNotebookLmExpertStage"));
            stages[1].Status = "AwaitingReview"; stages[1].Response = "Strategy ready.";
            await db.SaveChangesAsync();
        }
        var reject = await fixture.Client.PostAsJsonAsync($"/api/studio/productions/{productionId}/expert-runs/{runs[1].Id}/review",
            new { approved = false, evidence = "The proposed angle is not sufficiently supported.", correctedResponse = "" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        await using (var db = CreateDb())
        {
            var last = await db.Set<ExpertWorkflowRun>().SingleAsync(x => x.Id == runs[2].Id);
            Assert.Equal("Blocked", last.Status);
            Assert.Equal("PRIOR_STAGE_REJECTED", last.LastErrorCode);
            Assert.Equal(2, await db.Set<BackgroundJob>().CountAsync(x => x.Kind == "RunNotebookLmExpertStage"));
        }

        var repeated = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/productions/{productionId}/expert-workflows") { Content = JsonContent.Create(body) };
        repeated.Headers.Add("Idempotency-Key", "workflow-test-key");
        var sameWorkflow = await fixture.Client.SendAsync(repeated);
        Assert.Equal(HttpStatusCode.OK, sameWorkflow.StatusCode);
        Assert.Equal(runs.Select(x => x.Id), (await sameWorkflow.Content.ReadFromJsonAsync<ExpertWorkflowRun[]>())!.Select(x => x.Id));

        var secondProductionResponse = await fixture.Client.PostAsJsonAsync("/api/productions", new { contentNicheId = niche, topic = "Another workflow topic", plannedDate = "2026-11-02" });
        Assert.Equal(HttpStatusCode.Created, secondProductionResponse.StatusCode);
        var secondProductionId = (await secondProductionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var sameKeyForAnotherProduction = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/productions/{secondProductionId}/expert-workflows") { Content = JsonContent.Create(body) };
        sameKeyForAnotherProduction.Headers.Add("Idempotency-Key", "workflow-test-key");
        Assert.Equal(HttpStatusCode.Accepted, (await fixture.Client.SendAsync(sameKeyForAnotherProduction)).StatusCode);

        var automaticBody = new { steps = new[] { new { expertId, stageKey = "research", prompt = "Verify the evidence.", sourceIds = new[] { "source_a" } }, new { expertId, stageKey = "strategy", prompt = "Recommend an angle.", sourceIds = new[] { "source_a" } } } };
        var automaticRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/productions/{secondProductionId}/expert-workflows") { Content = JsonContent.Create(automaticBody) };
        automaticRequest.Headers.Add("Idempotency-Key", "automatic-workflow-test-key");
        var automaticCreated = await fixture.Client.SendAsync(automaticRequest);
        Assert.Equal(HttpStatusCode.Accepted, automaticCreated.StatusCode);
        var automaticRuns = await automaticCreated.Content.ReadFromJsonAsync<ExpertWorkflowRun[]>();
        Assert.NotNull(automaticRuns);
        Assert.Equal(new[] { "Pending", "WaitingForExecution" }, automaticRuns.Select(x => x.Status));
        Assert.All(automaticRuns, x => Assert.Equal("Automatic", x.WorkflowMode));

        var invalidModeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/productions/{secondProductionId}/expert-workflows")
        { Content = JsonContent.Create(new { mode = "Unattended", steps = automaticBody.steps }) };
        invalidModeRequest.Headers.Add("Idempotency-Key", "invalid-workflow-mode-key");
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.SendAsync(invalidModeRequest)).StatusCode);
    }

    private EditorialDb CreateDb() => new(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);

    private sealed class RegistrationProvider : INotebookLmProvider
    {
        private readonly string sourcesJson;
        public RegistrationProvider(string? sourcesJson = null) => this.sourcesJson = sourcesJson ?? "{}";
        public int SourceAddCount { get; private set; }
        public Task<NotebookLmCliResult> GetNotebookAsync(string notebookId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotebookLmCliResult(true, "OK", JsonSerializer.Serialize(new { id = notebookId }), 0));
        private static Task<NotebookLmCliResult> Unsupported() => Task.FromResult(new NotebookLmCliResult(false, "UNSUPPORTED_TEST_OPERATION", null, 0));
        public Task<NotebookLmCliResult> CheckAuthenticationAsync(CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> ListProfilesAsync(CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> GetUsageAsync(CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> ListNotebooksAsync(CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> CreateNotebookAsync(string title, CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> CopyNotebookAsync(string notebookId, string title, CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> ListSourcesAsync(string notebookId, CancellationToken cancellationToken = default) => Task.FromResult(new NotebookLmCliResult(true, "OK", sourcesJson, 0));
        public Task<NotebookLmCliResult> AddSourceAsync(string notebookId, string url, CancellationToken cancellationToken = default)
        {
            SourceAddCount++;
            return Task.FromResult(new NotebookLmCliResult(true, "OK", JsonSerializer.Serialize(new { id = "new_source", url }), 0));
        }
        public Task<NotebookLmCliResult> ListArtifactsAsync(string notebookId, string type = "all", CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> AskAsync(string notebookId, string prompt, IReadOnlyList<string> sourceIds, string? conversationId = null, CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> StartResearchAsync(string notebookId, string query, string mode, CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> ResearchStatusAsync(string notebookId, string? taskId, CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> GenerateVideoAsync(string notebookId, string prompt, string language, string style, string? customStyle, IReadOnlyList<string> sourceIds, CancellationToken cancellationToken = default) => Unsupported();
        public Task<NotebookLmCliResult> DownloadVideoAsync(string notebookId, string artifactId, string outputPath, CancellationToken cancellationToken = default) => Unsupported();
    }
}
