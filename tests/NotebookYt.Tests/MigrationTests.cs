using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NotebookYt.Api;

namespace NotebookYt.Tests;

public class MigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task EmptyIsolatedDatabaseCanMigrateDownAndBackUp()
    {
        await using var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(fixture.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>();
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await migrator.MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Channels.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
