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
