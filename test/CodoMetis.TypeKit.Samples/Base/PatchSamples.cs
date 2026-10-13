using System.Text.Json;
using CodoMetis.TypeKit;

namespace ReadmeSamples.Base.Patches;

public sealed class Order
{
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public int Quantity { get; set; }
}

// sample: CodoMetis.TypeKit/patch
public sealed record OrderPatch(Patch<string> Name, Patch<string> Nickname, Patch<int> Quantity);
// end sample

public static class Applying
{
    public static void Apply(Order order, OrderPatch patch)
    {
        // sample: CodoMetis.TypeKit/patch-apply
        patch.Name.Tap(name => order.Name = name);                       // a clear cannot reach a required field
        patch.Nickname.IfPresent(nickname => order.Nickname = nickname); // null clears an optional one
        order.Quantity = patch.Quantity.Match(
            quantity => quantity,
            () => throw new InvalidOperationException("Quantity cannot be cleared."),
            () => order.Quantity);
        // end sample
    }
}

public sealed class PatchTests
{
    private const string Readme = "src/CodoMetis.TypeKit/README.md";

    private static string Render<T>(Patch<T> patch) where T : notnull => patch.Match(value => $"Value: {value}", () => "Null", () => "Absent");

    [Fact]
    public void A_document_reads_as_the_comments_state()
    {
        // sample: CodoMetis.TypeKit/patch-read
        var patch = JsonSerializer.Deserialize<OrderPatch>("""{"nickname":null,"quantity":2}""", JsonSerializerOptions.Web)!;
        var name = patch.Name;           // Absent, the field was not mentioned
        var nickname = patch.Nickname;   // Null, the field was null
        var quantity = patch.Quantity;   // Value: 2
        // end sample

        patch.Name.ShouldBeShownIn(Readme, Render);
        patch.Nickname.ShouldBeShownIn(Readme, Render);
        patch.Quantity.ShouldBeShownIn(Readme, Render);
        _ = (name, nickname, quantity);
    }

    [Fact]
    public void Applying_sets_clears_and_keeps()
    {
        var order = new Order { Name = "Old", Nickname = "nick", Quantity = 1 };

        Applying.Apply(order, JsonSerializer.Deserialize<OrderPatch>("""{"name":"New","nickname":null}""", JsonSerializerOptions.Web)!);

        order.Name.ShouldBe("New");
        order.Nickname.ShouldBeNull();
        order.Quantity.ShouldBe(1);
    }
}
