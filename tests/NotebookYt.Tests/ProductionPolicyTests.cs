using NotebookYt.Api;

namespace NotebookYt.Tests;

public class ProductionPolicyTests
{
    [Fact]
    public void AllStatePairsFollowTheEditorialPolicy()
    {
        var allowed = new HashSet<(ProductionStatus, ProductionStatus)>
        {
            (ProductionStatus.Draft, ProductionStatus.BriefReady), (ProductionStatus.Draft, ProductionStatus.Blocked), (ProductionStatus.Draft, ProductionStatus.Cancelled),
            (ProductionStatus.BriefReady, ProductionStatus.AwaitingNotebook), (ProductionStatus.BriefReady, ProductionStatus.Blocked), (ProductionStatus.BriefReady, ProductionStatus.Cancelled),
            (ProductionStatus.AwaitingNotebook, ProductionStatus.Blocked), (ProductionStatus.AwaitingNotebook, ProductionStatus.Cancelled),
            (ProductionStatus.Blocked, ProductionStatus.Draft), (ProductionStatus.Blocked, ProductionStatus.BriefReady), (ProductionStatus.Blocked, ProductionStatus.AwaitingNotebook), (ProductionStatus.Blocked, ProductionStatus.Cancelled)
        };
        foreach (var from in Enum.GetValues<ProductionStatus>())
        foreach (var to in Enum.GetValues<ProductionStatus>())
            Assert.Equal(allowed.Contains((from, to)), ProductionPolicy.CanTransition(from, to));
    }
}
