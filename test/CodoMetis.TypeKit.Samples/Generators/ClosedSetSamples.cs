using System.Text.Json;
using CodoMetis.TypeKit.ClosedSets;

namespace ReadmeSamples.Generators.ClosedSets;

// sample: CodoMetis.TypeKit.Generators/closed-set
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}

public sealed partial class Tier : IClosedSet<Tier>
{
    public static readonly Tier Basic = new(1, null);
    public static readonly Tier Premium = new(2, "Priority support.");

    private Tier(int rank, string? perk) { Rank = rank; Perk = perk; }

    public int Rank { get; }
    public string? Perk { get; }
}
// end sample

public static class Using
{
    public static (string Label, string Names, bool Singleton, string Json) Run(OrderStatus status)
    {
        // sample: CodoMetis.TypeKit.Generators/closed-set-use
        var label = status.Match(open: () => "Open", shipped: () => "On its way", cancelled: () => "Cancelled");   // every member, or it does not compile
        var names = OrderStatus.All.Select(s => s.Name);                  // Open Shipped Cancelled
        var same = OrderStatus.Parse("Shipped") == OrderStatus.Shipped;   // True, members are singletons
        var json = JsonSerializer.Serialize(Tier.Premium);                // "Premium"
        // end sample

        return (label, string.Join(", ", names), same, json);
    }
}

public sealed class ClosedSetTests
{
    private const string Readme = "src/CodoMetis.TypeKit.Generators/README.md";

    [Fact]
    public void The_sample_answers_what_its_comments_state()
    {
        var (label, names, singleton, json) = Using.Run(OrderStatus.Shipped);

        label.ShouldBe("On its way");
        OrderStatus.All.Select(s => s.Name).ShouldBeShownIn(Readme, all => string.Join(" ", all));
        (OrderStatus.Parse("Shipped") == OrderStatus.Shipped).ShouldBeShownIn(Readme, b => b.ToString());
        JsonSerializer.Serialize(Tier.Premium).ShouldBeShownIn(Readme, j => j);
        names.ShouldBe("Open, Shipped, Cancelled");
        singleton.ShouldBeTrue();
        json.ShouldBe("\"Premium\"");
    }
}
