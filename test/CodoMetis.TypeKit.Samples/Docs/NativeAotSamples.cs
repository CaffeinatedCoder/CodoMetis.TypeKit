using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace ReadmeSamples.Docs.Aot;

// sample: native-aot/json-context
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Quantity : IValue<int>;

public sealed record OrderLine(OrderId Order, Quantity Quantity);

[JsonSerializable(typeof(OrderLine))]   // the shape; neither Guid nor int is needed for the JSON
internal partial class OrdersJson : JsonSerializerContext;
// end sample

// sample: native-aot/config-type
public enum HostFault { Blank }

public readonly partial record struct SmtpHost : IValidatedValue<SmtpHost, string, HostFault>
{
    public static Result<SmtpHost, HostFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(HostFault.Blank) : new SmtpHost(value.Trim());
}
// end sample

public static class Usage
{
    public static (string Json, bool RoundTrip) Context()
    {
        // sample: native-aot/json-use
        string json = JsonSerializer.Serialize(new OrderLine(OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee")), Quantity.From(3)), OrdersJson.Default.OrderLine);   // {"Order":"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee","Quantity":3}
        OrderLine back = JsonSerializer.Deserialize(json, OrdersJson.Default.OrderLine)!;
        bool roundTrip = back == new OrderLine(OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee")), Quantity.From(3));   // true
        // end sample

        return (json, roundTrip);
    }

    public static string Configuration()
    {
        // sample: native-aot/config-use
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Host"] = " mail.example.com " }).Build();

        Result<SmtpHost, HostFault> host = SmtpHost.Create(configuration["Smtp:Host"] ?? "");            // the wrapped type bound, then Create
        string bound = host.Match(h => h.Value, fault => fault.ToString());                                 // "mail.example.com"
        // end sample

        return bound;
    }

    // sample: native-aot/describe
    static string Describe(Type type) =>
        type.GetCustomAttribute<GeneratedValueObjectAttribute>() is { } valueObject
            ? $"{valueObject.ValueObjectType.Name} wraps {valueObject.WrappedType.Name}"
            : $"{type.Name} is no value object";
    // end sample

    public static (string Described, string Plain) Discovery()
    {
        // sample: native-aot/describe-use
        string described = Describe(typeof(OrderId));   // "OrderId wraps Guid"
        string plain = Describe(typeof(Guid));          // "Guid is no value object"
        // end sample

        return (described, plain);
    }
}

public sealed class NativeAotTests
{
    private const string Readme = "docs/native-aot.md";

    private static string Quoted(string text) => $"\"{text}\"";

    [Fact]
    public void A_context_that_lists_the_shape_alone_writes_the_value_objects()
    {
        var (json, roundTrip) = Usage.Context();

        json.ShouldBeShownIn(Readme, text => text, "JsonSerializer.Serialize(new OrderLine(OrderId.From(Guid.Parse(\"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee\")), Quantity.From(3)), OrdersJson.Default.OrderLine)");
        roundTrip.ShouldBeShownIn(Readme, value => value ? "true" : "false", "back == new OrderLine(OrderId.From(Guid.Parse(\"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee\")), Quantity.From(3))");
        OrdersJson.Default.GetTypeInfo(typeof(Guid)).ShouldBeNull("the context lists no wrapped type, and the converter needs none");
    }

    [Fact]
    public void Configuration_binds_the_wrapped_type_and_Create_applies() =>
        Usage.Configuration().ShouldBeShownIn(Readme, Quoted, "host.Match(h => h.Value, fault => fault.ToString())");

    [Fact]
    public void A_value_object_is_recognised_by_its_attribute()
    {
        var (described, plain) = Usage.Discovery();

        described.ShouldBeShownIn(Readme, Quoted, "Describe(typeof(OrderId))");
        plain.ShouldBeShownIn(Readme, Quoted, "Describe(typeof(Guid))");
    }
}
