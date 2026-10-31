using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class MigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task EmptyIsolatedDatabaseCanMigrateDownAndBackUp()
    {
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await migrator.MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Channels.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task LegacyRowsAndFrozenPackagesSurviveAdditiveEditorialMigration()
    {
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261030120828_CompleteStudio");
        var channel = new Channel { Name = "Legacy", TimeZone = "Europe/Madrid" }; db.Add(channel);
        var niches = Enumerable.Range(1, 7).Select(day => new ContentNiche { ChannelId = channel.Id, Weekday = day, Name = "Legacy " + day }).ToArray(); db.AddRange(niches);
        var production = new Production { ChannelId = channel.Id, ContentNicheId = niches[0].Id, Topic = "Legacy production", NicheNameSnapshot = "Legacy 1", PlannedDate = new(2026, 10, 30) }; db.Add(production);
        db.Add(new ProductionWorkspace { Id = production.Id, Stage = "NotebookPending" });
        var package = new SourcePackage { ProductionId = production.Id, Version = 1, Markdown = "Legacy immutable evidence", Sha256 = StudioRules.Hash("Legacy immutable evidence") }; db.Add(package);
        await db.SaveChangesAsync(); await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal("Legacy immutable evidence", (await db.Set<SourcePackage>().FindAsync(package.Id))!.Markdown);
        Assert.Equal(7, await db.Niches.CountAsync(n => n.ChannelId == channel.Id));
        Assert.False(await db.Set<EditorialConfiguration>().AnyAsync(c => c.ProductionId == production.Id));
        Assert.Empty(await EditorialWorkflow.ReviewFailures(db, production.Id, null));
        Assert.Equal("Legacy production", (await db.Productions.FindAsync(production.Id))!.Topic);
    }
}
