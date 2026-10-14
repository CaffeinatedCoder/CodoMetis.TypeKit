using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="IDescribedJsonConverter"/> through System.Text.Json's own schema exporter: a converter of
/// one's own is the empty schema there, and a described one is what it says it writes, in every position.
/// </summary>
public sealed class DescribedJsonSchemaTests
{
    private const string DecimalString = """{"type":"string","format":"decimal","pattern":"^-?(?:0|[1-9]\\d*)(?:\\.\\d*[1-9])?$"}""";

    private static readonly JsonSerializerOptions Strings = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { new DecimalStringJsonConverter() } };

    public sealed record Order(decimal Total, decimal? Discount, List<decimal> Lines, Dictionary<string, decimal> Taxes, decimal?[] Gaps);

    /// <summary>An amount written as the decimal it holds, by a converter that says so.</summary>
    [JsonConverter(typeof(MoneyConverter))]
    public readonly record struct Money(decimal Amount);

    public sealed class MoneyConverter : JsonConverter<Money>, IDescribedJsonConverter
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(JsonSerializer.Deserialize<decimal>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Amount, options);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.SameAs(typeof(decimal));
    }

    /// <summary>A converter described as itself, through another: its schema would never end.</summary>
    [JsonConverter(typeof(LoopConverter))]
    public readonly record struct Loop(int Value);

    public sealed class LoopConverter : JsonConverter<Loop>, IDescribedJsonConverter
    {
        public override Loop Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, Loop value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.SameAs(typeof(Loop));
    }

    private static JsonNode Export<T>(JsonSerializerOptions options) => options.GetJsonSchemaAsNode(typeof(T), DescribedJsonSchema.ExporterOptions());

    [Fact]
    public void Without_a_description_a_converter_of_one_s_own_is_the_empty_schema()
    {
        // The control: what the hook exists for.
        Strings.GetJsonSchemaAsNode(typeof(decimal)).ToJsonString().ShouldBe("true");
    }

    [Fact]
    public void A_described_converter_is_its_schema_as_the_root_and_in_every_position()
    {
        Export<decimal>(Strings).ToJsonString().ShouldBe(DecimalString);

        var properties = Export<Order>(Strings)["properties"]!;
        properties["total"]!.ToJsonString().ShouldBe(DecimalString);
        properties["discount"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["discount"]!["pattern"].ShouldNotBeNull();
        properties["lines"]!["items"]!.ToJsonString().ShouldBe(DecimalString);
        properties["taxes"]!["additionalProperties"]!.ToJsonString().ShouldBe(DecimalString);
        properties["gaps"]!["items"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
    }

    [Fact]
    public void A_converter_described_as_another_type_is_that_type_s_schema_under_the_same_options()
    {
        Export<Money>(Strings).ToJsonString().ShouldBe(DecimalString);
        Export<Money>(new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { new DecimalNumberJsonConverter() } }).ToJsonString().ShouldBe("""{"type":"number","format":"decimal"}""");
        Export<Money?>(Strings)["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
    }

    [Fact]
    public void A_description_that_never_ends_is_refused_naming_its_path() =>
        Should.Throw<InvalidOperationException>(() => Export<Loop>(Strings)).Message.ShouldContain("Loop -> Loop");

    /// <summary>A converter someone else wrote is described in the options, and reads and writes as before.</summary>
    [Fact]
    public void A_converter_of_another_s_is_described_in_the_options()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters =
            {
                new UpperConverter(),
                new JsonSchemaDescriptions().Describe<string>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["pattern"] = "^[A-Z]*$" })),
            },
        };

        Export<string>(options).ToJsonString().ShouldBe("""{"type":"string","pattern":"^[A-Z]*$"}""");
        JsonSerializer.Serialize("abc", options).ShouldBe("\"ABC\"");
        JsonSerializer.Deserialize<string>("\"abc\"", options).ShouldBe("abc");
    }

    /// <summary>A rule describes every type it answers for, the serializer's own enum converter's among them.</summary>
    [Fact]
    public void A_rule_describes_every_type_it_answers_for()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters =
            {
                new JsonStringEnumConverter(),
                new JsonSchemaDescriptions().Describe(type => type.IsEnum
                    ? JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Enum.GetNames(type).Select(name => JsonValue.Create(name))]) })
                    : null),
            },
        };

        Export<DayOfWeek>(options)["type"]!.GetValue<string>().ShouldBe("string");
        Export<DayOfWeek?>(options)["enum"]!.AsArray().Count.ShouldBe(8, "seven days and null");
        JsonSerializer.Serialize(DayOfWeek.Friday, options).ShouldBe("\"Friday\"");
        JsonSchemaDescription.For(typeof(int), options).ShouldBeNull("the rule says nothing of a type it does not answer for");
    }

    /// <summary>The application's word comes before the converter's own.</summary>
    [Fact]
    public void A_description_in_the_options_comes_before_the_converter_s_own()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { new DecimalStringJsonConverter(), new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["minLength"] = 1 })) },
        };

        Export<decimal>(options).ToJsonString().ShouldBe("""{"type":"string","minLength":1}""");
    }

    /// <summary>The carrier converts nothing, so the serializer passes over it.</summary>
    [Fact]
    public void Descriptions_convert_nothing()
    {
        var descriptions = new JsonSchemaDescriptions().Describe(_ => JsonSchemaDescription.Of(new JsonObject { ["type"] = "string" }));

        descriptions.CanConvert(typeof(int)).ShouldBeFalse();
        JsonSerializer.Serialize(new Dictionary<string, int> { ["a"] = 1 }, new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { descriptions } }).ShouldBe("""{"a":1}""");
        Should.Throw<NotSupportedException>(() => descriptions.CreateConverter(typeof(int), JsonSerializerOptions.Default));
    }

    [Theory]
    [InlineData("""{"$ref":"#/$defs/x"}""", "$ref")]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"#"}}}""", "$ref")]
    [InlineData("""{"$defs":{"x":{}}}""", "$defs")]
    [InlineData("""{"anyOf":[{"$id":"x"}]}""", "$id")]
    public void A_description_that_refers_elsewhere_is_refused(string schema, string keyword) =>
        Should.Throw<ArgumentException>(() => JsonSchemaDescription.Of(JsonNode.Parse(schema)!.AsObject())).Message.ShouldContain($"'{keyword}'");

    [Fact]
    public void A_description_never_changes_once_made()
    {
        var schema = new JsonObject { ["type"] = "string" };
        var description = JsonSchemaDescription.Of(schema);

        schema["type"] = "number";
        description.Schema!["type"] = "boolean";

        description.Schema!["type"]!.GetValue<string>().ShouldBe("string");
    }

    private sealed class UpperConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUpperInvariant());
    }
}
