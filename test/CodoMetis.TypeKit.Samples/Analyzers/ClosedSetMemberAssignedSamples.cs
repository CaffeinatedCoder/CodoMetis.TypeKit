using CodoMetis.TypeKit.ClosedSets;

namespace ReadmeSamples.Analyzers.ClosedSetMemberAssigned;

#pragma warning disable CMTK0013 // what the sample shows the rule reporting
// sample: CodoMetis.TypeKit.Analyzers/cmtk0013
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Closed = new();

    static OrderStatus() { Open = Closed; }   // CMTK0013: Open now holds the instance named "Closed"
}
// end sample
#pragma warning restore CMTK0013

/// <summary>What the README says the suppressed rule would have prevented, read from the woven set.</summary>
public sealed class ClosedSetMemberAssignedTests
{
    [Fact]
    public void Open_holds_the_instance_named_Closed()
    {
        OrderStatus.Open.Name.ShouldBe("Closed");
    }

    [Fact]
    public void Closed_takes_the_open_branch_and_Parse_disagrees_with_All()
    {
        OrderStatus.Closed.Match(open: () => "open", closed: () => "closed").ShouldBe("open");
        OrderStatus.Parse("Open").ToString().ShouldBe("Closed");
        OrderStatus.All[0].ShouldNotBeSameAs(OrderStatus.Parse("Open"));
    }
}
