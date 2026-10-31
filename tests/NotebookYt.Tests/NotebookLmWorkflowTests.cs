using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public sealed class NotebookLmWorkflowTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
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
    }

    private EditorialDb CreateDb() => new(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
}
