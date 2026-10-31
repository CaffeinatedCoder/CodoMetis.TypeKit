using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using Json.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A <see cref="Patch{T}"/> property in the document: its argument's schema, inlined, never
/// required, admitting null. Measured first in <c>spikes/MergePatch</c>, in 3.1 and 3.0 and under
/// Native AOT, where a value object inside a patch has no contract and is described through what it wraps.
/// </summary>
public sealed class PatchSchemaTests
{
    public sealed record OrderPatch(Patch<string> Name, Patch<int> Quantity, Patch<ProbeCode> Code, Patch<ProbeId> Id, Patch<Lines> Lines);

    public sealed record Lines(int Count);
    /// <summary>Under <c>RespectRequiredConstructorParameters</c>, <c>Name</c>, a constructor parameter, is required by the serializer, and <c>Quantity</c>, an <c>init</c> property, is not.</summary>
    public sealed record StrictPatch(Patch<string> Name)
    {
        public Patch<int> Quantity { get; init; }
    }


    public sealed record Control(string? Name, int Quantity, ProbeCode Code, ProbeId Id, Lines? Lines);

    /// <summary>A patch parameter with <c>= default</c>, which the merge-patches page says breaks the document.</summary>
    public sealed record DefaultedPatch(Patch<int> Quantity = default);

    [JsonConverter(typeof(JsonStringEnumConverter<Colour>))]
    public enum Colour { Red, Green }

    public enum Size { Small, Large }

    [JsonPolymorphic]
    [JsonDerivedType(typeof(Circle), "circle")]
    [JsonDerivedType(typeof(Square), "square")]
    public abstract record Shape;

    public sealed record Circle(int Radius) : Shape;

    public sealed record Square(int Side) : Shape;

    /// <summary>Described by the host as a string or a number, two branches that each refuse null.</summary>
    public sealed record Either(string Value);

    /// <summary>
    /// One patch per kind of schema: a type, an enum without a type, an enum with one, branches, none at all, and the
    /// containers, whose elements are a schema of their own or a component's.
    /// </summary>
    public sealed record KindsPatch(
        Patch<string> Name, Patch<int> Quantity, Patch<ProbeCode> Code, Patch<ProbeId> Id, Patch<Lines> Lines,
        Patch<Colour> Colour, Patch<ProbeStatus> Status, Patch<Size> Size, Patch<int[]> Numbers, Patch<Shape> Shape, Patch<object> Anything, Patch<Either> Either,
        Patch<Lines[]> LinesList, Patch<Dictionary<string, int>> Counts);

    /// <summary>The same arguments, unpatched: what each value is judged against.</summary>
    public sealed record Kinds(
        string Name, int Quantity, ProbeCode Code, ProbeId Id, Lines Lines,
        Colour Colour, ProbeStatus Status, Size Size, int[] Numbers, Shape Shape, object Anything, Either Either,
        Lines[] LinesList, Dictionary<string, int> Counts);

    /// <summary>
    /// Every place a patch can sit, beside a patch property of the same argument: an element of a list, an array or a
    /// dictionary, and an element of a list in a dictionary.
    /// </summary>
    public sealed record PatchPositions<T>(
        Patch<T> Property,
        List<Patch<T>> List,
        Patch<T>[] Array,
        Dictionary<string, Patch<T>> Map,
        IReadOnlyDictionary<string, Patch<T>> ReadOnlyMap,
        Dictionary<string, List<Patch<T>>> Nested) where T : notnull;

    /// <summary>The patch in each position of <see cref="PatchPositions{T}"/> other than the property itself.</summary>
    private static readonly (string Position, Func<JsonNode, JsonNode?> Patch)[] Positions =
    [
        ("list", schema => schema["items"]),
        ("array", schema => schema["items"]),
        ("map", schema => schema["additionalProperties"]),
        ("readOnlyMap", schema => schema["additionalProperties"]),
        ("nested", schema => schema["additionalProperties"]?["items"]),
    ];

    [Microsoft.AspNetCore.Mvc.ApiController]
    public sealed class PositionsController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpPatch("/mvc/positions/guid")]
        public int Patch([Microsoft.AspNetCore.Mvc.FromBody] PatchPositions<Guid> positions) => 0;
    }

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    private static Task<OpenApiJson> DocumentAsync(OpenApiSpecVersion version, string placement = "components") =>
        ProbeHost.DocumentAsync(openApi =>
        {
            if (placement == "inlined by the host") openApi.CreateSchemaReferenceId = _ => null;
            openApi.AddTypeKit(typeKit => typeKit.InlineValueObjects = placement == "InlineValueObjects");
            openApi.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type != typeof(Either)) return Task.CompletedTask;
                schema.Type = null;
                schema.Properties?.Clear();
                schema.Required?.Clear();
                schema.OneOf = [new OpenApiSchema { Type = JsonSchemaType.String }, new OpenApiSchema { Type = JsonSchemaType.Integer }];
                return Task.CompletedTask;
            });
        }, version: version, endpoints: app =>
        {
            app.MapPatch("/patched/{id}", int (int id, OrderPatch patch) => 0);
            app.MapPost("/control", int (Control control) => 0);
            MapPositions<string>(app, "name");
            MapPositions<int>(app, "quantity");
            MapPositions<ProbeCode>(app, "code");
            MapPositions<ProbeId>(app, "id");
            MapPositions<Lines>(app, "lines");
            MapPositions<Colour>(app, "colour");
            MapPositions<ProbeStatus>(app, "status");
            MapPositions<Size>(app, "size");
            MapPositions<int[]>(app, "numbers");
            if (placement != "inlined by the host")
            {
                app.MapPatch("/kinds", int (KindsPatch patch) => 0);
                app.MapPost("/kinds", int (Kinds kinds) => 0);
                MapPositions<Shape>(app, "shape");
            }
            MapPositions<object>(app, "anything");
            MapPositions<Either>(app, "either");
            MapPositions<Lines[]>(app, "linesList");
            MapPositions<Dictionary<string, int>>(app, "counts");
        }, controllers: [typeof(PositionsController)]);

    /// <summary>The positions of a patch of <typeparamref name="T"/>, in a body of their own, and a list of patches as the whole body.</summary>
    private static void MapPositions<T>(WebApplication app, string key) where T : notnull
    {
        app.MapPatch($"/positions/{key}", int (PatchPositions<T> positions) => 0);
        app.MapPatch($"/positions/{key}/body", int (List<Patch<T>> body) => 0);
    }

    /// <summary>A request body's schema, in 3.x the content's and in 2.0 the body parameter's.</summary>
    private static JsonNode Body(OpenApiJson document, string path)
    {
        var operation = document.Root["paths"]?[path]?["patch"] ?? throw new InvalidOperationException($"No operation patch {path}.");
        return operation["requestBody"]?["content"]?["application/json"]?["schema"]
            ?? operation["parameters"]!.AsArray().Single(parameter => parameter!["in"]!.GetValue<string>() == "body")!["schema"]!;
    }

    /// <summary>For each property, values its argument accepts and values it refuses, as JSON.</summary>
    private static readonly (string Property, string[] Values)[] Values =
    [
        ("name", ["\"Ada\"", "1"]),
        ("quantity", ["1", "\"many\"", "true"]),
        ("code", ["\"ABC\"", "1"]),
        ("id", ["\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"", "1"]),
        ("lines", ["{\"count\":1}", "{}", "\"x\""]),
        ("colour", ["\"Red\"", "\"Blue\"", "1"]),
        ("status", ["\"Open\"", "\"Lost\"", "1"]),
        ("size", ["0", "\"x\""]),
        ("numbers", ["[1,2]", "[\"x\"]", "1"]),
        ("shape", ["{\"$type\":\"circle\",\"radius\":1}", "\"x\"", "1"]),
        ("anything", ["1", "\"x\"", "{}", "[]"]),
        ("either", ["\"x\"", "1", "true"]),
        ("linesList", ["[{\"count\":1}]", "[{}]", "{\"count\":1}"]),
        ("counts", ["{\"a\":1}", "{\"a\":\"x\"}", "[1]"]),
    ];

    /// <summary>
    /// A patch's schema is its argument's with null beside it, judged by a JSON Schema validator as a
    /// client would: null is accepted, and every other value exactly as the argument's own schema
    /// judges it. A string enum has no type, a closed set's enum lists its names, and a schema the
    /// host describes as branches that each refuse null admits null only beside them, so null cannot be
    /// admitted by a type alone; an <see cref="object"/> restricts nothing, and admitting null must not
    /// restrict it to null.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_patch_accepts_null_and_otherwise_exactly_what_its_argument_accepts(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version);
        Values.Length.ShouldBe(document.Component(nameof(KindsPatch))["properties"]!.AsObject().Count, "every property of the patch has values to judge");

        foreach (var (property, values) in Values)
        {
            var patch = Validator(document, document.Property(nameof(KindsPatch), property), version);
            var argument = Validator(document, document.Property(nameof(Kinds), property), version);

            patch("null").ShouldBeTrue($"'{property}' should accept null: {document.Property(nameof(KindsPatch), property).ToJsonString()}");
            argument(values[0]).ShouldBeTrue($"'{property}': the control should accept {values[0]}, or the comparison means nothing");

            foreach (var value in values)
                patch(value).ShouldBe(argument(value), $"'{property}' should judge {value} as its argument does: {document.Property(nameof(KindsPatch), property).ToJsonString()}");
        }
    }

    public static TheoryData<OpenApiSpecVersion, string> Hosts()
    {
        var data = new TheoryData<OpenApiSpecVersion, string>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0 })
        foreach (var placement in new[] { "components", "InlineValueObjects", "inlined by the host" })
            data.Add(version, placement);

        return data;
    }

    /// <summary>
    /// A patch anywhere else is described as a patch property is: an element of a list, an array or a dictionary, one in
    /// a list in a dictionary, and the element of a list that is the whole body. ASP.NET left the elements out, as it does
    /// for any converter-backed element, so a client generator typed a map of patches as a map of anything. Over every
    /// argument of the catalogue, in each version, wherever value objects are described, in a minimal API and an MVC
    /// controller.
    /// </summary>
    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_patch_in_any_position_is_described_as_a_patch_property_is(OpenApiSpecVersion version, string placement)
    {
        var document = await DocumentAsync(version, placement);

        // ASP.NET refuses a polymorphic type with no reference id, a plain property of it too (an ArgumentNullException for
        // the referenceId, measured with Microsoft.AspNetCore.OpenApi 10.0), so a host that inlines everything has no shape.
        var arguments = Values.Select(value => value.Property).Where(property => placement != "inlined by the host" || property != "shape");
        foreach (var path in arguments.Select(property => "/positions/" + property).Append("/mvc/positions/guid"))
        {
            var positions = document.Resolve(Body(document, path));
            var property = document.Expand(positions["properties"]!["property"])!;
            property["description"]!.GetValue<string>().ShouldStartWith("A JSON Merge Patch field", customMessage: $"{path}: the property is the patch's");

            foreach (var (position, patch) in Positions)
                document.Expand(patch(positions["properties"]![position]!))
                    .ShouldNotBeNull($"{path} '{position}': {positions["properties"]![position]!.ToJsonString()}")
                    .ShouldDescribeTheSameAs(property, $"{path} '{position}'");

            if (path.StartsWith("/mvc", StringComparison.Ordinal)) continue;
            document.Expand(Body(document, path + "/body")["items"]).ShouldNotBeNull($"{path}/body").ShouldDescribeTheSameAs(property, $"{path}/body");
        }
    }

    /// <summary>
    /// What the document says of a map and a list of patches is what the endpoint reads: a value sets a key and a null
    /// clears it.
    /// </summary>
    [Fact]
    public async Task A_map_of_patches_reads_a_value_and_a_null_as_the_document_says()
    {
        await using var host = await ProbeHost.StartAsync(openApi => openApi.AddTypeKit(), endpoints: app =>
            app.MapPatch("/read", string (PatchPositions<string> patch) => string.Join(" ",
                patch.Map.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value.State}")
                    .Concat(patch.List.Select(element => element.State.ToString())))));

        using var response = await host.Client.PatchAsync("/read", new StringContent(
            """{"map":{"a":"x","b":null},"list":["y",null]}""", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("a=Value b=Null Value Null");
    }

    /// <summary>
    /// The element of a container of patches accepts null and otherwise exactly what its argument accepts, judged by a
    /// JSON Schema validator over the catalogue above, as a patch property is.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_patch_in_a_container_accepts_null_and_otherwise_exactly_what_its_argument_accepts(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version);

        foreach (var (property, values) in Values)
        {
            var positions = document.Resolve(Body(document, "/positions/" + property));
            var argument = Validator(document, document.Property(nameof(Kinds), property), version);

            foreach (var (position, select) in Positions)
            {
                var schema = select(positions["properties"]![position]!) ?? throw new ShouldAssertException($"'{property}' '{position}' has no element schema");
                var patch = Validator(document, schema, version);

                patch("null").ShouldBeTrue($"'{property}' '{position}' should accept null: {schema.ToJsonString()}");
                foreach (var value in values)
                    patch(value).ShouldBe(argument(value), $"'{property}' '{position}' should judge {value} as its argument does: {schema.ToJsonString()}");
            }
        }
    }

    /// <summary>
    /// A validator for a property's schema, its references to components inlined, and in 3.0 its
    /// <c>nullable</c> read as 3.0.3 defines it: null added to a type the same schema states, and
    /// nothing where it states none.
    /// </summary>
    internal static Func<string, bool> Validator(OpenApiJson document, JsonNode schema, OpenApiSpecVersion version)
    {
        var inlined = Inline(document, schema.DeepClone());
        if (version == OpenApiSpecVersion.OpenApi3_0) ReadNullable(inlined);
        inlined.AsObject()["$schema"] = "https://json-schema.org/draft/2020-12/schema";

        var validator = JsonSchema.FromText(inlined.ToJsonString());
        return value =>
        {
            using var instance = JsonDocument.Parse(value);
            return validator.Evaluate(instance.RootElement).IsValid;
        };
    }

    private static JsonNode Inline(OpenApiJson document, JsonNode schema)
    {
        switch (schema)
        {
            case JsonObject node when node["$ref"]?.GetValue<string>() is { } reference:
                return Inline(document, document.Component(reference.Split('/')[^1]).DeepClone());
            case JsonObject node:
                foreach (var (key, child) in node.ToArray())
                    if (child is not null && key != "discriminator") node[key] = Inline(document, child.DeepClone());
                return node;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    if (array[index] is { } child) array[index] = Inline(document, child.DeepClone());
                return array;
            default:
                return schema;
        }
    }

    private static void ReadNullable(JsonNode schema)
    {
        switch (schema)
        {
            case JsonObject node:
                if (node["nullable"]?.GetValue<bool>() == true && node["type"] is JsonValue type)
                    node["type"] = new JsonArray(type.GetValue<string>(), "null");
                node.Remove("nullable");
                foreach (var (_, child) in node) if (child is not null) ReadNullable(child);
                break;
            case JsonArray array:
                foreach (var child in array) if (child is not null) ReadNullable(child);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_patch_property_is_its_argument_s_schema_inlined_and_admits_null(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version);

        foreach (var property in new[] { "name", "quantity", "code", "id" })
        {
            var patch = document.Property(nameof(OrderPatch), property);
            var control = document.Resolve(document.Property(nameof(Control), property));

            patch["$ref"].ShouldBeNull($"'{property}' should be inlined, but was {patch.ToJsonString()}");
            patch["description"]!.GetValue<string>().ShouldStartWith("A JSON Merge Patch field");
            AdmitsNull(patch, version).ShouldBeTrue($"'{property}' should admit null: {patch.ToJsonString()}");
            WithoutNull(patch).ShouldDescribeTheSameAs(WithoutNull(control), property);
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task No_patch_property_is_required(OpenApiSpecVersion version)
    {
        var document = await DocumentAsync(version);

        var required = document.Component(nameof(OrderPatch))["required"]?.AsArray().Select(node => node!.GetValue<string>()) ?? [];
        required.ShouldBeEmpty();

        document.Component(nameof(Control))["required"].ShouldNotBeNull("the control has required properties, so the patch's emptiness means something");
    }

    /// <summary>
    /// The document follows the serializer: a patch parameter without a default is required under
    /// <c>RespectRequiredConstructorParameters</c>, and a body without it is refused, so it stays in
    /// <c>required</c>; an <c>init</c> property is optional under every option.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_patch_the_serializer_requires_stays_required(OpenApiSpecVersion version)
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var strict = await ProbeHost.StartAsync(
            openApi => openApi.AddTypeKit(), json => json.SerializerOptions.RespectRequiredConstructorParameters = true, version,
            app => app.MapPatch("/strict", int (StrictPatch patch) => patch.Quantity.Match(q => q, () => -1, () => 0)));
        var lenient = await ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version, endpoints: app => app.MapPatch("/strict", int (StrictPatch patch) => 0));

        Required(await strict.DocumentAsync()).ShouldBe(["name"]);
        Required(lenient).ShouldBeEmpty();

        using var missing = await strict.Client.PatchAsync("/strict", new StringContent("""{"quantity":2}""", System.Text.Encoding.UTF8, "application/json"), cancellation);
        using var given = await strict.Client.PatchAsync("/strict", new StringContent("""{"name":null}""", System.Text.Encoding.UTF8, "application/json"), cancellation);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "the document says name is required, and the endpoint refuses a body without it");
        given.StatusCode.ShouldBe(HttpStatusCode.OK, "quantity is optional in the document, and the endpoint takes a body without it");

        static IEnumerable<string> Required(OpenApiJson document) =>
            document.Component(nameof(StrictPatch))["required"]?.AsArray().Select(node => node!.GetValue<string>()) ?? [];
    }

    /// <summary>
    /// The merge-patches page's warning: <c>= default</c> on a patch parameter satisfies the serializer, but
    /// System.Text.Json's schema exporter throws on a struct parameter whose default is <c>default</c>, so the
    /// endpoint's document fails. Pinned, so the page is corrected when the exporter learns to describe it.
    /// </summary>
    [Fact]
    public async Task A_patch_parameter_defaulted_with_default_breaks_the_document()
    {
        var refusal = await Should.ThrowAsync<JsonException>(() => ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(), endpoints: app => app.MapPatch("/defaulted", int (DefaultedPatch patch) => 0)));

        refusal.StackTrace.ShouldNotBeNull().ShouldContain(nameof(System.Text.Json.Schema.JsonSchemaExporter), customMessage: "the exporter throws, not the converter");
    }

    private static bool AdmitsNull(JsonNode schema, OpenApiSpecVersion version) =>
        version == OpenApiSpecVersion.OpenApi3_1
            ? schema["type"] is JsonArray types && types.Any(type => type!.GetValue<string>() == "null")
            : schema["nullable"]?.GetValue<bool>() == true;

    /// <summary>The schema without its null admission and the patch's own description, for comparing with the control.</summary>
    private static JsonNode WithoutNull(JsonNode schema)
    {
        var copy = JsonNode.Parse(schema.ToJsonString())!.AsObject();
        copy.Remove("nullable");
        copy.Remove("description");
        if (copy["type"] is JsonArray types)
        {
            var kept = types.Where(type => type!.GetValue<string>() != "null").Select(type => type!.GetValue<string>()).ToArray();
            copy["type"] = kept.Length == 1 ? kept[0] : new JsonArray([.. kept.Select(type => (JsonNode)type)]);
        }
        if (copy["anyOf"] is JsonArray anyOf)
        {
            var kept = anyOf.Where(option => option!["enum"] is null && option["nullable"] is null).Select(option => JsonNode.Parse(option!.ToJsonString())!).ToArray();
            if (kept.Length == 0) copy.Remove("anyOf"); else copy["anyOf"] = new JsonArray(kept);
        }
        return copy;
    }
}
