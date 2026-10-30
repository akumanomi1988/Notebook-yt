namespace NotebookYt.Api;

public sealed class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Local channel";
    public string TimeZone { get; set; } = "Etc/UTC";
}
public sealed class ContentNiche
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public int Weekday { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Guidance { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public enum ProductionStatus { Draft, BriefReady, AwaitingNotebook, Blocked, Cancelled }
public sealed class Production
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Guid ContentNicheId { get; set; }
    public string NicheNameSnapshot { get; set; } = "";
    public string Topic { get; set; } = "";
    public DateOnly PlannedDate { get; set; }
    public ProductionStatus Status { get; set; } = ProductionStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class NotebookReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public string Url { get; set; } = "";
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class ProductionStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public ProductionStatus? From { get; set; }
    public ProductionStatus To { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
