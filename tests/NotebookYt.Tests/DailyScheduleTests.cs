using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class DailyScheduleTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("private", 0)]
    [InlineData("unlisted", 21)]
    [InlineData("public", 42)]
    public async Task DailyPlanningAndReschedulingRespectUploadVisibility(string visibility, int offset)
    {
        var channel = await (await fixture.Client.PutAsJsonAsync("/api/channel", new { name = "Daily planning fixture", timeZone = "Europe/Madrid" })).Content.ReadFromJsonAsync<JsonElement>();
        var week = await fixture.Client.GetFromJsonAsync<JsonElement>("/api/week");
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var channelId = channel.GetProperty("id").GetGuid(); var settings = await db.Set<ChannelSettings>().FindAsync(channelId);
        if (settings is null) { settings = new ChannelSettings { Id = channelId }; db.Add(settings); }
        settings.Visibility = visibility; settings.SchedulingEnabled = true; settings.HorizonDays = 7; settings.LeadDays = 0;
        var topic = new EditorialTopic { ContentNicheId = week[0].GetProperty("id").GetGuid(), Title = "Daily topic " + visibility, NormalizedTitle = "DAILY TOPIC " + visibility, Status = "Approved" }; db.Add(topic); await db.SaveChangesAsync();
        var now = new DateTime(2027, 1, 4, 0, 0, 0, DateTimeKind.Utc).AddDays(offset);
        await EditorialScheduler.Maintain(db, now);
        var workspace = await db.Set<ProductionWorkspace>().SingleAsync(w => w.TopicId == topic.Id);
        var slot = await db.Set<ScheduleSlot>().SingleAsync(s => s.ProductionId == workspace.Id);
        Assert.Equal(visibility, workspace.Visibility);
        Assert.Equal(visibility == "public" ? slot.PublishAtUtc : (DateTime?)null, workspace.PublishAtUtc);
        if (visibility != "public")
        {
            workspace.PublishAtUtc = slot.PublishAtUtc; await db.SaveChangesAsync();
            await EditorialScheduler.Maintain(db, now);
            Assert.Null(workspace.PublishAtUtc);
            Assert.True(await db.Set<StudioEvent>().AnyAsync(e => e.ProductionId == workspace.Id && e.Kind == "PrivateScheduleAligned"));
        }
        var response = await fixture.Client.PutAsJsonAsync($"/api/studio/slots/{slot.Id}", new { date = DateOnly.FromDateTime(now.AddDays(7)), time = new TimeOnly(19, 0) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await db.Entry(workspace).ReloadAsync(); await db.Entry(slot).ReloadAsync();
        Assert.Equal(visibility == "public" ? slot.PublishAtUtc : (DateTime?)null, workspace.PublishAtUtc);
        Assert.Null(workspace.ApprovedAtUtc);
    }
}
