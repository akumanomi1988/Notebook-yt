using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class StudioModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ChannelSettings>().HasOne<Channel>().WithOne().HasForeignKey<ChannelSettings>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NicheSettings>().HasOne<ContentNiche>().WithOne().HasForeignKey<NicheSettings>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EditorialTopic>().HasOne<ContentNiche>().WithMany().HasForeignKey(x => x.ContentNicheId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EditorialTopic>().HasIndex(x => new { x.ContentNicheId, x.NormalizedTitle }).IsUnique();
        model.Entity<EditorialTopic>().Property(x => x.NormalizedTitle).HasMaxLength(300);
        model.Entity<EditorialTopic>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionWorkspace>().HasOne<Production>().WithOne().HasForeignKey<ProductionWorkspace>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionWorkspace>().HasOne<MediaArtifact>().WithMany().HasForeignKey(x => x.CanonicalArtifactId).OnDelete(DeleteBehavior.Restrict);
        foreach (var type in new[] { typeof(ResearchSource), typeof(SourcePackage), typeof(ManualNotebookTask), typeof(MediaArtifact), typeof(QualityReview), typeof(StudioEvent), typeof(BackgroundJob), typeof(YouTubeUpload) })
            model.Entity(type).HasOne(typeof(Production)).WithMany().HasForeignKey("ProductionId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ResearchSource>().HasIndex(x => new { x.ProductionId, x.Fingerprint }).IsUnique();
        model.Entity<ResearchSource>().Property(x => x.Fingerprint).HasMaxLength(64);
        model.Entity<ResearchSource>().HasOne<MediaArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SourcePackage>().HasIndex(x => new { x.ProductionId, x.Version }).IsUnique();
        model.Entity<ManualNotebookTask>().HasIndex(x => new { x.ProductionId, x.Kind }).IsUnique();
        model.Entity<ManualNotebookTask>().Property(x => x.Kind).HasMaxLength(80);
        model.Entity<MediaArtifact>().HasIndex(x => new { x.ProductionId, x.Kind, x.Sha256 }).IsUnique();
        model.Entity<MediaArtifact>().Property(x => x.Sha256).HasMaxLength(64);
        model.Entity<MediaArtifact>().Property(x => x.Kind).HasMaxLength(40);
        model.Entity<QualityReview>().HasOne<MediaArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BackgroundJob>().HasIndex(x => x.IdempotencyKey).IsUnique();
        model.Entity<BackgroundJob>().Property(x => x.IdempotencyKey).HasMaxLength(200);
        model.Entity<BackgroundJob>().HasIndex(x => new { x.Status, x.AvailableAtUtc });
        model.Entity<ScheduleSlot>().HasIndex(x => new { x.ChannelId, x.LocalDate }).IsUnique();
        model.Entity<ScheduleSlot>().HasIndex(x => x.ProductionId).IsUnique();
        model.Entity<ScheduleSlot>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ScheduleSlot>().HasOne<ContentNiche>().WithMany().HasForeignKey(x => x.ContentNicheId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ScheduleSlot>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GoogleConnection>().HasIndex(x => x.Kind).IsUnique();
        model.Entity<OAuthAttempt>().HasIndex(x => x.StateHash).IsUnique();
        model.Entity<NotebookSourceBinding>().HasIndex(x => x.ResearchSourceId).IsUnique();
        model.Entity<NotebookSourceBinding>().HasOne<ResearchSource>().WithMany().HasForeignKey(x => x.ResearchSourceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<YouTubeUpload>().HasIndex(x => x.ProductionId).IsUnique();
        model.Entity<YouTubeUpload>().HasOne<MediaArtifact>().WithMany().HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CreationReceipt>().HasIndex(x => x.Key).IsUnique();
        model.Entity<CreationReceipt>().Property(x => x.Key).HasMaxLength(128);
        model.Entity<CreationReceipt>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionWorkspace>().HasOne<EditorialTopic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManualNotebookTask>().ToTable(t => t.HasCheckConstraint("CK_ManualTask_Status", "\"Status\" IN ('Pending','Completed') AND (\"Status\" <> 'Completed' OR (\"CompletedAtUtc\" IS NOT NULL AND \"EvidenceKind\" IN ('OperatorAttested','ProviderVerified')))"));
        model.Entity<MediaArtifact>().ToTable(t => t.HasCheckConstraint("CK_Artifact_State", "\"SizeBytes\" > 0 AND \"Status\" IN ('PendingInspection','Valid','Rejected','Missing') AND \"Kind\" IN ('Video','Document','Thumbnail')"));
        model.Entity<EditorialTopic>().ToTable(t => t.HasCheckConstraint("CK_Topic_Status", "\"Status\" IN ('Proposed','Approved','Rejected') AND \"Priority\" BETWEEN 0 AND 100"));
    }
}
