using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A nullable value object that is a component admits null as a property wherever the serializer's contract says the property
/// is nullable, as its wrapped type's property does there: a get-only property bound by the constructor, a field under
/// <c>IncludeFields</c>, and a reference-type property in <c>#nullable disable</c> code (decision 36). Each is written as
/// ASP.NET writes a settable annotated property of the same value object beside it, <c>oneOf: [null, component]</c>, and
/// under OpenAPI 2.0 as its <c>allOf: [{x-nullable: true}]</c> without the reference (KB-026).
/// </summary>
/// <remarks>
/// ASP.NET puts the null around a component only for a property whose <c>NullabilityInfoContext</c> write state is nullable,
/// which a get-only property (unknown), a field (not a property) and an oblivious reference type are not. The wrapped type,
/// which is no component, gets its null from <c>JsonPropertyInfo</c> in all three. Without the mark the value object's
/// property was the bare reference where the wrapped type's admitted null (C21OpenapiSchemaParityProbe, confirmed by the
/// code-analyst run of 2026-10-18).
/// </remarks>
public sealed class ComponentNullablePropertyTests
{
    private static readonly ConcurrentDictionary<OpenApiSpecVersion, Task<OpenApiJson>> Documents = new();

    public static TheoryData<OpenApiSpecVersion, string, string> Positions()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0 })
        {
            data.Add(version, "get-only", "GetOnlyTimeSpan");
            data.Add(version, "field", "FieldTimeSpan");
            data.Add(version, "oblivious", "ObliviousString");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public async Task A_nullable_value_object_component_admits_null_where_its_wrapped_type_does(OpenApiSpecVersion version, string position, string twin)
    {
        var document = await Documents.GetOrAdd(version, static v => ProbeHost.DocumentAsync(
            openApi => openApi.AddTypeKit(),
            json: json => json.SerializerOptions.IncludeFields = true,
            version: v,
            endpoints: app =>
            {
                app.MapPost("/get-only/vo", ([FromBody] GetOnlyDuration v) => 0);
                app.MapPost("/get-only/twin", ([FromBody] GetOnlyTimeSpan v) => 0);
                app.MapPost("/field/vo", ([FromBody] ComponentField<ProbeDuration?> v) => 0);
                app.MapPost("/field/twin", ([FromBody] FieldTimeSpan v) => 0);
                app.MapPost("/oblivious/vo", ([FromBody] ObliviousLabel v) => 0);
                app.MapPost("/oblivious/twin", ([FromBody] ObliviousString v) => 0);
            }));

        var holder = Body(document, version, $"/{position}/vo");
        var valueObject = Property(document, holder, "p");
        // ASP.NET's own form for a nullable property of the component, settable and annotated, beside it.
        var asAspNetWritesIt = Property(document, holder, "q");
        var wrapped = Property(document, Body(document, version, $"/{position}/twin"), "p");

        if (version == OpenApiSpecVersion.OpenApi2_0)
            (asAspNetWritesIt["allOf"]?[0]?["x-nullable"]?.GetValue<bool>() == true && asAspNetWritesIt["$ref"] is null).ShouldBeTrue(asAspNetWritesIt.ToJsonString());
        else
            asAspNetWritesIt["oneOf"]?[1]?["$ref"].ShouldNotBeNull(asAspNetWritesIt.ToJsonString());

        valueObject.ShouldDescribeTheSameAs(asAspNetWritesIt, $"{position}: the value object's property against its settable annotated sibling");

        // The wrapped type admits null there, and where the null-or keeps the reference, it is the component's content or null.
        JsonNode.DeepEquals(NullablePositionTests.WithoutNull(wrapped), wrapped).ShouldBeFalse($"{twin}'s property admits no null: {wrapped.ToJsonString()}");
        if (version != OpenApiSpecVersion.OpenApi2_0)
            NullablePositionTests.WithoutNull(Resolve(document, valueObject["oneOf"]![1]!)).ShouldDescribeTheSameAs(NullablePositionTests.WithoutNull(wrapped), $"{position} against {twin}");
    }

    /// <summary>
    /// The input class: a property's nullability as <c>NullabilityInfoContext</c> reads it (ASP.NET's, for a component) against
    /// <c>JsonPropertyInfo</c> (the serializer's, and ASP.NET's for an inlined schema). Each variant is a value object's member
    /// and its wrapped type's, at the same declared nullability; the reference forms are the settable members beside them.
    /// </summary>
    private static readonly (string Name, Type ValueObject, Type Wrapped)[] Variants =
    [
        // A struct value object, made nullable or not.
        ("settable", typeof(NvSettable<ProbeDuration?>), typeof(NvSettable<TimeSpan?>)),
        ("get-only", typeof(NvGetOnly<ProbeDuration?>), typeof(NvGetOnly<TimeSpan?>)),
        ("computed", typeof(NvComputed<ProbeDuration?>), typeof(NvComputed<TimeSpan?>)),
        ("field", typeof(NvField<ProbeDuration?>), typeof(NvField<TimeSpan?>)),
        ("readonly-field", typeof(NvReadOnlyField<ProbeDuration?>), typeof(NvReadOnlyField<TimeSpan?>)),
        ("init", typeof(NvInit<ProbeDuration?>), typeof(NvInit<TimeSpan?>)),
        ("required-init", typeof(NvRequiredInit<ProbeDuration?>), typeof(NvRequiredInit<TimeSpan?>)),
        ("positional", typeof(NvPositional<ProbeDuration?>), typeof(NvPositional<TimeSpan?>)),
        ("private-set-included", typeof(NvIncluded<ProbeDuration?>), typeof(NvIncluded<TimeSpan?>)),
        ("struct-holder-get-only", typeof(NvStructGetOnly<ProbeDuration?>), typeof(NvStructGetOnly<TimeSpan?>)),
        // Types of their own: ASP.NET names the component of NvSettable<TimeSpan> and of NvSettable<TimeSpan?> alike, and
        // describes both as the first it meets.
        ("settable-not-nullable", typeof(NvSettableNotNullable<ProbeDuration>), typeof(NvSettableNotNullable<TimeSpan>)),
        ("get-only-not-nullable", typeof(NvGetOnlyNotNullable<ProbeDuration>), typeof(NvGetOnlyNotNullable<TimeSpan>)),
        ("field-not-nullable", typeof(NvFieldNotNullable<ProbeDuration>), typeof(NvFieldNotNullable<TimeSpan>)),
        // A class value object, annotated, oblivious, or under a nullability attribute.
        ("label-settable", typeof(NvLabelSettable), typeof(NvStringSettable)),
        ("label-get-only", typeof(NvLabelGetOnly), typeof(NvStringGetOnly)),
        ("label-field", typeof(NvLabelField), typeof(NvStringField)),
        ("label-required-init", typeof(NvLabelRequiredInit), typeof(NvStringRequiredInit)),
        ("label-positional", typeof(NvLabelPositional), typeof(NvStringPositional)),
        ("label-oblivious", typeof(NvLabelOblivious), typeof(NvStringOblivious)),
        ("label-oblivious-get-only", typeof(NvLabelObliviousGetOnly), typeof(NvStringObliviousGetOnly)),
        ("label-oblivious-field", typeof(NvLabelObliviousField), typeof(NvStringObliviousField)),
        ("label-allow-null", typeof(NvLabelAllowNull), typeof(NvStringAllowNull)),
        ("label-maybe-null", typeof(NvLabelMaybeNull), typeof(NvStringMaybeNull)),
        ("label-disallow-null", typeof(NvLabelDisallowNull), typeof(NvStringDisallowNull)),
        ("label-not-null", typeof(NvLabelNotNull), typeof(NvStringNotNull)),
        ("label-not-nullable", typeof(NvLabelNotNullable), typeof(NvStringNotNullable)),
        ("label-get-only-not-nullable", typeof(NvLabelGetOnlyNotNullable), typeof(NvStringGetOnlyNotNullable)),
        // A class value object typed by a type parameter, whose annotation the reflected property does not carry.
        ("label-type-parameter-settable", typeof(NvSettable<ProbeLabel>), typeof(NvSettable<string>)),
        ("label-type-parameter-get-only", typeof(NvGetOnly<ProbeLabel>), typeof(NvGetOnly<string>)),
        ("label-type-parameter-field", typeof(NvField<ProbeLabel>), typeof(NvField<string>)),
        ("label-type-parameter-required-init", typeof(NvRequiredInit<ProbeLabel>), typeof(NvRequiredInit<string>)),
    ];

    private static readonly ConcurrentDictionary<(OpenApiSpecVersion, bool, bool), Task<OpenApiJson>> VariantDocuments = new();

    public static TheoryData<OpenApiSpecVersion, bool, bool, string> VariantRows()
    {
        var data = new TheoryData<OpenApiSpecVersion, bool, bool, string>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0 })
        foreach (var components in new[] { true, false })
        foreach (var respectNullableAnnotations in new[] { false, true })
        foreach (var (name, _, _) in Variants)
            data.Add(version, components, respectNullableAnnotations, name);

        return data;
    }

    [Theory]
    [MemberData(nameof(VariantRows))]
    public async Task A_value_object_member_admits_null_exactly_where_its_wrapped_type_s_does(
        OpenApiSpecVersion version, bool components, bool respectNullableAnnotations, string variant)
    {
        var document = await VariantDocuments.GetOrAdd((version, components, respectNullableAnnotations), static key => ProbeHost.DocumentAsync(
            openApi =>
            {
                openApi.AddTypeKit();
                if (!key.Item2)
                    openApi.CreateSchemaReferenceId = type =>
                        (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
                            ? null
                            : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            },
            json: json =>
            {
                json.SerializerOptions.IncludeFields = true;
                json.SerializerOptions.RespectNullableAnnotations = key.Item3;
            },
            version: key.Item1,
            endpoints: app =>
            {
                foreach (var (name, valueObject, wrapped) in Variants)
                {
                    MapBody(app, $"/variants/{name}/vo", valueObject);
                    MapBody(app, $"/variants/{name}/twin", wrapped);
                }
            }));

        JsonNode Member(string name, string side) => Property(document, Body(document, version, $"/variants/{name}/{side}"), "p");
        bool AdmitsNull(JsonNode schema) => !JsonNode.DeepEquals(NullablePositionTests.WithoutNull(schema), schema);

        var valueObject = Member(variant, "vo");
        var wrapped = Member(variant, "twin");
        var label = variant.StartsWith("label-", StringComparison.Ordinal);

        if (components)
        {
            // Not vacuous: ASP.NET's two forms differ, and the wrapped type admits null exactly where its declaration says.
            JsonNode.DeepEquals(Member(label ? "label-settable" : "settable", "vo"), Member(label ? "label-not-nullable" : "settable-not-nullable", "vo")).ShouldBeFalse();
            AdmitsNull(wrapped).ShouldBe(!variant.EndsWith("-not-nullable", StringComparison.Ordinal), wrapped.ToJsonString());

            // ASP.NET's own forms for the component: its settable annotated property, and its property that is not nullable.
            var reference = AdmitsNull(wrapped)
                ? Member(label ? "label-settable" : "settable", "vo")
                : Member(label ? "label-not-nullable" : "settable-not-nullable", "vo");
            valueObject.ShouldDescribeTheSameAs(reference, $"{variant}, the wrapped type's {wrapped.ToJsonString()}");
        }
        else
        {
            NullablePositionTests.Inline(document, valueObject)!.ShouldDescribeTheSameAs(NullablePositionTests.Inline(document, wrapped)!, variant);
        }
    }

    private static void MapBody(WebApplication app, string path, Type type) =>
        typeof(ComponentNullablePropertyTests).GetMethod(nameof(MapBodyOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [app, path]);

    private static void MapBodyOf<T>(WebApplication app, string path) => app.MapPost(path, ([FromBody] T v) => 0);

    private static JsonNode Body(OpenApiJson document, OpenApiSpecVersion version, string path)
    {
        var operation = document.Root["paths"]?[path]?["post"] ?? throw new InvalidOperationException($"No operation post {path}.");
        var body = version == OpenApiSpecVersion.OpenApi2_0
            ? operation["parameters"]!.AsArray().Single(p => p!["in"]!.GetValue<string>() == "body")!["schema"]!
            : operation["requestBody"]!["content"]!["application/json"]!["schema"]!;

        return Resolve(document, body);
    }

    private static JsonNode Property(OpenApiJson document, JsonNode holder, string name) =>
        holder["properties"]?[name]?.DeepClone() ?? throw new InvalidOperationException($"No property '{name}' in {holder.ToJsonString()}.");

    private static JsonNode Resolve(OpenApiJson document, JsonNode schema) =>
        schema["$ref"] is { } reference
            ? (document.Root["components"]?["schemas"] ?? document.Root["definitions"])?[reference.GetValue<string>().Split('/')[^1]]
              ?? throw new InvalidOperationException($"No schema '{reference}'.")
            : schema;
}

public sealed class GetOnlyDuration(ProbeDuration? p)
{
    public ProbeDuration? P { get; } = p;

    public ProbeDuration? Q { get; set; }
}

public sealed class GetOnlyTimeSpan(TimeSpan? p)
{
    public TimeSpan? P { get; } = p;

    public TimeSpan? Q { get; set; }
}

/// <summary>A field of the type argument, serialized under <c>IncludeFields</c>.</summary>
public sealed class ComponentField<T>
{
    public T P = default!;

    public T Q { get; set; } = default!;
}

public sealed class FieldTimeSpan
{
    public TimeSpan? P;

    public TimeSpan? Q { get; set; }
}

#nullable disable
public sealed class ObliviousLabel
{
    public ProbeLabel P { get; set; }
#nullable enable
    public ProbeLabel? Q { get; set; }
#nullable disable
}

public sealed class ObliviousString
{
    public string P { get; set; }
#nullable enable
    public string? Q { get; set; }
#nullable disable
}
#nullable restore

// The variants: the member is p, each beside its wrapped type's (Nv...String... beside Nv...Label...).
public sealed class NvSettable<T> { public T P { get; set; } = default!; }
public sealed class NvGetOnly<T>(T p) { public T P { get; } = p; }
public sealed class NvComputed<T> { public T P => default!; }
public sealed class NvField<T> { public T P = default!; }
public sealed class NvReadOnlyField<T>(T p) { public readonly T P = p; }
public sealed class NvInit<T> { public T P { get; init; } = default!; }
public sealed class NvRequiredInit<T> { public required T P { get; init; } }
public sealed record NvPositional<T>(T P);
public sealed class NvIncluded<T> { [JsonInclude] public T P { get; private set; } = default!; }
public readonly struct NvStructGetOnly<T>(T p) { public T P { get; } = p; }
public sealed class NvSettableNotNullable<T> { public T P { get; set; } = default!; }
public sealed class NvGetOnlyNotNullable<T>(T p) { public T P { get; } = p; }
public sealed class NvFieldNotNullable<T> { public T P = default!; }

public sealed class NvLabelSettable { public ProbeLabel? P { get; set; } }
public sealed class NvStringSettable { public string? P { get; set; } }
public sealed class NvLabelGetOnly(ProbeLabel? p) { public ProbeLabel? P { get; } = p; }
public sealed class NvStringGetOnly(string? p) { public string? P { get; } = p; }
public sealed class NvLabelField { public ProbeLabel? P; }
public sealed class NvStringField { public string? P; }
public sealed class NvLabelRequiredInit { public required ProbeLabel? P { get; init; } }
public sealed class NvStringRequiredInit { public required string? P { get; init; } }
public sealed record NvLabelPositional(ProbeLabel? P);
public sealed record NvStringPositional(string? P);
public sealed class NvLabelAllowNull { [AllowNull] public ProbeLabel P { get; set; } = ProbeLabel.From("x"); }
public sealed class NvStringAllowNull { [AllowNull] public string P { get; set; } = ""; }
public sealed class NvLabelMaybeNull { [MaybeNull] public ProbeLabel P { get; set; } = ProbeLabel.From("x"); }
public sealed class NvStringMaybeNull { [MaybeNull] public string P { get; set; } = ""; }
public sealed class NvLabelDisallowNull { [DisallowNull] public ProbeLabel? P { get; set; } }
public sealed class NvStringDisallowNull { [DisallowNull] public string? P { get; set; } }
public sealed class NvLabelNotNull { [NotNull] public ProbeLabel? P { get; set; } = ProbeLabel.From("x"); }
public sealed class NvStringNotNull { [NotNull] public string? P { get; set; } = ""; }
public sealed class NvLabelNotNullable { public ProbeLabel P { get; set; } = ProbeLabel.From("x"); }
public sealed class NvStringNotNullable { public string P { get; set; } = ""; }
public sealed class NvLabelGetOnlyNotNullable(ProbeLabel p) { public ProbeLabel P { get; } = p; }
public sealed class NvStringGetOnlyNotNullable(string p) { public string P { get; } = p; }

#nullable disable
public sealed class NvLabelOblivious { public ProbeLabel P { get; set; } }
public sealed class NvStringOblivious { public string P { get; set; } }
public sealed class NvLabelObliviousGetOnly(ProbeLabel p) { public ProbeLabel P { get; } = p; }
public sealed class NvStringObliviousGetOnly(string p) { public string P { get; } = p; }
public sealed class NvLabelObliviousField { public ProbeLabel P; }
public sealed class NvStringObliviousField { public string P; }
#nullable restore
