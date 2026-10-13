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
    public sealed record OrderPatch(Patch<string?> Name, Patch<int> Quantity, Patch<ProbeCode> Code, Patch<ProbeId> Id, Patch<Lines?> Lines);

    public sealed record Lines(int Count);

    public sealed record Control(string? Name, int Quantity, ProbeCode Code, ProbeId Id, Lines? Lines);

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

    /// <summary>One patch per kind of schema: a type, an enum without a type, an enum with one, branches, none at all.</summary>
    public sealed record KindsPatch(
        Patch<string?> Name, Patch<int> Quantity, Patch<ProbeCode> Code, Patch<ProbeId> Id, Patch<Lines> Lines,
        Patch<Colour> Colour, Patch<ProbeStatus> Status, Patch<Size> Size, Patch<int[]> Numbers, Patch<Shape> Shape, Patch<object> Anything, Patch<Either> Either);

    /// <summary>The same arguments, unpatched: what each value is judged against.</summary>
    public sealed record Kinds(
        string Name, int Quantity, ProbeCode Code, ProbeId Id, Lines Lines,
        Colour Colour, ProbeStatus Status, Size Size, int[] Numbers, Shape Shape, object Anything, Either Either);

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    private static Task<OpenApiJson> DocumentAsync(OpenApiSpecVersion version) =>
        ProbeHost.DocumentAsync(openApi =>
        {
            openApi.AddTypeKit();
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
            app.MapPatch("/kinds", int (KindsPatch patch) => 0);
            app.MapPost("/kinds", int (Kinds kinds) => 0);
        });

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
