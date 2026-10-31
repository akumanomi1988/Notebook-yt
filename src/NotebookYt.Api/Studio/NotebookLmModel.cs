using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class NotebookLmModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<NotebookExpert>().HasKey(x => x.Id);
        model.Entity<NotebookExpert>().Property(x => x.Name).HasMaxLength(160);
        model.Entity<NotebookExpert>().Property(x => x.Specialization).HasMaxLength(120);
        model.Entity<NotebookExpert>().Property(x => x.Description).HasMaxLength(2000);
        model.Entity<NotebookExpert>().Property(x => x.NotebookId).HasMaxLength(160);
        model.Entity<NotebookExpert>().Property(x => x.NotebookUrl).HasMaxLength(2048);
        model.Entity<NotebookExpert>().Property(x => x.Instructions).HasMaxLength(10000);
        model.Entity<NotebookExpert>().Property(x => x.LastErrorCode).HasMaxLength(80);
        model.Entity<NotebookExpert>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<NotebookExpert>().HasIndex(x => new { x.Enabled, x.Specialization });

        model.Entity<ExpertWorkflowRun>().HasKey(x => x.Id);
        model.Entity<ExpertWorkflowRun>().Property(x => x.StageKey).HasMaxLength(80);
        model.Entity<ExpertWorkflowRun>().Property(x => x.Status).HasMaxLength(40);
        model.Entity<ExpertWorkflowRun>().Property(x => x.IdempotencyKey).HasMaxLength(180);
        model.Entity<ExpertWorkflowRun>().Property(x => x.RequestHash).HasMaxLength(64);
        model.Entity<ExpertWorkflowRun>().Property(x => x.LastErrorCode).HasMaxLength(80);
        model.Entity<ExpertWorkflowRun>().HasIndex(x => x.IdempotencyKey).IsUnique();
        model.Entity<ExpertWorkflowRun>().HasIndex(x => new { x.ProductionId, x.Sequence }).IsUnique().HasFilter("\"Status\" <> 'Rejected'");
        model.Entity<ExpertWorkflowRun>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ExpertWorkflowRun>().HasOne<NotebookExpert>().WithMany().HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ExpertWorkflowRun>().ToTable(t => t.HasCheckConstraint("CK_ExpertRun_Status",
            "\"Status\" IN ('Pending','Running','AwaitingReview','Approved','Rejected','OutcomeUnknown','Failed')"));

        model.Entity<NotebookLmOperation>().HasKey(x => x.Id);
        model.Entity<NotebookLmOperation>().Property(x => x.Kind).HasMaxLength(60);
        model.Entity<NotebookLmOperation>().Property(x => x.NotebookId).HasMaxLength(160);
        model.Entity<NotebookLmOperation>().Property(x => x.IdempotencyKey).HasMaxLength(200);
        model.Entity<NotebookLmOperation>().Property(x => x.RequestHash).HasMaxLength(64);
        model.Entity<NotebookLmOperation>().Property(x => x.Status).HasMaxLength(40);
        model.Entity<NotebookLmOperation>().Property(x => x.ErrorCode).HasMaxLength(80);
        model.Entity<NotebookLmOperation>().HasIndex(x => x.IdempotencyKey).IsUnique();
        model.Entity<NotebookLmOperation>().HasIndex(x => new { x.Status, x.ModifiedAtUtc });
        model.Entity<NotebookLmOperation>().HasOne<Production>().WithMany().HasForeignKey(x => x.ProductionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NotebookLmOperation>().HasOne<MediaArtifact>().WithMany().HasForeignKey(x => x.MediaArtifactId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NotebookLmOperation>().ToTable(t => t.HasCheckConstraint("CK_NotebookLmOperation_Status",
            "\"Status\" IN ('Pending','Running','Completed','OutcomeUnknown','Failed','DeferredQuota')"));

        model.Entity<NotebookLmUsageSnapshot>().HasKey(x => x.Id);
        model.Entity<NotebookLmUsageSnapshot>().Property(x => x.ProfileName).HasMaxLength(80);
        model.Entity<NotebookLmUsageSnapshot>().Property(x => x.Status).HasMaxLength(40);
        model.Entity<NotebookLmUsageSnapshot>().Property(x => x.ActiveWindow).HasMaxLength(40);
        model.Entity<NotebookLmUsageSnapshot>().HasIndex(x => x.CheckedAtUtc);
    }
}
