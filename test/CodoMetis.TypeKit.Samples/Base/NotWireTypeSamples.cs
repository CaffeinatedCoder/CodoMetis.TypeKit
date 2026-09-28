using System.Text.Json;
using CodoMetis.TypeKit;

namespace ReadmeSamples.Base.NotWireTypes;

public sealed record Customer(string Name);

public sealed record OrderDto(Option<Customer> Customer);

public static class NotWireTypes
{
    public static void Serialize(Customer customer)
    {
        // sample: CodoMetis.TypeKit/not-wire-types
        JsonSerializer.Serialize(new OrderDto(Option.Some(customer)));
        // NotSupportedException: Option<Customer> is not a wire type. A serialized shape says absent with a
        // nullable (Customer?), and ToOption() and OrNull() convert at the boundary. ...
        // end sample
    }
}

public sealed class NotWireTypeTests
{
    /// <summary>The comment below the sample: the exception, then the start of its message, cut off with "...".</summary>
    [Fact]
    public void Serializing_an_option_is_refused_with_the_message_the_comment_shows()
    {
        var shown = SampleOutputs.Shown("src/CodoMetis.TypeKit/README.md", "JsonSerializer.Serialize(new OrderDto(Option.Some(customer)))").Split(": ", 2);

        var refusal = Record.Exception(() => NotWireTypes.Serialize(new Customer("Ada"))).ShouldNotBeNull();

        refusal.GetType().Name.ShouldBe(shown[0]);
        shown[1].ShouldEndWith(" ...");
        refusal.Message.ShouldStartWith(shown[1][..^" ...".Length]);
    }
}
