using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// The branch of a non-abstract <c>[JsonPolymorphic]</c> base that describes the base itself, which ASP.NET never hands to
/// a schema transformer: each of TypeKit's transformers walks it from the polymorphic schema, as ASP.NET walks the rest.
/// </summary>
/// <remarks>
/// <para>
/// A base that is not abstract and does not list itself as a derived type is serialized as itself too, so its schema is
/// an <c>anyOf</c> of each derived type's branch and, last, the base's own (its <c>x-schema-id</c> is <c>Base</c>, the
/// component <c>{Base}Base</c>). ASP.NET visits the derived types' branches by <c>PolymorphismOptions.DerivedTypes</c>,
/// one per index, and stops there (measured with Microsoft.AspNetCore.OpenApi 10.0, and read in its
/// <c>OpenApiSchemaService.InnerApplySchemaTransformersAsync</c>): a value object, a patch, a closed set or a described
/// type among the base's properties was left as ASP.NET writes a converter of one's own, <c>{}</c>, and a list of them
/// lost its <c>items</c>.
/// </para>
/// <para>
/// A transformer that meets a polymorphic schema hands the branches past the derived types to <see cref="WalkAsync"/>,
/// which visits them, and everything below them, in ASP.NET's order and with the type and property ASP.NET would have
/// given. ASP.NET's context cannot be pointed at another type from outside, so each visit gets a context of its own; a
/// schema it asks for (<see cref="GetOrCreateSchemaAsync"/>) is asked of ASP.NET's context, whose transformers a context
/// made here does not have.
/// </para>
/// </remarks>
internal static class PolymorphicBaseBranch
{
    /// <summary>The context ASP.NET made, for each context made here.</summary>
    private static readonly ConditionalWeakTable<OpenApiSchemaTransformerContext, OpenApiSchemaTransformerContext> Origins = new();

    /// <summary>
    /// <see cref="OpenApiSchemaTransformerContext.GetOrCreateSchemaAsync"/> through the context ASP.NET made: the schema of
    /// <paramref name="type"/>, every transformer of the document applied.
    /// </summary>
    public static Task<OpenApiSchema> GetOrCreateSchemaAsync(
        OpenApiSchemaTransformerContext context, Type type, ApiParameterDescription? parameterDescription, CancellationToken cancellationToken) =>
        Origin(context).GetOrCreateSchemaAsync(type, parameterDescription, cancellationToken);

    /// <summary>Visits, with <paramref name="transformer"/>, the branches of a polymorphic schema ASP.NET does not.</summary>
    public static async Task WalkAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, IOpenApiSchemaTransformer transformer, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;
        if (info.PolymorphismOptions is not { } polymorphism || schema.AnyOf is not { } branches) return;

        for (var i = polymorphism.DerivedTypes.Count; i < branches.Count; i++)
            await VisitAsync(branches[i], info, property: null, Origin(context), transformer, cancellationToken);
    }

    private static OpenApiSchemaTransformerContext Origin(OpenApiSchemaTransformerContext context) =>
        Origins.TryGetValue(context, out var origin) ? origin : context;

    /// <summary>A schema and those below it, as <c>InnerApplySchemaTransformersAsync</c> visits them.</summary>
    private static async Task VisitAsync(
        IOpenApiSchema input,
        JsonTypeInfo info,
        JsonPropertyInfo? property,
        OpenApiSchemaTransformerContext origin,
        IOpenApiSchemaTransformer transformer,
        CancellationToken cancellationToken)
    {
        if ((input as OpenApiSchema ?? (input as OpenApiSchemaReference)?.Target as OpenApiSchema) is not { } schema) return;

        var context = new OpenApiSchemaTransformerContext
        {
            DocumentName = origin.DocumentName,
            ParameterDescription = origin.ParameterDescription,
            JsonTypeInfo = info,
            JsonPropertyInfo = property,
            ApplicationServices = origin.ApplicationServices,
            Document = origin.Document,
        };
        Origins.AddOrUpdate(context, origin);

        // A polymorphic schema below walks its own base branch from here, as at the top.
        await transformer.TransformAsync(schema, context, cancellationToken);

        var options = info.Options;

        if (schema.AnyOf is { Count: > 0 } branches && info.PolymorphismOptions is { } polymorphism)
        {
            for (var i = 0; i < polymorphism.DerivedTypes.Count && i < branches.Count; i++)
                await VisitAsync(branches[i], options.GetTypeInfo(polymorphism.DerivedTypes[i].DerivedType), property: null, origin, transformer, cancellationToken);
        }

        if (schema.Items is { } items && info.ElementType is { } element)
            await VisitAsync(items, options.GetTypeInfo(element), property: null, origin, transformer, cancellationToken);

        if (schema.Properties is { Count: > 0 } properties)
        {
            foreach (var member in info.Properties)
            {
                if (properties.TryGetValue(member.Name, out var memberSchema))
                    await VisitAsync(memberSchema, options.GetTypeInfo(member.PropertyType), member, origin, transformer, cancellationToken);
            }
        }

        if (schema is { AdditionalPropertiesAllowed: true, AdditionalProperties: { } values } && info.ElementType is { } valueType)
            await VisitAsync(values, options.GetTypeInfo(valueType), property: null, origin, transformer, cancellationToken);
    }
}
