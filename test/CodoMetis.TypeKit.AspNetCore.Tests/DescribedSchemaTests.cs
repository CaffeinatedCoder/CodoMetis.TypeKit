using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A described converter in the document (<see cref="IDescribedJsonConverter"/>, <see cref="JsonSchemaDescriptions"/>):
/// what it writes, in every position, where ASP.NET writes a converter of one's own as <c>{"format":"double"}</c>
/// and drops a list's <c>items</c> and a dictionary's <c>additionalProperties</c> (measured, the control below).
/// </summary>
public sealed class DescribedSchemaTests
{
    private static readonly JsonObject DecimalString = new()
    {
        ["type"] = "string",
        ["format"] = "decimal",
        ["pattern"] = @"^-?(?:0|[1-9]\d*)(?:\.\d*[1-9])?$",
    };

    public sealed record Order(
        decimal Total,
        decimal? Discount,
        List<decimal> Lines,
        Dictionary<string, decimal> Taxes,
        decimal?[] Gaps,
        ProbeAmount Amount,
        ProbeAmount? MaybeAmount,
        List<ProbeAmount> Amounts,
        Patch<decimal> Rebate);

    /// <summary>A property that says its own converter, under a host that registers none.</summary>
    public sealed record Tagged([property: JsonConverter(typeof(DecimalStringJsonConverter))] decimal Price, decimal Plain);

    /// <summary>A value object as a property and as a merge patch of it.</summary>
    public sealed record Amend(ProbeAmount Amount, Patch<ProbeAmount> Change);

    /// <summary>A reference type written as a string by a converter that says so.</summary>
    [JsonConverter(typeof(LabelConverter))]
    public sealed class Label(string value)
    {
        public string Value { get; } = value;
    }

    public sealed class LabelConverter : JsonConverter<Label>, IDescribedJsonConverter
    {
        public override Label Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => new(reader.GetString()!);

        public override void Write(System.Text.Json.Utf8JsonWriter writer, Label value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(value.Value);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["minLength"] = 1 });
    }

    public sealed record Labels(Label? Optional, List<string?> Plain, List<Label?> Described, Dictionary<string, string?> PlainNamed, Dictionary<string, Label?> DescribedNamed);

    /// <summary>A value object written as a quoted cent count, by a converter that replaces the generated one and says so.</summary>
    public sealed class CentsConverter : JsonConverter<ProbeAmount>, IDescribedJsonConverter
    {
        public override ProbeAmount Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
            ProbeAmount.From(decimal.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture) / 100);

        public override void Write(System.Text.Json.Utf8JsonWriter writer, ProbeAmount value, System.Text.Json.JsonSerializerOptions options) =>
            writer.WriteStringValue(((long)(value.Value * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture));

        public JsonSchemaDescription DescribeJson(Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => JsonSchemaDescription.Of(Cents);
    }

    private static readonly JsonObject Cents = new() { ["type"] = "string", ["pattern"] = "^-?[0-9]*$" };

    public sealed record Priced([property: JsonConverter(typeof(CentsConverter))] ProbeAmount Price, ProbeAmount Plain);

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    private static void Endpoints(WebApplication app)
    {
        app.MapPost("/orders", int (Order order) => 0);
        app.MapPost("/tagged", int (Tagged tagged) => 0);
        app.MapGet("/amounts/{amount}", int (decimal amount) => 0);
        app.MapPost("/amend", int (Amend amend) => 0);
        app.MapPost("/labels", int (Labels labels) => 0);
        app.MapPatch("/conditioned", int (Conditioned conditioned) => 0);
    }

    /// <summary>A value object and a closed set the options write with converters of their own that say nothing.</summary>
    public sealed record Undescribed(ProbeAmount Amount, List<ProbeAmount> Amounts, Patch<ProbeAmount> Change, ProbeStatus Status, List<ProbeStatus> Statuses);

    public sealed class SilentAmount : JsonConverter<ProbeAmount>
    {
        public override ProbeAmount Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => ProbeAmount.From(reader.GetInt32());

        public override void Write(System.Text.Json.Utf8JsonWriter writer, ProbeAmount value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue("amount");
    }

    public sealed class SilentStatus : JsonConverter<ProbeStatus>
    {
        public override ProbeStatus Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => ProbeStatus.Open;

        public override void Write(System.Text.Json.Utf8JsonWriter writer, ProbeStatus value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(1);
    }

    /// <summary>A host of minimal APIs alone, so the options' converters need not be on MVC's too.</summary>
    private static async Task<OpenApiJson> MinimalDocumentAsync(Action<JsonOptions> json, Action<WebApplication> endpoints)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(json);
        builder.Services.AddOpenApi(openApi => openApi.AddTypeKit());

        await using var app = builder.Build();
        app.MapOpenApi();
        endpoints(app);
        await app.StartAsync(TestContext.Current.CancellationToken);

        return new(JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!);
    }

    private static void PricedEndpoints(WebApplication app) => app.MapPost("/priced", int (Priced priced) => 0);

    /// <summary>Whether a schema admits null, as 3.1 says it (a type, a branch) or 3.0 (<c>nullable</c>).</summary>
    private static bool AdmitsNull(JsonNode schema) =>
        schema["type"] is JsonArray types && types.Any(t => t!.GetValue<string>() == "null")
     || schema["nullable"] is JsonValue nullable && nullable.GetValue<bool>()
     || (schema["anyOf"] ?? schema["oneOf"]) is JsonArray branches && branches.Any(branch => branch!["type"]?.ToString() == "null");

    private static Task<OpenApiJson> DocumentAsync(OpenApiSpecVersion version, Action<JsonOptions>? json) =>
        ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), json, version, Endpoints);

    private static JsonNode Inline(OpenApiJson document, JsonNode schema) => document.Resolve(schema);

    /// <summary>What a 3.0 document says of a nullable string: the type, and <c>nullable</c>; 3.1 says both types.</summary>
    private static void ShouldBeDecimalStringOrNull(JsonNode schema, OpenApiSpecVersion version, string where)
    {
        var copy = JsonNode.Parse(schema.ToJsonString())!.AsObject();
        if (version == OpenApiSpecVersion.OpenApi3_1)
        {
            copy["type"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(["null", "string"], ignoreOrder: true, where);
            copy["type"] = "string";
        }
        else
        {
            copy["nullable"]!.GetValue<bool>().ShouldBeTrue(where);
            copy.Remove("nullable");
        }

        copy.ShouldDescribeTheSameAs(DecimalString, where);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_described_decimal_is_what_its_converter_writes_in_every_position(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version, json => json.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()));

        document.Property(nameof(Order), "total").ShouldDescribeTheSameAs(DecimalString, "a property");
        ShouldBeDecimalStringOrNull(document.Property(nameof(Order), "discount"), version, "a nullable property");
        document.Property(nameof(Order), "lines")["items"]!.ShouldDescribeTheSameAs(DecimalString, "a list's element");
        document.Property(nameof(Order), "taxes")["additionalProperties"]!.ShouldDescribeTheSameAs(DecimalString, "a dictionary's value");
        ShouldBeDecimalStringOrNull(document.Property(nameof(Order), "gaps")["items"]!, version, "a nullable element");

        Inline(document, document.Property(nameof(Order), "amount")).ShouldDescribeTheSameAs(DecimalString, "a value object over a decimal");
        Inline(document, document.Property(nameof(Order), "amounts")["items"]!).ShouldDescribeTheSameAs(DecimalString, "a list of them");
        document.Property(nameof(Order), "rebate")["type"].ShouldNotBeNull("a patch over a decimal, inlined and nullable").ToJsonString().ShouldContain("string");
    }

    /// <summary>The control: what ASP.NET writes for the same host and converter without the description.</summary>
    [Fact]
    public async Task Without_AddTypeKit_a_decimal_converter_empties_the_document()
    {
        var document = await ProbeHost.DocumentAsync(openApi: null, json => json.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()), endpoints: Endpoints);

        document.Property(nameof(Order), "total").ToJsonString().ShouldBe("""{"format":"double"}""");
        document.Property(nameof(Order), "lines")["items"].ShouldBeNull();
        document.Property(nameof(Order), "taxes")["additionalProperties"].ShouldBeNull();
        document.Parameter("/amounts/{amount}", "get", "amount").ToJsonString().ShouldBe("""{"format":"double"}""");
    }

    /// <summary>A parameter binds through TryParse, not JSON: it keeps what ASP.NET writes without the converter.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_parameter_keeps_ASP_NET_s_own_description(OpenApiSpecVersion version)
    {
        var described = await DocumentAsync(version, json => json.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
        var plain = await DocumentAsync(version, json: null);

        described.Parameter("/amounts/{amount}", "get", "amount").ShouldDescribeTheSameAs(plain.Parameter("/amounts/{amount}", "get", "amount"));
        described.Parameter("/probes/kinds", "get", "amount").ShouldDescribeTheSameAs(plain.Parameter("/probes/kinds", "get", "amount"), "a value object over a decimal");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_property_s_own_described_converter_is_described_and_its_neighbour_left_alone(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version, json: null);
        var plain = await ProbeHost.DocumentAsync(openApi: null, version: version, endpoints: Endpoints);

        document.Property(nameof(Tagged), "price").ShouldDescribeTheSameAs(DecimalString);
        document.Property(nameof(Tagged), "plain").ShouldDescribeTheSameAs(plain.Property(nameof(Tagged), "plain"), "a plain decimal stays ASP.NET's");
    }

    [Fact]
    public async Task The_number_spelling_is_a_number()
    {
        var document = await DocumentAsync(OpenApiSpecVersion.OpenApi3_1, json => json.SerializerOptions.Converters.Add(new DecimalNumberJsonConverter()));

        document.Property(nameof(Order), "total").ShouldDescribeTheSameAs(new JsonObject { ["type"] = "number", ["format"] = "decimal" });
    }

    /// <summary>
    /// The host's descriptions in its JSON options reach the document: a converter it does not own, here the
    /// serializer's own enum converter by a rule, and a value object and a closed set, before their own.
    /// </summary>
    [Fact]
    public async Task The_host_s_descriptions_come_first_for_any_type()
    {
        var document = await DocumentAsync(OpenApiSpecVersion.OpenApi3_1, json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions()
            .Describe<ProbeCount>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "integer", ["minimum"] = 0 }))
            .Describe<ProbeStatus>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["description"] = "The order's status." }))
            .Describe(type => type == typeof(DayOfWeek) ? JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["maxLength"] = 9 }) : null)));

        Inline(document, document.Property(nameof(ProbeDocument), "count")).ShouldDescribeTheSameAs(new JsonObject { ["type"] = "integer", ["minimum"] = 0 }, "a value object, before its wrapped type");
        Inline(document, document.Property(nameof(ProbeDocument), "counts")["additionalProperties"]!)["minimum"]!.GetValue<int>().ShouldBe(0, "a dictionary's value too, which the wrapped type's schema once took first");
        Inline(document, document.Property(nameof(ProbeDocument), "weekday"))["maxLength"]!.GetValue<int>().ShouldBe(9, "a value object over an enum, described by the rule through its wrapped type");
    }

    /// <summary>A value object the host describes is described so as a merge patch of it too, not as the type it wraps.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_value_object_s_description_reaches_a_patch_of_it(OpenApiSpecVersion version)
    {
        var range = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 10 };
        var document = await DocumentAsync(version, json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<ProbeAmount>(JsonSchemaDescription.Of(range))));

        var amount = Inline(document, document.Property(nameof(Amend), "amount"));
        amount["maximum"]!.GetValue<int>().ShouldBe(10, "the property, the control");
        var change = document.Property(nameof(Amend), "change");
        change["minimum"]!.GetValue<int>().ShouldBe(0, "the patch");
        change["maximum"]!.GetValue<int>().ShouldBe(10, "the patch");
        AdmitsNull(change).ShouldBeTrue("a patch admits null whatever its argument");
    }

    /// <summary>
    /// Under a source-generated context, a value object inside a patch alone has no metadata; its description
    /// needs none.
    /// </summary>
    [Fact]
    public async Task A_value_object_s_description_reaches_a_patch_of_it_under_a_source_generated_context()
    {
        var range = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 10 };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.TypeInfoResolver = AmendJson.Default;
            json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<ProbeAmount>(JsonSchemaDescription.Of(range)));
        });
        builder.Services.AddOpenApi(openApi => openApi.AddTypeKit());

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/amend", int (AmendChange amend) => 0);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var document = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var change = document["components"]!["schemas"]![nameof(AmendChange)]!["properties"]!["change"]!;
        change["maximum"]!.GetValue<int>().ShouldBe(10, change.ToJsonString());
    }

    /// <summary>
    /// A converter that replaces a value object's generated one writes it, on the options or on a property, so
    /// the document says what that converter says, not the wrapped type's schema.
    /// </summary>
    [Fact]
    public async Task A_described_converter_that_replaces_a_value_object_s_generated_one_is_described()
    {
        var registered = await DocumentAsync(OpenApiSpecVersion.OpenApi3_1, json => json.SerializerOptions.Converters.Add(new CentsConverter()));
        Inline(registered, registered.Property(nameof(Amend), "amount")).ShouldDescribeTheSameAs(Cents, "on the options");
        registered.Property(nameof(Amend), "change")["pattern"]!.GetValue<string>().ShouldBe("^-?[0-9]*$", "and in a patch of it");

    }

    /// <summary>
    /// A converter the options put in charge of a value object or a closed set, which says nothing of what it
    /// writes: the document no longer claims the wrapped type or the names, which that converter does not
    /// write, and leaves the type as ASP.NET writes any converter of one's own.
    /// </summary>
    [Fact]
    public async Task An_undescribed_converter_that_replaces_the_generated_one_is_left_as_ASP_NET_writes_it()
    {
        var document = await MinimalDocumentAsync(
            json => { json.SerializerOptions.Converters.Add(new SilentAmount()); json.SerializerOptions.Converters.Add(new SilentStatus()); },
            app => app.MapPost("/undescribed", int (Undescribed undescribed) => 0));

        var amount = Inline(document, document.Property(nameof(Undescribed), "amount"));
        amount["type"].ShouldBeNull($"not the wrapped decimal: {amount.ToJsonString()}");
        amount["format"].ShouldBeNull(amount.ToJsonString());
        document.Property(nameof(Undescribed), "amounts")["items"].ShouldBeNull("ASP.NET drops the element of a converter of one's own, and it is not put back as the wrapped type");
        document.Property(nameof(Undescribed), "change")["format"].ShouldBeNull("nor in a patch of it");
        var status = Inline(document, document.Property(nameof(Undescribed), "status"));
        status["enum"].ShouldBeNull($"not the set's names: {status.ToJsonString()}");
        document.Property(nameof(Undescribed), "statuses")["items"].ShouldBeNull();
    }

    /// <summary>
    /// A converter one property names, for a value object the document writes once as a component: the
    /// component cannot say it for one position alone, so the document is refused rather than wrong for one.
    /// </summary>
    [Fact]
    public async Task A_described_converter_one_property_names_for_a_shared_component_stops_the_document()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), endpoints: PricedEndpoints));

        refusal.Message.ShouldContain($"{typeof(Priced)}.Price");
        refusal.Message.ShouldContain("component 'ProbeAmount'");
    }

    /// <summary>A described reference type admits null where ASP.NET has a plain one admit it: a nullable property, and an element as a plain one's.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_described_reference_type_admits_null_where_a_plain_one_does(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version, json: null);

        // 3.0 has no way to say a reference to a component is nullable, and ASP.NET writes the bare reference.
        if (version == OpenApiSpecVersion.OpenApi3_1) AdmitsNull(document.Property(nameof(Labels), "optional")).ShouldBeTrue("a nullable property");
        AdmitsNull(document.Property(nameof(Labels), "described")["items"]!).ShouldBe(AdmitsNull(document.Property(nameof(Labels), "plain")["items"]!), "an element, as ASP.NET has a string?'s");
        AdmitsNull(document.Property(nameof(Labels), "describedNamed")["additionalProperties"]!).ShouldBe(AdmitsNull(document.Property(nameof(Labels), "plainNamed")["additionalProperties"]!), "a value, as ASP.NET has a string?'s");
    }

    /// <summary>
    /// A keyword the OpenAPI model does not hold is written aside, where validators do not see it: a tuple's
    /// <c>prefixItems</c> beside <c>items: false</c> would then refuse every non-empty array. Refused instead.
    /// </summary>
    [Fact]
    public async Task A_description_the_document_would_change_stops_it_naming_the_keyword()
    {
        var tuple = JsonNode.Parse("""{"type":"array","prefixItems":[{"type":"string"},{"type":"integer"}],"items":false}""")!.AsObject();

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => DocumentAsync(OpenApiSpecVersion.OpenApi3_1,
            json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(tuple)))));

        refusal.Message.ShouldContain("System.Decimal");
        refusal.Message.ShouldContain("prefixItems");

        // A boolean schema where the model holds only an object: items: false is dropped, and the array then admits anything.
        Should.Throw<InvalidOperationException>(() => DescribedSchemaTransformer.Parse(JsonNode.Parse("""{"type":"array","items":false}""")!.AsObject(), typeof(decimal)))
              .Message.ShouldContain("'/items'");
    }

    /// <summary>What the document writes as described, or leaves out as its default, is no change (measured on common keywords).</summary>
    [Theory]
    [InlineData("""{"type":"string","const":"a","$comment":"c","default":"a","title":"t","description":"d"}""")]
    [InlineData("""{"type":"string","deprecated":true,"readOnly":true,"writeOnly":false,"uniqueItems":false}""")]
    [InlineData("""{"type":"number","multipleOf":0.5,"exclusiveMinimum":0,"exclusiveMaximum":10.0,"examples":[1,2]}""")]
    [InlineData("""{"type":"array","minItems":1,"uniqueItems":true,"contains":{"type":"string"},"minContains":1}""")]
    [InlineData("""{"type":"object","minProperties":1,"propertyNames":{"pattern":"^a"},"dependentRequired":{"a":["b"]},"unevaluatedProperties":false}""")]
    [InlineData("""{"if":{"type":"string"},"then":{"minLength":1},"else":{"not":{"type":"null"}}}""")]
    [InlineData("""{"type":["string","null"],"enum":["a",null],"format":"date-time","x-custom":1}""")]
    public void A_description_the_document_holds_as_described_is_kept(string schema) =>
        Should.NotThrow(() => DescribedSchemaTransformer.Parse(JsonNode.Parse(schema)!.AsObject(), typeof(decimal)));

    [Fact]
    public async Task A_description_an_OpenAPI_document_cannot_hold_stops_it_naming_the_type()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => DocumentAsync(OpenApiSpecVersion.OpenApi3_1,
            json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(new JsonObject { ["type"] = 42 })))));

        refusal.Message.ShouldContain("System.Decimal");
    }

    /// <summary>A factory in the options for a value object, asked for the converter it makes: one that says what it writes, and one that says nothing.</summary>
    public sealed class CentsFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ProbeAmount);

        public override System.Text.Json.Serialization.JsonConverter CreateConverter(Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => new CentsConverter();
    }

    public sealed class SilentFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ProbeAmount);

        public override System.Text.Json.Serialization.JsonConverter CreateConverter(Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => new SilentAmount();
    }

    public sealed class StringsFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(decimal);

        public override System.Text.Json.Serialization.JsonConverter CreateConverter(Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => new DecimalStringJsonConverter();
    }

    /// <summary>A converter registered for the nullable value object alone, which writes those positions as a flag.</summary>
    public sealed class NullableAmountFlag : JsonConverter<ProbeAmount?>, IDescribedJsonConverter
    {
        public override bool HandleNull => true;

        public override ProbeAmount? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => reader.GetBoolean() ? ProbeAmount.From(1) : null;

        public override void Write(System.Text.Json.Utf8JsonWriter writer, ProbeAmount? value, System.Text.Json.JsonSerializerOptions options) => writer.WriteBooleanValue(value is not null);

        public JsonSchemaDescription DescribeJson(Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => JsonSchemaDescription.Of(new JsonObject { ["type"] = "boolean" });
    }

    /// <summary>A value object in every JSON position: a property, a nullable one, an element, a value, a patch.</summary>
    public sealed record Placed(ProbeAmount Amount, ProbeAmount? Maybe, List<ProbeAmount> Many, Dictionary<string, ProbeAmount> Named, Patch<ProbeAmount> Change);

    public sealed record Factored([property: JsonConverter(typeof(StringsFactory))] decimal Price, [property: JsonConverter(typeof(CentsFactory))] ProbeAmount Amount);

    public sealed record Flagged(ProbeAmount? Maybe, ProbeAmount Plain);

    public sealed record Conditioned(decimal? Maybe, Patch<decimal> Change);

    private static readonly JsonObject Conditional = JsonNode.Parse("""{"type":"integer","if":{"type":"integer"},"then":{"minimum":0},"else":{"type":"number"}}""")!.AsObject();

    /// <summary>Each way the options can have a value object written, and a value that writer does not write.</summary>
    private static readonly Dictionary<string, (Func<System.Text.Json.Serialization.JsonConverter[]> Converters, string Foreign)> Writers = new()
    {
        ["the generated converter"] = (() => [], "true"),
        ["a described converter"] = (() => [new CentsConverter()], "7.8"),
        ["a described factory"] = (() => [new CentsFactory()], "7.8"),
        ["a decimal converter"] = (() => [new DecimalStringJsonConverter()], "7.8"),
        ["a description in the options"] = (() => [new JsonSchemaDescriptions().Describe<ProbeAmount>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "number", ["minimum"] = 0 }))], "\"100\""),
        ["a stored-JSON factory"] = (() => [new StoredJsonConverterFactory()], "true"),
    };

    public static TheoryData<string, OpenApiSpecVersion> WritersAndVersions => new(Writers.Keys.SelectMany(writer => new[] { (writer, OpenApiSpecVersion.OpenApi3_1), (writer, OpenApiSpecVersion.OpenApi3_0) }));

    /// <summary>
    /// The matrix: whatever writes a value object, in whichever position and document version, each
    /// property's schema accepts the JSON the host's options write for it, and refuses a value the writer
    /// does not write, judged by a JSON Schema validator as a client would.
    /// </summary>
    [Theory]
    [MemberData(nameof(WritersAndVersions))]
    public async Task Every_position_accepts_what_its_writer_writes_and_refuses_what_it_does_not(string writer, OpenApiSpecVersion version)
    {
        var (converters, foreign) = Writers[writer];
        await using var host = await ProbeHost.StartAsync(openApi => openApi.AddTypeKit(), json => { foreach (var converter in converters()) json.SerializerOptions.Converters.Add(converter); }, version,
            app => app.MapPost("/placed", int (Placed placed) => 0));
        var document = await host.DocumentAsync();
        var options = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        var placed = new Placed(ProbeAmount.From(1), ProbeAmount.From(2), [ProbeAmount.From(3)], new() { ["k"] = ProbeAmount.From(4) }, Patch.Value(ProbeAmount.From(5)));
        var cleared = new Placed(ProbeAmount.From(1), null, [], [], Patch.Null<ProbeAmount>());
        foreach (var instance in new[] { placed, cleared })
        {
            foreach (var (property, value) in System.Text.Json.JsonSerializer.SerializeToNode(instance, options)!.AsObject())
            {
                var schema = document.Property(nameof(Placed), property);
                PatchSchemaTests.Validator(document, schema, version)(value?.ToJsonString() ?? "null")
                    .ShouldBeTrue($"{writer}, {version}: '{property}' is written as {value?.ToJsonString() ?? "null"}, which {schema.ToJsonString()} refuses");
            }
        }

        PatchSchemaTests.Validator(document, document.Property(nameof(Placed), "amount"), version)(foreign)
            .ShouldBeFalse($"{writer}, {version}: the schema accepts {foreign}, which the writer does not write, so the check above means nothing");
    }

    /// <summary>A factory in the options writes with the converter it makes, which is asked: for what it says, and for its silence.</summary>
    [Fact]
    public async Task A_factory_in_the_options_is_asked_for_the_converter_it_makes()
    {
        var described = await MinimalDocumentAsync(json => json.SerializerOptions.Converters.Add(new CentsFactory()), app => app.MapPost("/placed", int (Placed placed) => 0));
        Inline(described, described.Property(nameof(Placed), "amount")).ShouldDescribeTheSameAs(Cents, "a property");
        described.Property(nameof(Placed), "change")["pattern"]!.GetValue<string>().ShouldBe("^-?[0-9]*$", "a patch");

        var silent = await MinimalDocumentAsync(json => json.SerializerOptions.Converters.Add(new SilentFactory()), app => app.MapPost("/placed", int (Placed placed) => 0));
        var amount = Inline(silent, silent.Property(nameof(Placed), "amount"));
        amount["type"].ShouldBeNull($"not the wrapped decimal, which the converter does not write: {amount.ToJsonString()}");
        silent.Property(nameof(Placed), "many")["items"].ShouldBeNull("an element of one is left as ASP.NET writes it");
        silent.Property(nameof(Placed), "change")["format"].ShouldBeNull("a patch of one too");
    }

    /// <summary>A factory a property names is asked for the converter it makes; for a value object, that is the component refusal, as for any converter named there.</summary>
    [Fact]
    public async Task A_factory_a_property_names_is_asked_for_the_converter_it_makes()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => MinimalDocumentAsync(json => { }, app => app.MapPost("/factored", int (Factored factored) => 0)));
        refusal.Message.ShouldContain($"{typeof(Factored)}.Amount");

        var document = await MinimalDocumentAsync(json => { }, app => app.MapPost("/tagged", int (TaggedByFactory tagged) => 0));
        document.Property(nameof(TaggedByFactory), "price").ShouldDescribeTheSameAs(DecimalString);
    }

    public sealed record TaggedByFactory([property: JsonConverter(typeof(StringsFactory))] decimal Price);

    /// <summary>
    /// A converter registered for the nullable value object alone writes its nullable positions, which the
    /// document writes as references to the one component the non-nullable positions are described by: it
    /// cannot say both, so it is refused rather than wrong for one.
    /// </summary>
    [Fact]
    public async Task A_converter_registered_for_the_nullable_type_alone_stops_the_document()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => MinimalDocumentAsync(json => json.SerializerOptions.Converters.Add(new NullableAmountFlag()), app => app.MapPost("/flagged", int (Flagged flagged) => 0)));

        refusal.Message.ShouldContain($"'{typeof(ProbeAmount?)}'");
        refusal.Message.ShouldContain("component 'ProbeAmount'");
    }

    /// <summary>
    /// An <c>if</c> and its branches judge the whole value, so a conditional description admits null only in
    /// a branch beside it: in a nullable property and in a patch, judged by a validator.
    /// </summary>
    [Fact]
    public async Task A_conditional_description_admits_null_beside_its_branches()
    {
        var document = await DocumentAsync(OpenApiSpecVersion.OpenApi3_1, json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(Conditional))));

        foreach (var property in new[] { "maybe", "change" })
        {
            var valid = PatchSchemaTests.Validator(document, document.Property(nameof(Conditioned), property), OpenApiSpecVersion.OpenApi3_1);
            valid("null").ShouldBeTrue($"'{property}' admits null: {document.Property(nameof(Conditioned), property).ToJsonString()}");
            valid("1").ShouldBeTrue($"'{property}' admits what the condition admits");
            valid("-1").ShouldBeFalse($"'{property}' refuses what the condition refuses");
            valid("1.5").ShouldBeFalse($"'{property}' refuses what the type refuses");
        }
    }

    /// <summary>
    /// A 3.0 document writes a keyword it does not model as an <c>x-jsonschema-</c> extension, or leaves it
    /// out, where no validator reads it (measured, Microsoft.OpenApi 2.12): a description that relies on one
    /// is refused for that version, naming the keyword, and kept for 3.1.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"integer","if":{"type":"integer"},"then":{"minimum":0},"else":{"type":"number"}}""", "/if")]
    [InlineData("""{"type":"array","contains":{"type":"string"}}""", "/contains")]
    [InlineData("""{"type":"object","propertyNames":{"pattern":"^a"}}""", "/propertyNames")]
    [InlineData("""{"type":"object","patternProperties":{"^a":{"type":"string"}}}""", "/patternProperties")]
    [InlineData("""{"type":"object","dependentRequired":{"a":["b"]}}""", "/dependentRequired")]
    [InlineData("""{"type":"object","unevaluatedProperties":false}""", "/unevaluatedProperties")]
    [InlineData("""{"type":"string","$comment":"c"}""", "/$comment")]
    [InlineData("""{"type":"number","examples":[1,2]}""", "/examples")]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","contentEncoding":"base64"}}}""", "/properties/a/contentEncoding")]
    [InlineData("""{"anyOf":[{"type":"string"},{"type":"array","contains":{"type":"string"},"minContains":1}]}""", "/anyOf/1/contains")]
    public void A_description_a_3_0_document_writes_aside_or_leaves_out_stops_it_naming_the_keyword(string schema, string keyword)
    {
        var described = JsonNode.Parse(schema)!.AsObject();

        Should.NotThrow(() => DescribedSchemaTransformer.Parse(described, typeof(decimal), OpenApiSpecVersion.OpenApi3_1));
        var refusal = Should.Throw<InvalidOperationException>(() => DescribedSchemaTransformer.Parse(described, typeof(decimal), OpenApiSpecVersion.OpenApi3_0));
        refusal.Message.ShouldContain("OpenAPI 3.0 document");
        refusal.Message.ShouldContain($"'{keyword}'");
    }

    /// <summary>What 3.0 spells its own way is kept: null as <c>nullable</c>, several types as branches, a <c>const</c> as an <c>enum</c>, one example as <c>example</c>.</summary>
    [Theory]
    [InlineData("""{"type":["string","null"],"enum":["a",null],"format":"date-time","x-custom":1}""")]
    [InlineData("""{"type":"null"}""")]
    [InlineData("""{"type":["string","integer"],"pattern":"^a"}""")]
    [InlineData("""{"type":"string","const":"a","title":"t","description":"d","default":"a","deprecated":true,"readOnly":true,"writeOnly":false}""")]
    [InlineData("""{"type":"number","multipleOf":0.5,"exclusiveMinimum":0,"exclusiveMaximum":10,"examples":[1]}""")]
    [InlineData("""{"anyOf":[{"type":"string"},{"type":"null"}],"not":{"const":"x"}}""")]
    [InlineData("""{"type":"object","properties":{"a":{"type":["string","null"]}},"required":["a"],"additionalProperties":false,"minProperties":1}""")]
    [InlineData("""{"type":"array","items":{"type":"string"},"minItems":1,"uniqueItems":true}""")]
    public void A_description_a_3_0_document_holds_in_its_own_spelling_is_kept(string schema) =>
        Should.NotThrow(() => DescribedSchemaTransformer.Parse(JsonNode.Parse(schema)!.AsObject(), typeof(decimal), OpenApiSpecVersion.OpenApi3_0));

    /// <summary>The version checked is the document's: the same host, the same description, refused in 3.0 and served in 3.1.</summary>
    [Fact]
    public async Task The_version_checked_is_the_document_s()
    {
        Action<JsonOptions> json = json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(Conditional)));

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => DocumentAsync(OpenApiSpecVersion.OpenApi3_0, json));
        refusal.Message.ShouldContain("System.Decimal");
        refusal.Message.ShouldContain("'/if'");
        refusal.Message.ShouldContain("3.0");

        await Should.NotThrowAsync(() => DocumentAsync(OpenApiSpecVersion.OpenApi3_1, json));
    }
}

/// <summary>
/// ASP.NET builds the document from the minimal-API JSON options and MVC writes with its own: where a host
/// has a controller, a described converter on one and not the other is refused when the document is built,
/// rather than a document of numbers over a controller that writes strings (measured before the check).
/// </summary>
public sealed class DescribedOptionsAgreementTests
{
    public sealed record Quote(decimal Amount);

    [Microsoft.AspNetCore.Mvc.ApiController]
    public sealed class QuoteController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet("/mvc/quote")]
        public Quote Get() => new(7.80m);
    }

    private sealed class OnlyQuotes : Microsoft.AspNetCore.Mvc.ApplicationParts.IApplicationFeatureProvider<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> parts, Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature feature) =>
            feature.Controllers.Add(typeof(QuoteController).GetTypeInfo());
    }

    /// <summary>A decimal the host holds inside a patch alone, where ASP.NET asks no transformer of the decimal itself.</summary>
    public sealed record PatchedQuote(Patch<decimal> Amount);

    [Microsoft.AspNetCore.Mvc.ApiController]
    public sealed class PatchedQuoteController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet("/mvc/patched")]
        public PatchedQuote Get() => new(Patch.Value(7.80m));
    }

    private sealed class OnlyPatchedQuotes : Microsoft.AspNetCore.Mvc.ApplicationParts.IApplicationFeatureProvider<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> parts, Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature feature) =>
            feature.Controllers.Add(typeof(PatchedQuoteController).GetTypeInfo());
    }

    /// <summary>
    /// A type the document holds only as a patch's argument is compared too: a described converter on each set
    /// of options, saying different things, wrote <c>{"amount":7.8}</c> under a document of strings.
    /// </summary>
    [Fact]
    public async Task A_type_held_by_a_patch_alone_is_compared()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => HostAsync(
            http: converters => converters.Add(new DecimalStringJsonConverter()),
            mvc: converters => converters.Add(new DecimalNumberJsonConverter()),
            patchAlone: true));
        refusal.Message.ShouldContain("'System.Decimal'");
        refusal.Message.ShouldContain("""MVC's as {"type":"number","format":"decimal"}""");

        var (wire, document) = await HostAsync(Strings, Strings, patchAlone: true);
        wire.ShouldBe("""{"amount":"7.8"}""");
        JsonNode.Parse(document)!["components"]!["schemas"]!["PatchedQuote"]!["properties"]!["amount"]!["type"]!.ToJsonString().ShouldContain("string");
    }

    private static async Task<(string Wire, string Document)> HostAsync(Action<IList<JsonConverter>>? http, Action<IList<JsonConverter>>? mvc, bool controllers = true, bool patchAlone = false)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (http is not null) builder.Services.ConfigureHttpJsonOptions(json => http(json.SerializerOptions.Converters));
        var controllersBuilder = builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            if (patchAlone) parts.FeatureProviders.Add(new OnlyPatchedQuotes());
            else if (controllers) parts.FeatureProviders.Add(new OnlyQuotes());
        });
        if (mvc is not null) controllersBuilder.AddJsonOptions(json => mvc(json.JsonSerializerOptions.Converters));
        builder.Services.AddOpenApi(openApi => openApi.AddTypeKit());

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapControllers();
        if (!patchAlone)
        {
            app.MapGet("/minimal/quote", () => new Quote(7.80m));
            app.MapGet("/minimal/amount", () => new AmountQuote(ProbeAmount.From(7.80m)));
        }

        await app.StartAsync(cancellation);

        var client = app.GetTestClient();
        var wire = await client.GetStringAsync(patchAlone ? "/mvc/patched" : controllers ? "/mvc/quote" : "/minimal/quote", cancellation);
        return (wire, await client.GetStringAsync("/openapi/v1.json", cancellation));
    }

    private static void Strings(IList<JsonConverter> converters) => converters.Add(new DecimalStringJsonConverter());

    private static JsonSchemaDescriptions Minimum(int minimum) => new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "number", ["minimum"] = minimum }));

    private static string AmountType(string document) => JsonNode.Parse(document)!["components"]!["schemas"]!["Quote"]!["properties"]!["amount"]!["type"]!.GetValue<string>();

    [Theory]
    [InlineData(false, true, """the minimal-API options as nothing (no converter in charge describes it), MVC's as {"type":"string""")]
    [InlineData(true, false, """the minimal-API options as {"type":"string""")]
    public async Task A_described_converter_on_one_set_of_options_alone_stops_the_document(bool onHttp, bool onMvc, string said)
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => HostAsync(onHttp ? Strings : null, onMvc ? Strings : null));

        refusal.Message.ShouldStartWith("The OpenAPI document is built from the minimal-API JSON options");
        refusal.Message.ShouldContain("'System.Decimal'");
        refusal.Message.ShouldContain(said);
    }

    /// <summary>
    /// The same converters in another order: the serializer picks the first that converts, so MVC writes
    /// numbers under a document of strings (the review's case). Compared by what each says, not by name.
    /// </summary>
    [Fact]
    public async Task The_same_converters_in_another_order_stop_the_document()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => HostAsync(
            http: converters => { converters.Add(new DecimalStringJsonConverter()); converters.Add(new DecimalNumberJsonConverter()); },
            mvc: converters => { converters.Add(new DecimalNumberJsonConverter()); converters.Add(new DecimalStringJsonConverter()); }));

        refusal.Message.ShouldContain("""MVC's as {"type":"number","format":"decimal"}""");
    }

    /// <summary>Two description sets that say different things of a type are told apart; two that say the same are not.</summary>
    [Fact]
    public async Task Description_sets_are_compared_by_what_they_say()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => HostAsync(http: c => c.Add(Minimum(0)), mvc: c => c.Add(Minimum(1))));
        refusal.Message.ShouldContain("\"minimum\":1");

        var (_, document) = await HostAsync(http: c => c.Add(Minimum(0)), mvc: c => c.Add(Minimum(0)));
        AmountType(document).ShouldBe("number", "two sets, each its own instance, that say the same");
    }

    /// <summary>
    /// A converter that says nothing, for a value object, on one set of options alone: the other writes the
    /// generated JSON, the wrapped type, so the two describe it differently.
    /// </summary>
    [Fact]
    public async Task An_undescribed_converter_for_a_value_object_on_one_set_alone_stops_the_document()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => HostAsync(http: c => c.Add(new DescribedSchemaTests.SilentAmount()), mvc: null));

        refusal.Message.ShouldContain($"'{typeof(ProbeAmount)}'");
        refusal.Message.ShouldContain("MVC's as the schema of 'System.Decimal'");
    }

    [Fact]
    public async Task On_both_the_controller_s_wire_and_the_document_agree()
    {
        var (wire, document) = await HostAsync(Strings, Strings);

        wire.ShouldBe("""{"amount":"7.8"}""");
        AmountType(document).ShouldBe("string");
    }

    /// <summary>A host of minimal APIs alone writes nothing with MVC's options, so they are never compared.</summary>
    [Fact]
    public async Task Without_a_controller_the_minimal_API_options_alone_are_enough()
    {
        var (wire, document) = await HostAsync(Strings, mvc: null, controllers: false);

        wire.ShouldBe("""{"amount":"7.8"}""");
        AmountType(document).ShouldBe("string");
    }
}

public sealed record AmountQuote(ProbeAmount Amount);

/// <summary>A value object inside a patch alone, which a source-generated context has no metadata for.</summary>
public sealed record AmendChange(Patch<ProbeAmount> Change);

[JsonSerializable(typeof(AmendChange))]
[JsonSerializable(typeof(int))]
internal sealed partial class AmendJson : JsonSerializerContext;
