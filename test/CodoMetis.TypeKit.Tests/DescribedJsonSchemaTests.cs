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
    private const string DecimalNumber = """{"type":"number","format":"decimal"}""";

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

    /// <summary>A recursive object, written by a converter described as that object: the exporter refers back into it.</summary>
    public sealed class Node
    {
        public string? Name { get; set; }

        public Node? Child { get; set; }
    }

    [JsonConverter(typeof(TreeConverter))]
    public sealed class Tree
    {
        public Node? Root { get; set; }
    }

    public sealed class TreeConverter : JsonConverter<Tree>, IDescribedJsonConverter
    {
        public override Tree Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new() { Root = JsonSerializer.Deserialize<Node>(ref reader, options) };

        public override void Write(Utf8JsonWriter writer, Tree value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Root, options);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.SameAs(typeof(Node));
    }

    public sealed record Forest(Tree Item, Tree? Maybe, List<Tree> Trees, Dictionary<string, Tree?> Named);

    /// <summary>A property that names its converter, on a nullable decimal, which the serializer adapts.</summary>
    public sealed record Priced([property: JsonConverter(typeof(DecimalStringJsonConverter))] decimal? Amount, [property: JsonConverter(typeof(DecimalStringJsonConverter))] decimal Price);

    /// <summary>A reference type written as a string by a converter that says so.</summary>
    [JsonConverter(typeof(TextConverter))]
    public sealed class Text(string value)
    {
        public string Value { get; } = value;
    }

    public sealed class TextConverter : JsonConverter<Text>, IDescribedJsonConverter
    {
        public override Text Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, Text value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["minLength"] = 1 });
    }

    public sealed record Texts(Text Required, Text? Optional, List<Text?> Items, Dictionary<string, Text?> Named);

    private static JsonNode Export<T>(JsonSerializerOptions options) => options.GetJsonSchemaAsNode(typeof(T), DescribedJsonSchema.ExporterOptions());

    private static JsonNode Export<T>(JsonSerializerOptions options, JsonSchemaExporterOptions exporter) => options.GetJsonSchemaAsNode(typeof(T), DescribedJsonSchema.ExporterOptions(exporter));

    /// <summary>Every <c>$ref</c> in <paramref name="document"/>, each with the node its pointer reaches from the root, or null.</summary>
    private static IEnumerable<(string Pointer, JsonNode? Target)> References(JsonNode document)
    {
        IEnumerable<string> All(JsonNode? node) => node switch
        {
            JsonObject o => o.SelectMany(p => p.Key == "$ref" && p.Value is JsonValue v ? [v.GetValue<string>()] : All(p.Value)),
            JsonArray a => a.SelectMany(All),
            _ => [],
        };

        JsonNode? Resolve(string pointer) => pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Replace("~1", "/").Replace("~0", "~"))
            .Aggregate<string, JsonNode?>(document, (node, segment) => node switch
            {
                JsonObject o => o[segment],
                JsonArray a when int.TryParse(segment, out var index) && index < a.Count => a[index],
                _ => null,
            });

        return All(document).Select(pointer => (pointer, Resolve(pointer)));
    }

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

        Export<string>(options).ToJsonString().ShouldBe("""{"type":["string","null"],"pattern":"^[A-Z]*$"}""", "a string at the root admits null, as the exporter has a plain one");
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

    /// <summary>
    /// A converter described as a recursive object, anywhere but the root: the exporter writes the object's
    /// references from its own root, and they must point from the document's.
    /// </summary>
    [Fact]
    public void A_recursive_schema_described_inside_another_keeps_every_reference_resolving()
    {
        var document = Export<Forest>(Strings);

        var references = References(document).ToList();
        references.Count.ShouldBe(4, "one per place a tree is put: a property, a nullable property, an element, a value");
        foreach (var (pointer, target) in references)
        {
            target.ShouldNotBeNull($"'{pointer}' resolves to nothing in {document.ToJsonString()}");
            target!["properties"]!["child"].ShouldNotBeNull($"'{pointer}' points at a node, not where a node is");
        }

        Export<Tree>(Strings)["properties"]!["child"]!["properties"]!["child"]!["$ref"]!.GetValue<string>().ShouldBe("#/properties/child", "the root keeps its own");
    }

    /// <summary>A described object put where null is not allowed is not nullable for being a reference type's schema.</summary>
    [Fact]
    public void A_schema_described_as_a_reference_type_s_admits_null_only_where_the_position_does()
    {
        var properties = Export<Forest>(Strings)["properties"]!;

        properties["item"]!["type"]!.ToJsonString().ShouldBe("\"object\"");
        properties["maybe"]!["type"]!.ToJsonString().ShouldBe("""["object","null"]""");
    }

    /// <summary>The serializer adapts a nullable property's converter to the nullable type; the converter it names still says what it writes.</summary>
    [Fact]
    public void A_property_s_own_converter_on_a_nullable_value_type_is_described()
    {
        var priced = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var properties = Export<Priced>(priced)["properties"]!;

        properties["amount"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["amount"]!["pattern"].ShouldNotBeNull();
        properties["price"]!.ToJsonString().ShouldBe(DecimalString);
        JsonSerializer.Serialize(new Priced(7.80m, 1m), priced).ShouldBe("""{"amount":"7.8","price":"1"}""", "the wire the schema describes");

        var generated = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = PricedJson.Default };
        Export<Priced>(generated)["properties"]!["amount"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""", "under a source-generated context too");
        JsonSerializer.Serialize(new Priced(7.80m, 1m), generated).ShouldBe("""{"amount":"7.8","price":"1"}""");
    }

    /// <summary>A reference type admits null where a plain one would: a nullable property, and an element unless the exporter says otherwise.</summary>
    [Fact]
    public void A_described_reference_type_admits_null_where_a_plain_one_would()
    {
        var plain = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var properties = Export<Texts>(plain)["properties"]!;

        properties["required"]!.ToJsonString().ShouldBe("""{"type":"string","minLength":1}""");
        properties["optional"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["items"]!["items"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["named"]!["additionalProperties"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        JsonSerializer.Serialize(new Texts(new("a"), null, [null], new() { ["k"] = null }), plain).ShouldBe("""{"required":"a","optional":null,"items":[null],"named":{"k":null}}""");

        var oblivious = Export<Texts>(plain, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true })["properties"]!;
        oblivious["items"]!["items"]!["type"]!.ToJsonString().ShouldBe("\"string\"", "as the exporter has a plain string element then");
        oblivious["optional"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""", "an annotated property stays nullable");
    }

    /// <summary>A keyword's name is a keyword only where a schema is: a property may be called <c>$id</c>, and a value may hold one.</summary>
    [Theory]
    [InlineData("""{"type":"object","properties":{"$id":{"type":"string"},"$ref":{"type":"string"}},"required":["$id"]}""")]
    [InlineData("""{"const":{"$ref":"#/x"}}""")]
    [InlineData("""{"enum":[{"$id":"a"}],"examples":[{"$defs":1}],"default":{"$anchor":"a"}}""")]
    [InlineData("""{"type":"object","patternProperties":{"^\\$":{"type":"string"}},"propertyNames":{"enum":["$schema"]}}""")]
    public void A_keyword_s_name_as_data_is_allowed(string schema) =>
        JsonSchemaDescription.Of(JsonNode.Parse(schema)!.AsObject()).Schema!.ToJsonString().ShouldBe(JsonNode.Parse(schema)!.ToJsonString());

    [Theory]
    [InlineData("""{"$ref":"#/$defs/x"}""", "$ref")]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"#"}}}""", "$ref")]
    [InlineData("""{"$defs":{"x":{}}}""", "$defs")]
    [InlineData("""{"anyOf":[{"$id":"x"}]}""", "$id")]
    [InlineData("""{"type":"object","properties":{"$id":{"$ref":"#"}}}""", "$ref")]
    [InlineData("""{"prefixItems":[{"$anchor":"a"}]}""", "$anchor")]
    [InlineData("""{"items":{"items":{"$dynamicRef":"#a"}}}""", "$dynamicRef")]
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

    /// <summary>A factory in the options, asked for the converter it makes: described where that one says what it writes.</summary>
    public sealed class StringsFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(decimal);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new DecimalStringJsonConverter();
    }

    /// <summary>A factory whose converter says nothing, for a type whose own converter describes itself.</summary>
    public sealed class SilentMoneyFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Money);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new SilentMoney();
    }

    public sealed class SilentMoney : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(0);

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) => writer.WriteStringValue("money");
    }

    public sealed record Factored([property: JsonConverter(typeof(StringsFactory))] decimal Price, [property: JsonConverter(typeof(StringsFactory))] decimal? Maybe);

    /// <summary>A converter registered for the nullable type alone, which the serializer picks for the nullable positions.</summary>
    public sealed class NullableMoneyFlag : JsonConverter<Money?>, IDescribedJsonConverter
    {
        public override bool HandleNull => true;

        public override Money? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetBoolean() ? new(1) : null;

        public override void Write(Utf8JsonWriter writer, Money? value, JsonSerializerOptions options) => writer.WriteBooleanValue(value is not null);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options)
        {
            typeToConvert.ShouldBe(typeof(Money?), "asked for the type it converts");
            return JsonSchemaDescription.Of(new JsonObject { ["type"] = "boolean" });
        }
    }

    public sealed record Flags(Money? Maybe, Money Plain);

    public sealed record Holder(decimal? Maybe, decimal Plain);

    private static JsonSerializerOptions Options(params JsonConverter[] converters)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        foreach (var converter in converters) options.Converters.Add(converter);
        return options;
    }

    /// <summary>
    /// The serializer runs the converter a factory makes, so that converter is asked: a described one is
    /// described, and one that says nothing says nothing, whatever the type's own converter would have said.
    /// </summary>
    [Fact]
    public void A_factory_in_the_options_is_asked_for_the_converter_it_makes()
    {
        var strings = Options(new StringsFactory());
        Export<decimal>(strings).ToJsonString().ShouldBe(DecimalString);
        JsonSerializer.Serialize(7.80m, strings).ShouldBe("\"7.8\"");

        var silent = Options(new SilentMoneyFactory());
        Export<Money>(silent).ToJsonString().ShouldBe("true", "the converter the factory makes says nothing, and the type's own, which does not write it, is not asked");
        JsonSerializer.Serialize(new Money(1), silent).ShouldBe("\"money\"");
    }

    [Fact]
    public void A_factory_a_property_names_is_asked_for_the_converter_it_makes()
    {
        var options = Options();
        var properties = Export<Factored>(options)["properties"]!;

        properties["price"]!.ToJsonString().ShouldBe(DecimalString);
        properties["maybe"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        properties["maybe"]!["pattern"].ShouldNotBeNull();
        JsonSerializer.Serialize(new Factored(7.80m, 7.80m), options).ShouldBe("""{"price":"7.8","maybe":"7.8"}""");
    }

    /// <summary>The serializer picks a converter for the declared type first, a <c>Nullable&lt;T&gt;</c> included, and so does the description.</summary>
    [Fact]
    public void A_converter_registered_for_the_nullable_type_describes_the_nullable_positions()
    {
        var options = Options(new DecimalStringJsonConverter(), new NullableMoneyFlag());
        var properties = Export<Flags>(options)["properties"]!;

        properties["maybe"]!.ToJsonString().ShouldBe("""{"type":["boolean","null"]}""");
        properties["plain"]!.ToJsonString().ShouldBe(DecimalString, "the non-nullable positions are the type's own converter's");
        JsonSerializer.Serialize(new Flags(new(1), new(1)), options).ShouldBe("""{"maybe":true,"plain":"1"}""");
    }

    /// <summary>
    /// An <c>if</c> and its branches judge the whole value, null included, whatever the type says: a
    /// conditional description is kept whole in a branch beside null, as a <c>oneOf</c> is.
    /// </summary>
    [Fact]
    public void A_conditional_description_keeps_its_branches_beside_null()
    {
        var conditional = JsonNode.Parse("""{"type":"integer","if":{"type":"integer"},"then":{"minimum":0},"else":{"type":"number"}}""")!.AsObject();
        var options = Options(new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(conditional)));

        var expected = new JsonObject { ["anyOf"] = new JsonArray(conditional.DeepClone(), new JsonObject { ["type"] = "null" }) };
        Export<decimal?>(options).ToJsonString().ShouldBe(expected.ToJsonString());
        Export<Holder>(options)["properties"]!["maybe"]!.ToJsonString().ShouldBe(expected.ToJsonString());
        Export<Holder>(options)["properties"]!["plain"]!.ToJsonString().ShouldBe(conditional.ToJsonString(), "a position that refuses null is the description as it is");
    }

    /// <summary>A resolver that puts one converter in charge of one type, before the rest of the chain and the options' converters.</summary>
    public sealed class ResolverOf<T>(JsonConverter<T> converter) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(T) ? JsonMetadataServices.CreateValueInfo<T>(options, converter) : null;
    }

    /// <summary>A nullable amount written as its cents, by a converter of the nullable type that says so.</summary>
    public sealed class NullableMoneyCents : JsonConverter<Money?>, IDescribedJsonConverter
    {
        public override Money? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(decimal.Parse(reader.GetString()!["cents:".Length..], System.Globalization.CultureInfo.InvariantCulture) / 100);

        public override void Write(Utf8JsonWriter writer, Money? value, JsonSerializerOptions options) =>
            writer.WriteStringValue("cents:" + ((long)(value!.Value.Amount * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture));

        public JsonSchemaDescription DescribeJson(Type typeToConvert, JsonSerializerOptions options) => JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["pattern"] = "^cents:-?[0-9]*$" });
    }

    /// <summary>The same writer, saying nothing of what it writes.</summary>
    public sealed class SilentNullableMoney : JsonConverter<Money?>
    {
        public override Money? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(0);

        public override void Write(Utf8JsonWriter writer, Money? value, JsonSerializerOptions options) => writer.WriteStringValue("money");
    }

    public sealed record Wallet(Money? Maybe, Money Plain, List<Money?> Many, Dictionary<string, Money?> Named);

    private static JsonSerializerOptions Resolved(IJsonTypeInfoResolver first, params JsonConverter[] converters)
    {
        var options = Options(converters);
        options.TypeInfoResolverChain.Insert(0, first);
        return options;
    }

    /// <summary>
    /// A resolver's converter is what the serializer runs, whatever the options' converters would have been:
    /// the description is the contract's, so the exported schema accepts what is written. The options' string
    /// converter was described while the resolver's number converter wrote <c>1.2</c> (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_resolver_s_converter_is_described_over_a_conflicting_one_in_the_options()
    {
        var options = Resolved(new ResolverOf<decimal>(new DecimalNumberJsonConverter()), new DecimalStringJsonConverter());

        JsonSerializer.Serialize(new Holder(1.2m, 1.2m), options).ShouldBe("""{"maybe":1.2,"plain":1.2}""", "the wire: the resolver's numbers, in the nullable position too");
        Export<decimal>(options).ToJsonString().ShouldBe(DecimalNumber);

        var properties = Export<Holder>(options)["properties"]!;
        properties["plain"]!.ToJsonString().ShouldBe(DecimalNumber);
        properties["maybe"]!.ToJsonString().ShouldBe("""{"type":["number","null"],"format":"decimal"}""");
        Export<Money>(options).ToJsonString().ShouldBe(DecimalNumber, "a type described as a decimal is the resolver's decimal");
    }

    /// <summary>
    /// A resolver's converter of a nullable type writes the nullable positions, as one in the options does:
    /// it is asked, not the value's converter, in a property, an element and a value. The wrapped decimal's
    /// schema was described while the resolver's converter wrote <c>"cents:780"</c> (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_resolver_s_converter_of_the_nullable_type_describes_the_nullable_positions()
    {
        var options = Resolved(new ResolverOf<Money?>(new NullableMoneyCents()), new DecimalStringJsonConverter());
        const string cents = """{"type":["string","null"],"pattern":"^cents:-?[0-9]*$"}""";

        JsonSerializer.Serialize(new Wallet(new(7.80m), new(7.80m), [new(7.80m), null], new() { ["k"] = new(7.80m) }), options)
                      .ShouldBe("""{"maybe":"cents:780","plain":"7.8","many":["cents:780",null],"named":{"k":"cents:780"}}""", "the wire");

        var properties = Export<Wallet>(options)["properties"]!;
        properties["maybe"]!.ToJsonString().ShouldBe(cents);
        properties["plain"]!.ToJsonString().ShouldBe(DecimalString, "the non-nullable positions are the type's own converter's");
        properties["many"]!["items"]!.ToJsonString().ShouldBe(cents);
        properties["named"]!["additionalProperties"]!.ToJsonString().ShouldBe(cents);

        var silent = Resolved(new ResolverOf<Money?>(new SilentNullableMoney()), new DecimalStringJsonConverter());
        JsonSchemaDescription.For(typeof(Money?), silent).ShouldBeNull("a resolver's converter that says nothing says nothing, whatever the value's converter would say");
        JsonSchemaDescription.For(typeof(Money), silent).ShouldNotBeNull();
    }

    /// <summary>A resolver that builds the nullable contract by hand, over a converter of the value other than the one the value's contract has.</summary>
    public sealed class HandBuiltNullable(JsonConverter<decimal> inner) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(decimal?)
                ? JsonMetadataServices.CreateValueInfo<decimal?>(options, JsonMetadataServices.GetNullableConverter(JsonMetadataServices.CreateValueInfo<decimal>(options, inner)))
                : null;
    }

    /// <summary>
    /// A known limit: System.Text.Json's adapter of a nullable type writes what the converter it was built over
    /// writes, and no public API names that converter, so the adapter is described by the value's contract. A
    /// resolver that builds the nullable contract by hand over another converter has its nullable positions
    /// described as the value's: here a number written, a string described. Pinned so that a change is seen.
    /// </summary>
    [Fact]
    public void A_nullable_contract_built_by_hand_over_another_converter_is_described_by_the_value_s_contract()
    {
        var options = Resolved(new HandBuiltNullable(new DecimalNumberJsonConverter()), new DecimalStringJsonConverter());

        JsonSerializer.Serialize(new Holder(1.2m, 1.2m), options).ShouldBe("""{"maybe":1.2,"plain":"1.2"}""", "the wire: the hand-built adapter's number");
        JsonSchemaDescription.For(typeof(decimal?), options)!.Schema!.ToJsonString().ShouldBe(DecimalString, "described as the value's contract, the options' string");
    }

    public sealed record Attributed([property: JsonConverter(typeof(DecimalStringJsonConverter))] decimal? Value);

    /// <summary>A converter of the nullable type that writes whether there is a value, saying nothing of it.</summary>
    public sealed class Presence : JsonConverter<decimal?>
    {
        public override bool HandleNull => true;

        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetBoolean() ? 0m : null;

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options) => writer.WriteBooleanValue(value is not null);
    }

    /// <summary>
    /// A resolver's modifier that replaces a nullable property's converter puts its own in charge, whatever the
    /// property's attribute names: the attribute is asked only where the property's converter is the
    /// serializer's adapter of the converter it names. A replacement <c>JsonConverter&lt;decimal?&gt;</c> writing
    /// <c>true</c> was described as the attribute's decimal string (reported 2026-10-14).
    /// </summary>
    [Fact]
    public void A_modifier_s_converter_of_a_nullable_property_is_described_over_the_property_s_attribute()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(info =>
            {
                if (info.Type != typeof(Attributed)) return;
                foreach (var property in info.Properties) property.CustomConverter = new Presence();
            }),
        };

        JsonSerializer.Serialize(new Attributed(7.80m), options).ShouldBe("""{"value":true}""", "the wire: the modifier's converter");

        var property = options.GetTypeInfo(typeof(Attributed)).Properties.Single();
        JsonSchemaDescription.For(typeof(decimal?), options, property).ShouldBeNull("the converter in charge says nothing of what it writes");
        Export<Attributed>(options)["properties"]!["value"]!.ToJsonString().ShouldBe("true", "the empty schema, as for any converter that says nothing, not the attribute's decimal string");

        var attributed = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var adapted = attributed.GetTypeInfo(typeof(Attributed)).Properties.Single();
        JsonSchemaDescription.For(typeof(decimal?), attributed, adapted)!.Schema!["pattern"].ShouldNotBeNull("the control: the attribute's converter, adapted to the nullable type, is described");
    }

    private sealed class UpperConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUpperInvariant());
    }
}

[JsonSerializable(typeof(DescribedJsonSchemaTests.Priced))]
internal sealed partial class PricedJson : JsonSerializerContext;
