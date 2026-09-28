using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A host that inlines value objects (the README's <c>CreateSchemaReferenceId</c> returning <see langword="null"/>)
/// gets, in every position, exactly what ASP.NET publishes for the wrapped type there: a nullable value object
/// admits null as a nullable <c>int</c>, <c>string</c>, <c>Guid</c> or enum does, and a non-nullable one does not.
/// </summary>
/// <remarks>
/// Inlined, a nullable value object's schema is its own, not a component shared with the non-nullable uses,
/// so its null belongs in it; ASP.NET adds the null to the wrapped type's schema from the exporter, which sees
/// only <c>{}</c> for a value object. It was left out: <c>maybeCount</c> was <c>{"type":["integer","string"],…}</c>.
/// </remarks>
public sealed class InlinedValueObjectTests
{
    private static readonly ConcurrentDictionary<(OpenApiSpecVersion, bool), Task<OpenApiJson>> Documents = new();

    public static TheoryData<string, OpenApiSpecVersion, bool> PropertiesInEveryPosition()
    {
        var data = new TheoryData<string, OpenApiSpecVersion, bool>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0 })
        {
            foreach (var property in new[]
                     {
                         // Nullable: a struct over a number, a string, a Guid and an enum, and a record class.
                         "maybeCount", "maybeName", "maybeId", "maybeWeekday", "maybeLabel",
                         // Nullable elements of a list, an array and a dictionary.
                         "maybeCounts", "maybeCountArray", "maybeCountsByKey",
                         // The same value objects where they are not nullable, which must stay so.
                         "count", "name", "id", "weekday", "label", "ids", "counts",
                     })
                data.Add(property, version, false);

            // An enum the host writes by name.
            data.Add("maybeWeekday", version, true);
            data.Add("weekday", version, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PropertiesInEveryPosition))]
    public async Task An_inlined_value_object_is_described_as_its_wrapped_type_in_the_same_position(
        string property, OpenApiSpecVersion version, bool enumsByName)
    {
        var document = await Documents.GetOrAdd((version, enumsByName), static key => ProbeHost.DocumentAsync(
            InlineValueObjects,
            key.Item2 ? json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()) : null,
            key.Item1));

        var inlined = document.Property("ProbeDocument", property);
        inlined.ToJsonString().ShouldNotContain("$ref", customMessage: $"'{property}' is not inlined");

        // The wrapped enum is a component of ASP.NET's own, which the inlined value object holds in place.
        Inline(document, inlined)!
            .ShouldDescribeTheSameAs(Inline(document, document.Property("ControlDocument", property))!, property);
    }

    /// <summary>A second <c>AddTypeKit()</c> visits what the first made, the null-or-content schemas included.</summary>
    [Fact]
    public async Task Adding_it_twice_to_an_inlining_host_publishes_the_same_document()
    {
        Action<Microsoft.AspNetCore.Http.Json.JsonOptions> enumsByName = json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

        var once = await ProbeHost.DocumentAsync(InlineValueObjects, enumsByName);
        var twice = await ProbeHost.DocumentAsync(openApi => { InlineValueObjects(openApi); openApi.AddTypeKit(); }, enumsByName);

        twice.Root.ShouldDescribeTheSameAs(once.Root);
    }

    /// <summary>The schema with every reference replaced by the component it refers to.</summary>
    private static JsonNode? Inline(OpenApiJson document, JsonNode? schema) => schema switch
    {
        JsonObject reference when reference["$ref"] is { } id => Inline(document, document.Component(id.GetValue<string>().Split('/')[^1])),
        JsonObject keywords => new JsonObject(keywords.Select(keyword => KeyValuePair.Create(keyword.Key, Inline(document, keyword.Value)))),
        JsonArray items => new JsonArray([.. items.Select(item => Inline(document, item))]),
        _ => schema?.DeepClone(),
    };

    /// <summary>The README's predicate.</summary>
    private static void InlineValueObjects(OpenApiOptions openApi)
    {
        openApi.AddTypeKit();
        openApi.CreateSchemaReferenceId = type =>
            (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
                ? null
                : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
    }
}
