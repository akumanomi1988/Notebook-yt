using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api;

public sealed class EditorialDb(DbContextOptions<EditorialDb> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ContentNiche> Niches => Set<ContentNiche>();
    public DbSet<Production> Productions => Set<Production>();
    public DbSet<NotebookReference> NotebookReferences => Set<NotebookReference>();
    public DbSet<ProductionStatusHistory> StatusHistory => Set<ProductionStatusHistory>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Channel>().Property(x => x.Name).HasMaxLength(160);
        model.Entity<Channel>().Property(x => x.TimeZone).HasMaxLength(100);
        model.Entity<ContentNiche>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ContentNiche>().HasIndex(x => new { x.ChannelId, x.Weekday }).IsUnique();
        model.Entity<ContentNiche>().ToTable(t => t.HasCheckConstraint("CK_Niche_Weekday", "\"Weekday\" BETWEEN 1 AND 7"));
        model.Entity<ContentNiche>().Property(x => x.Name).HasMaxLength(160);
        model.Entity<ContentNiche>().Property(x => x.Description).HasMaxLength(2000);
        model.Entity<ContentNiche>().Property(x => x.Guidance).HasMaxLength(4000);
        model.Entity<Production>().Property(x => x.Topic).HasMaxLength(300);
        model.Entity<Production>().Property(x => x.NicheNameSnapshot).HasMaxLength(160);
        model.Entity<Production>().Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        model.Entity<Production>().HasOne<ContentNiche>().WithMany().HasForeignKey(x => x.ContentNicheId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Production>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NotebookReference>().HasIndex(x => x.ProductionId).IsUnique();
        model.Entity<NotebookReference>().Property(x => x.Url).HasMaxLength(2048);
        model.Entity<NotebookReference>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionStatusHistory>().Property(x => x.From).HasConversion<string>();
        model.Entity<ProductionStatusHistory>().Property(x => x.To).HasConversion<string>();
        model.Entity<ProductionStatusHistory>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
    }
}
