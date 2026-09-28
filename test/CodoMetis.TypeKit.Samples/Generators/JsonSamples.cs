using System.Text.Json;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;

namespace ReadmeSamples.Generators;

public sealed record Order(OrderId Id, Quantity Quantity);

public static class Json
{
    public static void Use(string json)
    {
        // sample: CodoMetis.TypeKit.Generators/json
        JsonSerializer.Serialize(new Order(OrderId.New(), Quantity.From(3)));
        // {"Id":"0199a3f4-1c00-7000-8000-000000000001","Quantity":3}

        JsonSerializer.Deserialize<Email>("\"not an address\"");   // JsonException naming Email and NoAt, not the text
        JsonSerializer.Deserialize<Dictionary<OrderId, int>>(json); // value objects work as dictionary keys
        // end sample
    }
}

public sealed class JsonTests
{
    private const string Readme = "src/CodoMetis.TypeKit.Generators/README.md";

    /// <summary>The sample's id is a new one, so the test writes an order with the id the comment shows.</summary>
    [Fact]
    public void An_order_is_written_as_the_comment_shows()
    {
        var shown = SampleOutputs.Shown(Readme, "JsonSerializer.Serialize(new Order(OrderId.New(), Quantity.From(3)))");
        var id = OrderId.From(JsonNode.Parse(shown)!["Id"]!.GetValue<Guid>());

        JsonSerializer.Serialize(new Order(id, Quantity.From(3))).ShouldBe(shown);
    }

    [Fact]
    public void A_refused_email_is_the_exception_the_comment_says() =>
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Email>("\"not an address\""), Readme, input: "not an address");

    [Fact]
    public void Value_objects_work_as_dictionary_keys()
    {
        var id = OrderId.New();
        var json = JsonSerializer.Serialize(new Dictionary<OrderId, int> { [id] = 2 });

        JsonSerializer.Deserialize<Dictionary<OrderId, int>>(json).ShouldBe(new Dictionary<OrderId, int> { [id] = 2 });
    }
}
