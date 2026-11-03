using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class WorkflowDayTests
{
    [Theory]
    [InlineData("2026-03-29T12:00:00Z", 23)]
    [InlineData("2026-10-25T12:00:00Z", 25)]
    public void DailyLimitUsesCalendarDayAcrossClockChanges(string at, int hours)
    {
        var day = WorkflowControl.LocalDay(DateTime.Parse(at).ToUniversalTime(), "Europe/Madrid");
        Assert.Equal(hours, (day.End - day.Start).TotalHours);
    }
}

public class WorkflowControlTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private EditorialDb Db() => new(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);

    private async Task<(Guid First, Guid Second)> Create()
    {
        await using var db = Db();
        var channel = await db.Channels.SingleOrDefaultAsync();
        if (channel is null)
        {
            channel = new Channel { Name = "Workflow tests", TimeZone = "Europe/Madrid" }; db.Add(channel);
            for (var day = 1; day <= 7; day++) db.Add(new ContentNiche { ChannelId = channel.Id, Weekday = day, Name = "Niche " + day });
            await db.SaveChangesAsync();
        }
        var niche = await db.Niches.FirstAsync();
        var first = new Production { ChannelId = channel.Id, ContentNicheId = niche.Id, Topic = "First", NicheNameSnapshot = niche.Name };
        var second = new Production { ChannelId = channel.Id, ContentNicheId = niche.Id, Topic = "Second", NicheNameSnapshot = niche.Name };
        db.AddRange(first, second); await db.SaveChangesAsync(); return (first.Id, second.Id);
    }

    [Fact]
    public async Task CompletedUploadUsesConfirmationDateAndSurvivesRestart()
    {
        var ids = await Create(); var now = new DateTime(2035, 4, 12, 10, 0, 0, DateTimeKind.Utc);
        await using (var db = Db())
        {
            db.Add(new StudioEvent { ProductionId = ids.First, Kind = "YouTubeUploadConfirmed", CreatedAtUtc = now });
            await db.SaveChangesAsync();
        }
        await using var restarted = Db();
        await Assert.ThrowsAsync<StudioException>(() => WorkflowControl.EnsureDailyUpload(restarted, ids.Second, now.AddHours(2)));
        await WorkflowControl.EnsureDailyUpload(restarted, ids.Second, now.AddDays(1));
    }

    [Fact]
    public async Task AmbiguousUploadReservesTheChannelAcrossDays()
    {
        var ids = await Create();
        await using (var db = Db())
        {
            var media = new MediaArtifact { ProductionId = ids.First, Kind = "Video", SizeBytes = 1 }; db.Add(media);
            db.Add(new YouTubeUpload { ProductionId = ids.First, ArtifactId = media.Id, Status = "NeedsAttention" });
            await db.SaveChangesAsync();
        }
        await using var restarted = Db();
        var error = await Assert.ThrowsAsync<StudioException>(() => WorkflowControl.EnsureDailyUpload(restarted, ids.Second, DateTime.UtcNow.AddDays(1)));
        Assert.Contains("incierto", error.Message);
        await restarted.Set<YouTubeUpload>().Where(u => u.ProductionId == ids.First).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task LaterFailedExpertResultInvalidatesEarlierApproval()
    {
        var ids = await Create(); await using var db = Db();
        var expert = new NotebookExpert { Name = "Real binding", NotebookId = "notebook", Specialization = "research" }; db.Add(expert);
        var index = 1;
        foreach (var stage in WorkflowControl.FinalStages)
            db.Add(new ExpertWorkflowRun { ProductionId = ids.First, ExpertId = expert.Id, StageKey = stage, Sequence = index++,
                Status = "Approved", Response = "Persisted provider response", ConversationId = "conversation", IdempotencyKey = Guid.NewGuid().ToString() });
        await db.SaveChangesAsync(); Assert.Empty(await WorkflowControl.ExpertFailures(db, ids.First, true));
        db.Add(new ExpertWorkflowRun { ProductionId = ids.First, ExpertId = expert.Id, StageKey = "research", Sequence = index,
            Status = "Failed", IdempotencyKey = Guid.NewGuid().ToString() }); await db.SaveChangesAsync();
        Assert.Single(await WorkflowControl.ExpertFailures(db, ids.First, false));
    }

    [Fact]
    public async Task WorkerCannotGenerateVideoWithoutExpertPreparation()
    {
        var ids = await Create(); await using var db = Db();
        var runner = new MetadataOnlyRunner();
        var operation = new NotebookLmOperation { ProductionId = ids.Second, Kind = "GenerateVideo", NotebookId = "production-notebook",
            IdempotencyKey = Guid.NewGuid().ToString(), RequestJson = "{}" };
        db.Add(operation); await db.SaveChangesAsync();
        var processor = new NotebookLmOperationProcessor(db, new NotebookLmCliProvider(runner),
            new MediaStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = fixture.StorageRoot }).Build()));
        await processor.Execute(new BackgroundJob { ResourceId = operation.Id }, CancellationToken.None);
        Assert.Equal("Failed", operation.Status); Assert.Equal("EXPERT_PREPARATION_REQUIRED", operation.ErrorCode);
        Assert.Equal(0, runner.Mutations);
    }

    private sealed class MetadataOnlyRunner : INotebookLmCliRunner
    {
        public int Mutations { get; private set; }
        public Task<NotebookLmCliResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null,
            bool mutating = false, CancellationToken cancellationToken = default)
        {
            if (mutating) Mutations++;
            return Task.FromResult(new NotebookLmCliResult(true, "OK", "{\"id\":\"production-notebook\"}", 0));
        }
    }

    [Fact]
    public async Task DirectAgentUploadCannotSkipExpertStages()
    {
        var ids = await Create();
        var response = await fixture.Client.PostAsJsonAsync($"/api/studio/productions/{ids.Second}/upload",
            new { approved = true, evidence = "Delegated private authorization" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("experto", await response.Content.ReadAsStringAsync());
        await using var db = Db();
        Assert.False(await db.Set<YouTubeUpload>().AnyAsync(u => u.ProductionId == ids.Second));
    }

    [Fact]
    public async Task WebAndAgentReadTheSameIncompleteStateAndCannotExportIt()
    {
        var ids = await Create();
        var state = await fixture.Client.GetAsync($"/api/studio/productions/{ids.Second}/workflow");
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        Assert.Contains("readyForReserve\":false", await state.Content.ReadAsStringAsync());
        var export = await fixture.Client.GetAsync($"/api/studio/productions/{ids.Second}/reserve.zip");
        Assert.Equal(HttpStatusCode.Conflict, export.StatusCode);
        Assert.NotEqual("application/zip", export.Content.Headers.ContentType?.MediaType);
    }
}
