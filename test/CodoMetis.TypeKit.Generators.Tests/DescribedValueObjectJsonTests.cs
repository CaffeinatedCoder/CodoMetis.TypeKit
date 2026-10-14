using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// A value object in the JSON description architecture: its JSON is its wrapped type's under the options,
/// so a decimal converter registered once reaches every value object over a decimal, and its schema is
/// described as the wrapped type's. A closed set is a string of its members' names. The application's
/// own description of a value object comes first.
/// </summary>
public sealed class DescribedValueObjectJsonTests
{
    private const string DecimalString = """{"type":"string","format":"decimal","pattern":"^-?(?:0|[1-9]\\d*)(?:\\.\\d*[1-9])?$"}""";

    private static JsonSerializerOptions Options(params System.Text.Json.Serialization.JsonConverter[] converters)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        foreach (var converter in converters) options.Converters.Add(converter);
        return options;
    }

    public sealed record Invoice(ProbeAmount Total, ProbeAmount? Discount, List<ProbeAmount> Lines, Dictionary<string, ProbeAmount> Taxes, ProbeStatus Status);

    [Fact]
    public void A_value_object_over_a_decimal_is_written_by_the_options_decimal_converter_as_a_value_and_a_key()
    {
        var strings = Options(new DecimalStringJsonConverter());
        var numbers = Options(new DecimalNumberJsonConverter());

        JsonSerializer.Serialize(ProbeAmount.From(7.80m), strings).ShouldBe("\"7.8\"");
        JsonSerializer.Serialize(ProbeAmount.From(7.80m), numbers).ShouldBe("7.8");
        JsonSerializer.Serialize(new Dictionary<ProbeAmount, int> { [ProbeAmount.From(7.80m)] = 1 }, strings).ShouldBe("""{"7.8":1}""");
        JsonSerializer.Deserialize<ProbeAmount>("\"7.80\"", numbers).Value.ShouldBe(7.80m, "and reads what either converter reads");
        JsonSerializer.Serialize(ProbeAmount.From(7.80m), Options()).ShouldBe("7.80", "the control: without a converter, the scale it arrived with");
    }

    [Fact]
    public void A_value_object_is_described_as_its_wrapped_type_and_a_closed_set_as_its_names()
    {
        var properties = Options(new DecimalStringJsonConverter()).GetJsonSchemaAsNode(typeof(Invoice), DescribedJsonSchema.ExporterOptions())["properties"]!;

        properties["total"]!.ToJsonString().ShouldBe(DecimalString);
        properties["discount"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["lines"]!["items"]!.ToJsonString().ShouldBe(DecimalString);
        properties["taxes"]!["additionalProperties"]!.ToJsonString().ShouldBe(DecimalString);
        properties["status"]!.ToJsonString().ShouldBe("""{"type":"string","enum":["Open","Shipped","Cancelled"]}""");
    }

    /// <summary>A value object written as a quoted cent count, by a converter that replaces the generated one and says so.</summary>
    public sealed class CentsConverter : System.Text.Json.Serialization.JsonConverter<ProbeAmount>, IDescribedJsonConverter
    {
        public override ProbeAmount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ProbeAmount.From(decimal.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture) / 100);

        public override void Write(Utf8JsonWriter writer, ProbeAmount value, JsonSerializerOptions options) => writer.WriteStringValue(((long)(value.Value * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture));

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["pattern"] = "^-?[0-9]*$" });
    }

    public sealed record Cents([property: System.Text.Json.Serialization.JsonConverter(typeof(CentsConverter))] ProbeAmount Price, ProbeAmount Plain);

    /// <summary>
    /// A converter that replaces a value object's generated one, on the options or on a property, writes it,
    /// so the schema is what that converter says, not the wrapped type's.
    /// </summary>
    [Fact]
    public void A_described_converter_that_replaces_the_generated_one_is_described()
    {
        var registered = Options(new CentsConverter());
        JsonSerializer.Serialize(ProbeAmount.From(7.80m), registered).ShouldBe("\"780\"", "the converter the options put in charge writes it");
        registered.GetJsonSchemaAsNode(typeof(ProbeAmount), DescribedJsonSchema.ExporterOptions()).ToJsonString().ShouldBe("""{"type":"string","pattern":"^-?[0-9]*$"}""");

        var plain = Options();
        JsonSerializer.Serialize(new Cents(ProbeAmount.From(7.80m), ProbeAmount.From(1m)), plain).ShouldBe("""{"price":"780","plain":1}""");
        var properties = plain.GetJsonSchemaAsNode(typeof(Cents), DescribedJsonSchema.ExporterOptions())["properties"]!;
        properties["price"]!.ToJsonString().ShouldBe("""{"type":"string","pattern":"^-?[0-9]*$"}""", "the property's converter");
        properties["plain"]!["type"]!.ToJsonString().ShouldContain("number", Case.Sensitive, "its neighbour keeps the generated one");
    }

    /// <summary>A resolver that writes a value object as a quoted cent count, through a converter of its own.</summary>
    public sealed class CentsResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(ProbeAmount) ? JsonMetadataServices.CreateValueInfo<ProbeAmount>(options, new CentsConverter()) : null;
    }

    /// <summary>
    /// A resolver puts its own converter in charge as the options' converters do, so the schema is what
    /// that converter says: the generated description, the wrapped decimal's, came first, and a value
    /// written as "780" was described as a number (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_described_converter_a_resolver_puts_in_charge_is_described()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = JsonTypeInfoResolver.Combine(new CentsResolver(), new DefaultJsonTypeInfoResolver()) };

        JsonSerializer.Serialize(new Invoice(ProbeAmount.From(7.80m), ProbeAmount.From(1m), [], [], ProbeStatus.Open), options).ShouldStartWith("""{"total":"780","discount":"100",""", Case.Sensitive, "the resolver's converter writes it");

        var properties = options.GetJsonSchemaAsNode(typeof(Invoice), DescribedJsonSchema.ExporterOptions())["properties"]!;
        properties["total"]!.ToJsonString().ShouldBe("""{"type":"string","pattern":"^-?[0-9]*$"}""");
        properties["discount"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["lines"]!["items"]!.ToJsonString().ShouldBe("""{"type":"string","pattern":"^-?[0-9]*$"}""");
    }

    /// <summary>A value object's own description, its rule as a schema, comes before the wrapped type's.</summary>
    [Fact]
    public void The_application_s_description_of_a_value_object_comes_first()
    {
        var options = Options(new JsonSchemaDescriptions().Describe<ProbePercentage>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 100 })));

        options.GetJsonSchemaAsNode(typeof(ProbePercentage), DescribedJsonSchema.ExporterOptions()).ToJsonString()
               .ShouldBe("""{"type":"integer","minimum":0,"maximum":100}""");
        JsonSchemaDescription.For(typeof(ProbePercentage?), options)!.Schema!["maximum"]!.GetValue<int>().ShouldBe(100);
    }
}
