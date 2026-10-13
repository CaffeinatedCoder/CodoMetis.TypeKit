using System.Text.Json.Nodes;
using CodoMetis.TypeKit.Generators.Probes;
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

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    private static Task<OpenApiJson> DocumentAsync(OpenApiSpecVersion version) =>
        ProbeHost.DocumentAsync(openApi => openApi.AddTypeKit(), version: version, endpoints: app =>
        {
            app.MapPatch("/patched/{id}", int (int id, OrderPatch patch) => 0);
            app.MapPost("/control", int (Control control) => 0);
        });

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
