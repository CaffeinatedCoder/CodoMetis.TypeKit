using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A non-abstract <c>[JsonPolymorphic]</c> base is serialized as itself too, and its schema has a branch of its own beside
/// each derived type's (<c>{Base}Base</c>). Every property of that branch is described as the same property is in the
/// derived type's branch, which inherits it: a value object, a nullable one, a list of them, a patch of one, a closed set
/// and a list of them, a described decimal and a list of them, a value object over a described decimal, and a nested object
/// holding a value object; and the plain twin of each. In MVC and minimal APIs, OpenAPI 3.1, 3.0 and 2.0, with everything
/// inlined, value objects alone inlined, or components.
/// </summary>
/// <remarks>
/// Found by the code analyst (2026-10-16, A1): ASP.NET hands the base's own branch to no schema transformer, so it kept
/// what ASP.NET writes for a converter of one's own: <c>{}</c> for a value object, a list without <c>items</c>, a patch
/// as <c>{}</c>, a described decimal as <c>{"format":"double"}</c>. 2.0 leaves the base's branch out of the root's
/// <c>allOf</c>, for the plain twin too (ASP.NET's own); its definition is compared here.
/// </remarks>
public sealed class PolymorphicBaseTests
{
    private static readonly ConcurrentDictionary<(OpenApiSpecVersion, string, string, string), Task<OpenApiJson>> Documents = new();

    public static TheoryData<OpenApiSpecVersion, string, string, string> Variants()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string, string>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0 })
        foreach (var references in new[] { "inline-all", "inline-value-objects", "components" })
        foreach (var host in new[] { "mvc", "minimal" })
        foreach (var model in new[] { nameof(PolyAnimal), nameof(PlainAnimal) })
            data.Add(version, references, host, model);

        return data;
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Each_property_of_a_polymorphic_base_s_own_branch_is_described_as_in_the_derived_branch(
        OpenApiSpecVersion version, string references, string host, string model)
    {
        var document = await Documents.GetOrAdd((version, references, host, model), static key => ProbeHost.DocumentAsync(
            openApi =>
            {
                if (key.Item2 == "inline-all") openApi.CreateSchemaReferenceId = _ => null;
                openApi.AddTypeKit();

                // The README's way to inline value objects alone.
                if (key.Item2 == "inline-value-objects")
                    openApi.CreateSchemaReferenceId = type =>
                        System.Reflection.CustomAttributeExtensions.GetCustomAttribute<CodoMetis.TypeKit.CompilerServices.GeneratedValueObjectAttribute>(Nullable.GetUnderlyingType(type.Type) ?? type.Type) is not null
                            ? null
                            : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            },
            json: json => json.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()),
            version: key.Item1,
            endpoints: key.Item3 == "minimal"
                ? app =>
                {
                    if (key.Item4 == nameof(PolyAnimal)) app.MapPost("/poly", int (PolyAnimal animal) => 0);
                    else app.MapPost("/poly", int (PlainAnimal animal) => 0);
                }
                : null,
            controllers: key.Item3 == "mvc" ? [key.Item4 == nameof(PolyAnimal) ? typeof(PolyAnimalController) : typeof(PlainAnimalController)] : null));

        var baseBranch = Definitions(document)[references == "inline-all" ? "Base" : model + "Base"]?["properties"]?.AsObject()
            ?? throw new InvalidOperationException($"No base branch: {string.Join(", ", Definitions(document).Select(c => c.Key))}");
        var derived = Derived(document.Root) ?? throw new InvalidOperationException("No derived branch.");

        baseBranch.Count.ShouldBeGreaterThanOrEqualTo(model == nameof(PolyAnimal) ? 11 : 9, baseBranch.ToJsonString());

        var differences = new List<string>();
        foreach (var (name, schema) in baseBranch)
        {
            if (!JsonNode.DeepEquals(schema, derived[name]))
                differences.Add($"{name}: {schema?.ToJsonString()}\n   in the derived branch: {derived[name]?.ToJsonString()}");
        }

        differences.ShouldBeEmpty(string.Join("\n", differences));
    }

    private static JsonObject Definitions(OpenApiJson document) =>
        (document.Root["components"]?["schemas"] ?? document.Root["definitions"])?.AsObject() ?? [];

    /// <summary>The derived type's branch, wherever the document writes it: the one schema whose properties hold <c>meow</c>.</summary>
    private static JsonObject? Derived(JsonNode? node) => node switch
    {
        JsonObject schema when schema["properties"] is JsonObject properties && properties.ContainsKey("meow") => properties,
        JsonObject schema => schema.Select(keyword => Derived(keyword.Value)).FirstOrDefault(found => found is not null),
        JsonArray items => items.Select(Derived).FirstOrDefault(found => found is not null),
        _ => null,
    };

    [ApiController]
    public sealed class PolyAnimalController : ControllerBase
    {
        [HttpPost("/poly")] public int Post(PolyAnimal animal) => 0;
    }

    [ApiController]
    public sealed class PlainAnimalController : ControllerBase
    {
        [HttpPost("/poly")] public int Post(PlainAnimal animal) => 0;
    }
}

/// <summary>A base serialized as itself and as <see cref="PolyCat"/>, with a property of each kind TypeKit describes.</summary>
[JsonPolymorphic, JsonDerivedType(typeof(PolyCat), "cat")]
public class PolyAnimal
{
    public ProbeCount Plain { get; set; }

    public ProbeCount? Maybe { get; set; }

    public List<ProbeCount> Counts { get; set; } = [];

    public Patch<ProbeCount> Patched { get; init; }

    public ProbeStatus Status { get; set; } = ProbeStatus.Open;

    public List<ProbeStatus> Statuses { get; set; } = [];

    public decimal Amount { get; set; }

    public List<decimal> Amounts { get; set; } = [];

    public ProbeAmount Money { get; set; }

    public ProbeAmount? MaybeMoney { get; set; }

    public PolyInner Inner { get; set; } = new();
}

public sealed class PolyCat : PolyAnimal
{
    public string Meow { get; set; } = "";
}

public sealed class PolyInner
{
    public ProbeCount Count { get; set; }
}

/// <summary><see cref="PolyAnimal"/> with the types its value objects wrap and a set's names.</summary>
[JsonPolymorphic, JsonDerivedType(typeof(PlainCat), "cat")]
public class PlainAnimal
{
    public int Plain { get; set; }

    public int? Maybe { get; set; }

    public List<int> Counts { get; set; } = [];

    public Patch<int> Patched { get; init; }

    public string Status { get; set; } = "Open";

    public List<string> Statuses { get; set; } = [];

    public decimal Amount { get; set; }

    public List<decimal> Amounts { get; set; } = [];

    public PlainInner Inner { get; set; } = new();
}

public sealed class PlainCat : PlainAnimal
{
    public string Meow { get; set; } = "";
}

public sealed class PlainInner
{
    public int Count { get; set; }
}
