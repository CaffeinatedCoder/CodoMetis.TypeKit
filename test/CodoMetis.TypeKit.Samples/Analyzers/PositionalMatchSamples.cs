using CodoMetis.TypeKit.ClosedSets;

namespace ReadmeSamples.Analyzers.PositionalMatch;

public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}

public static class Labels
{
    public static (string Label, string Named) Of(OrderStatus status)
    {
#pragma warning disable CMTK0012 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0012
        var label = status.Match(() => "Open", () => "On its way", () => "Cancelled");   // CMTK0012: by position
        var named = status.Match(open: () => "Open", shipped: () => "On its way", cancelled: () => "Cancelled");
        // end sample
#pragma warning restore CMTK0012

        return (label, named);
    }
}

public sealed class PositionalMatchTests
{
    [Fact]
    public void Both_calls_pick_the_branch_of_the_member_while_the_order_is_unchanged()
    {
        Labels.Of(OrderStatus.Shipped).ShouldBe(("On its way", "On its way"));
    }
}
