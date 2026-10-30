namespace NotebookYt.Api;

public record ChannelInput(string Name, string TimeZone);
public record NicheInput(string Name, string Description, string Guidance, bool Enabled);
public record ProductionInput(Guid ContentNicheId, string Topic, DateOnly PlannedDate);
public record ProductionEdit(string Topic, DateOnly PlannedDate);
public record StatusInput(ProductionStatus? Status);
public record NotebookInput(string Url);

public record ChannelResponse(Guid Id, string Name, string TimeZone);
public record NicheResponse(Guid Id, Guid ChannelId, int Weekday, string Name, string Description, string Guidance, bool Enabled);
public record ProductionResponse(Guid Id, Guid ChannelId, Guid ContentNicheId, string NicheNameSnapshot,
    string Topic, DateOnly PlannedDate, ProductionStatus Status, DateTime CreatedAtUtc, DateTime ModifiedAtUtc, string NextManualAction);
public record HistoryResponse(Guid Id, ProductionStatus? From, ProductionStatus To, DateTime ChangedAtUtc);
public record NotebookResponse(Guid Id, Guid ProductionId, string Url, DateTime RegisteredAtUtc);
public record ProductionDetail(ProductionResponse Production, List<HistoryResponse> History, NotebookResponse? Notebook);

public static class ResponseMapping
{
    public static ChannelResponse ToResponse(this Channel c) => new(c.Id, c.Name, c.TimeZone);
    public static NicheResponse ToResponse(this ContentNiche n) => new(n.Id, n.ChannelId, n.Weekday, n.Name, n.Description, n.Guidance, n.Enabled);
    public static ProductionResponse ToResponse(this Production p) => new(p.Id, p.ChannelId, p.ContentNicheId,
        p.NicheNameSnapshot, p.Topic, p.PlannedDate, p.Status, p.CreatedAtUtc, p.ModifiedAtUtc, ProductionPolicy.NextManualAction(p.Status));
    public static HistoryResponse ToResponse(this ProductionStatusHistory h) => new(h.Id, h.From, h.To, h.ChangedAtUtc);
    public static NotebookResponse ToResponse(this NotebookReference n) => new(n.Id, n.ProductionId, n.Url, n.RegisteredAtUtc);
}
