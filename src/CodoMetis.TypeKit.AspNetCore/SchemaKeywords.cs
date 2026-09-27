using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Carries a wrapped type's schema over onto a value object's, keyword by keyword, filling in only
/// what the value object's schema does not already say.
/// </summary>
/// <remarks>
/// <para>
/// The schema object itself cannot be replaced: a schema transformer receives the instance ASP.NET
/// later hoists into the value object's component. So the keywords are copied into it.
/// </para>
/// <para>
/// Not copied: ASP.NET's <see cref="OpenApiSchema.Metadata"/>, which holds the schema's reference id
/// (copying it turned every use of a value object into a reference to the wrapped type's component
/// and dropped the value object's own), and the keywords that identify a schema document rather than
/// describe values (<c>$id</c>, <c>$anchor</c>, <c>$dynamicAnchor</c>, <c>$schema</c>,
/// <c>$vocabulary</c>), which would give two schemas one identity. A test pins that every settable
/// property of <see cref="OpenApiSchema"/> is in exactly one of the two lists, so a keyword a new
/// Microsoft.OpenApi version adds is classified before it ships.
/// </para>
/// </remarks>
internal static class SchemaKeywords
{
    /// <summary>The properties deliberately left behind.</summary>
    public static readonly IReadOnlySet<string> NotCopied = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(OpenApiSchema.Metadata),
        nameof(OpenApiSchema.Id),
        nameof(OpenApiSchema.Anchor),
        nameof(OpenApiSchema.DynamicAnchor),
        nameof(OpenApiSchema.Schema),
        nameof(OpenApiSchema.Vocabulary),
    };

    /// <summary>Each copied keyword, with how a missing one is filled in from the wrapped type's schema.</summary>
    public static readonly IReadOnlyList<(string Name, Action<OpenApiSchema, OpenApiSchema> Fill)> Copied =
    [
        // Assertions and their annotations.
        Keyword(nameof(OpenApiSchema.Type), Scalar(static s => s.Type, static (s, v) => s.Type = v)),
        Keyword(nameof(OpenApiSchema.Format), Scalar(static s => s.Format, static (s, v) => s.Format = v)),
        Keyword(nameof(OpenApiSchema.Const), Scalar(static s => s.Const, static (s, v) => s.Const = v)),
        Keyword(nameof(OpenApiSchema.Enum), List(static s => s.Enum, static (s, v) => s.Enum = v)),
        Keyword(nameof(OpenApiSchema.Pattern), Scalar(static s => s.Pattern, static (s, v) => s.Pattern = v)),
        Keyword(nameof(OpenApiSchema.MinLength), Scalar(static s => s.MinLength, static (s, v) => s.MinLength = v)),
        Keyword(nameof(OpenApiSchema.MaxLength), Scalar(static s => s.MaxLength, static (s, v) => s.MaxLength = v)),
        Keyword(nameof(OpenApiSchema.Minimum), Scalar(static s => s.Minimum, static (s, v) => s.Minimum = v)),
        Keyword(nameof(OpenApiSchema.Maximum), Scalar(static s => s.Maximum, static (s, v) => s.Maximum = v)),
        Keyword(nameof(OpenApiSchema.ExclusiveMinimum), Scalar(static s => s.ExclusiveMinimum, static (s, v) => s.ExclusiveMinimum = v)),
        Keyword(nameof(OpenApiSchema.ExclusiveMaximum), Scalar(static s => s.ExclusiveMaximum, static (s, v) => s.ExclusiveMaximum = v)),
        Keyword(nameof(OpenApiSchema.MultipleOf), Scalar(static s => s.MultipleOf, static (s, v) => s.MultipleOf = v)),
        Keyword(nameof(OpenApiSchema.ContentEncoding), Scalar(static s => s.ContentEncoding, static (s, v) => s.ContentEncoding = v)),
        Keyword(nameof(OpenApiSchema.ContentMediaType), Scalar(static s => s.ContentMediaType, static (s, v) => s.ContentMediaType = v)),
        Keyword(nameof(OpenApiSchema.ContentSchema), Scalar(static s => s.ContentSchema, static (s, v) => s.ContentSchema = v)),

        // Arrays.
        Keyword(nameof(OpenApiSchema.Items), Scalar(static s => s.Items, static (s, v) => s.Items = v)),
        Keyword(nameof(OpenApiSchema.MinItems), Scalar(static s => s.MinItems, static (s, v) => s.MinItems = v)),
        Keyword(nameof(OpenApiSchema.MaxItems), Scalar(static s => s.MaxItems, static (s, v) => s.MaxItems = v)),
        Keyword(nameof(OpenApiSchema.UniqueItems), Scalar(static s => s.UniqueItems, static (s, v) => s.UniqueItems = v)),
        Keyword(nameof(OpenApiSchema.Contains), Scalar(static s => s.Contains, static (s, v) => s.Contains = v)),
        Keyword(nameof(OpenApiSchema.MinContains), Scalar(static s => s.MinContains, static (s, v) => s.MinContains = v)),
        Keyword(nameof(OpenApiSchema.MaxContains), Scalar(static s => s.MaxContains, static (s, v) => s.MaxContains = v)),

        // Objects.
        Keyword(nameof(OpenApiSchema.Properties), Dictionary(static s => s.Properties, static (s, v) => s.Properties = v)),
        Keyword(nameof(OpenApiSchema.Required), Set(static s => s.Required, static (s, v) => s.Required = v)),
        Keyword(nameof(OpenApiSchema.AdditionalProperties), Scalar(static s => s.AdditionalProperties, static (s, v) => s.AdditionalProperties = v)),
        Keyword(nameof(OpenApiSchema.AdditionalPropertiesAllowed), Restriction(static s => s.AdditionalPropertiesAllowed, static (s, v) => s.AdditionalPropertiesAllowed = v)),
        Keyword(nameof(OpenApiSchema.PatternProperties), Dictionary(static s => s.PatternProperties, static (s, v) => s.PatternProperties = v)),
        Keyword(nameof(OpenApiSchema.PropertyNames), Scalar(static s => s.PropertyNames, static (s, v) => s.PropertyNames = v)),
        Keyword(nameof(OpenApiSchema.MinProperties), Scalar(static s => s.MinProperties, static (s, v) => s.MinProperties = v)),
        Keyword(nameof(OpenApiSchema.MaxProperties), Scalar(static s => s.MaxProperties, static (s, v) => s.MaxProperties = v)),
        Keyword(nameof(OpenApiSchema.DependentRequired), Dictionary(static s => s.DependentRequired, static (s, v) => s.DependentRequired = v)),
        Keyword(nameof(OpenApiSchema.DependentSchemas), Dictionary(static s => s.DependentSchemas, static (s, v) => s.DependentSchemas = v)),
        Keyword(nameof(OpenApiSchema.UnevaluatedProperties), Restriction(static s => s.UnevaluatedProperties, static (s, v) => s.UnevaluatedProperties = v)),
        Keyword(nameof(OpenApiSchema.UnevaluatedPropertiesSchema), Scalar(static s => s.UnevaluatedPropertiesSchema, static (s, v) => s.UnevaluatedPropertiesSchema = v)),
        Keyword(nameof(OpenApiSchema.Discriminator), Scalar(static s => s.Discriminator, static (s, v) => s.Discriminator = v)),

        // Composition and conditionals.
        Keyword(nameof(OpenApiSchema.AllOf), List(static s => s.AllOf, static (s, v) => s.AllOf = v)),
        Keyword(nameof(OpenApiSchema.AnyOf), List(static s => s.AnyOf, static (s, v) => s.AnyOf = v)),
        Keyword(nameof(OpenApiSchema.OneOf), List(static s => s.OneOf, static (s, v) => s.OneOf = v)),
        Keyword(nameof(OpenApiSchema.Not), Scalar(static s => s.Not, static (s, v) => s.Not = v)),
        Keyword(nameof(OpenApiSchema.If), Scalar(static s => s.If, static (s, v) => s.If = v)),
        Keyword(nameof(OpenApiSchema.Then), Scalar(static s => s.Then, static (s, v) => s.Then = v)),
        Keyword(nameof(OpenApiSchema.Else), Scalar(static s => s.Else, static (s, v) => s.Else = v)),
        Keyword(nameof(OpenApiSchema.Definitions), Dictionary(static s => s.Definitions, static (s, v) => s.Definitions = v)),
        Keyword(nameof(OpenApiSchema.DynamicRef), Scalar(static s => s.DynamicRef, static (s, v) => s.DynamicRef = v)),

        // Annotations.
        Keyword(nameof(OpenApiSchema.Title), Scalar(static s => s.Title, static (s, v) => s.Title = v)),
        Keyword(nameof(OpenApiSchema.Description), Scalar(static s => s.Description, static (s, v) => s.Description = v)),
        Keyword(nameof(OpenApiSchema.Comment), Scalar(static s => s.Comment, static (s, v) => s.Comment = v)),
        Keyword(nameof(OpenApiSchema.Default), Scalar(static s => s.Default, static (s, v) => s.Default = v)),
        // Obsolete in favour of Examples, but still written to the document, and another transformer
        // may still set it on the wrapped type.
#pragma warning disable CS0618
        Keyword(nameof(OpenApiSchema.Example), Scalar(static s => s.Example, static (s, v) => s.Example = v)),
#pragma warning restore CS0618
        Keyword(nameof(OpenApiSchema.Examples), List(static s => s.Examples, static (s, v) => s.Examples = v)),
        Keyword(nameof(OpenApiSchema.ReadOnly), Flag(static s => s.ReadOnly, static (s, v) => s.ReadOnly = v)),
        Keyword(nameof(OpenApiSchema.WriteOnly), Flag(static s => s.WriteOnly, static (s, v) => s.WriteOnly = v)),
        Keyword(nameof(OpenApiSchema.Deprecated), Flag(static s => s.Deprecated, static (s, v) => s.Deprecated = v)),
        Keyword(nameof(OpenApiSchema.ExternalDocs), Scalar(static s => s.ExternalDocs, static (s, v) => s.ExternalDocs = v)),
        Keyword(nameof(OpenApiSchema.Xml), Scalar(static s => s.Xml, static (s, v) => s.Xml = v)),
        Keyword(nameof(OpenApiSchema.Extensions), Dictionary(static s => s.Extensions, static (s, v) => s.Extensions = v)),
        Keyword(nameof(OpenApiSchema.UnrecognizedKeywords), Dictionary(static s => s.UnrecognizedKeywords, static (s, v) => s.UnrecognizedKeywords = v)),
    ];

    /// <summary>Fills in every keyword <paramref name="target"/> leaves open from <paramref name="source"/>.</summary>
    public static void FillFrom(this OpenApiSchema target, OpenApiSchema source)
    {
        foreach (var (_, fill) in Copied) fill(source, target);
    }

    private static (string, Action<OpenApiSchema, OpenApiSchema>) Keyword(string name, Action<OpenApiSchema, OpenApiSchema> fill) => (name, fill);

    // Every helper assigns only when the source has something to give. A setter is not always a
    // no-op for "nothing": assigning null to Const marks it set, and the document then says
    // "const": null, a schema that admits only null.

    private static Action<OpenApiSchema, OpenApiSchema> Scalar<T>(Func<OpenApiSchema, T?> get, Action<OpenApiSchema, T> set) where T : class =>
        (from, to) =>
        {
            if (get(to) is null && get(from) is { } value) set(to, value);
        };

    private static Action<OpenApiSchema, OpenApiSchema> Scalar<T>(Func<OpenApiSchema, T?> get, Action<OpenApiSchema, T?> set) where T : struct =>
        (from, to) =>
        {
            if (get(to) is null && get(from) is { } value) set(to, value);
        };

    // Collections are copied, not shared: a later transformer that edits the wrapped type's schema
    // must not edit the value object's with it.

    private static Action<OpenApiSchema, OpenApiSchema> List<T>(Func<OpenApiSchema, IList<T>?> get, Action<OpenApiSchema, IList<T>> set) =>
        (from, to) =>
        {
            if (get(to) is null or { Count: 0 } && get(from) is { Count: > 0 } values) set(to, [.. values]);
        };

    private static Action<OpenApiSchema, OpenApiSchema> Set<T>(Func<OpenApiSchema, ISet<T>?> get, Action<OpenApiSchema, ISet<T>> set) =>
        (from, to) =>
        {
            if (get(to) is null or { Count: 0 } && get(from) is { Count: > 0 } values) set(to, new HashSet<T>(values));
        };

    private static Action<OpenApiSchema, OpenApiSchema> Dictionary<TKey, TValue>(
        Func<OpenApiSchema, IDictionary<TKey, TValue>?> get,
        Action<OpenApiSchema, IDictionary<TKey, TValue>> set)
        where TKey : notnull =>
        (from, to) =>
        {
            if (get(to) is null or { Count: 0 } && get(from) is { Count: > 0 } values) set(to, new Dictionary<TKey, TValue>(values));
        };

    /// <summary>A boolean that is <see langword="false"/> by default: the wrapped type's <see langword="true"/> carries over.</summary>
    private static Action<OpenApiSchema, OpenApiSchema> Flag(Func<OpenApiSchema, bool> get, Action<OpenApiSchema, bool> set) =>
        (from, to) =>
        {
            if (!get(to) && get(from)) set(to, true);
        };

    /// <summary>A boolean that is <see langword="true"/> by default: the wrapped type's <see langword="false"/> carries over.</summary>
    private static Action<OpenApiSchema, OpenApiSchema> Restriction(Func<OpenApiSchema, bool> get, Action<OpenApiSchema, bool> set) =>
        (from, to) =>
        {
            if (get(to) && !get(from)) set(to, false);
        };
}
