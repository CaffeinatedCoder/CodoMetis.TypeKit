using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

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

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    private static void Endpoints(WebApplication app)
    {
        app.MapPost("/orders", int (Order order) => 0);
        app.MapPost("/tagged", int (Tagged tagged) => 0);
        app.MapGet("/amounts/{amount}", int (decimal amount) => 0);
    }

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
        Inline(document, document.Property(nameof(ProbeDocument), "weekday"))["maxLength"]!.GetValue<int>().ShouldBe(9, "a value object over an enum, described by the rule through its wrapped type");
    }

    [Fact]
    public async Task A_description_an_OpenAPI_document_cannot_hold_stops_it_naming_the_type()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => DocumentAsync(OpenApiSpecVersion.OpenApi3_1,
            json => json.SerializerOptions.Converters.Add(new JsonSchemaDescriptions().Describe<decimal>(JsonSchemaDescription.Of(new JsonObject { ["type"] = 42 })))));

        refusal.Message.ShouldContain("System.Decimal");
    }
}
