namespace NotebookYt.Api;

public static class ProductionPolicy
{
    public static bool CanTransition(ProductionStatus from, ProductionStatus to) => (from, to) switch
    {
        (ProductionStatus.Draft, ProductionStatus.BriefReady or ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.BriefReady, ProductionStatus.AwaitingNotebook or ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.AwaitingNotebook, ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.Blocked, ProductionStatus.Draft or ProductionStatus.BriefReady or ProductionStatus.AwaitingNotebook or ProductionStatus.Cancelled) => true,
        _ => false
    };

    public static string NextManualAction(ProductionStatus status) => status switch
    {
        ProductionStatus.Draft => "PrepareBrief",
        ProductionStatus.BriefReady => "PrepareNotebook",
        ProductionStatus.AwaitingNotebook => "ReviewNotebook",
        ProductionStatus.Blocked => "ResolveBlock",
        ProductionStatus.Cancelled => "None",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
