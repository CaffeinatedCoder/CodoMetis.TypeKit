using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A closed set in the document: a string enum of its members' names, as an <c>enum</c> is
/// described, read from the attribute the generators put on the set, never by reflection over
/// its members.
/// </summary>
/// <remarks>
/// The three positions of <see cref="ValueObjectSchemaTransformer"/>: a set in a JSON position
/// arrives as itself; a route or query parameter bound through the generated <c>TryParse</c>
/// arrives as ASP.NET's placeholder <see cref="string"/>, and is the enum as an <c>enum</c>
/// parameter is; and a container of sets arrives without its element, which ASP.NET drops for a
/// converter-backed element, so the set's own schema is put back where it is missing.
/// </remarks>
internal sealed class ClosedSetSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly ConcurrentDictionary<Type, GeneratedClosedSetAttribute?> Descriptions = new();

    public static GeneratedClosedSetAttribute? Describe(Type type) =>
        Descriptions.GetOrAdd(type, static candidate =>
            candidate.GetCustomAttribute<GeneratedClosedSetAttribute>(inherit: false) is { } set && set.SetType == candidate ? set : null);

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;

        if (Describe(info.Type) is { } set)
        {
            // Written by a converter the options put in charge, not the generated one: not its names.
            if (DescribedSchemaTransformer.WrittenByTheOptions(info.Type, info.Options)) return;

            // Where the set is a component, ASP.NET puts the null of a nullable property around the reference.
            // Inlined, the schema is this property's own, and the null goes in it (decision 36), as ASP.NET
            // puts it in an inlined nullable string's type and an inlined nullable enum's values.
            var admitsNull = context.JsonPropertyInfo is { IsGetNullable: true } or { IsSetNullable: true }
                          && ValueObjectSchemaTransformer.SchemaReferenceId(info, context) is null;

            Fill(schema, set, admitsNull);
            return;
        }

        if (context is { JsonPropertyInfo: null, ParameterDescription: { } parameter } && ParameterSet(parameter) is { } parameterSet)
        {
            Fill(schema, parameterSet, admitsNull: false);
            return;
        }

        if (info.ElementType is not { } element || Describe(element) is null || DescribedSchemaTransformer.WrittenByTheOptions(element, info.Options)) return;

        // Only what ASP.NET left out: a schema it did build is its own description of the elements.
        if (info.Kind == JsonTypeInfoKind.Enumerable && schema.Items is null)
            schema.Items = await context.GetOrCreateSchemaAsync(element, parameterDescription: null, cancellationToken);
        else if (info.Kind == JsonTypeInfoKind.Dictionary && schema.AdditionalProperties is null)
            schema.AdditionalProperties = await context.GetOrCreateSchemaAsync(element, parameterDescription: null, cancellationToken);
    }

    private static void Fill(OpenApiSchema schema, GeneratedClosedSetAttribute set, bool admitsNull)
    {
        schema.Properties?.Clear();
        schema.Required?.Clear();
        schema.Type = admitsNull ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        schema.Enum = [.. set.Names.Select(name => (System.Text.Json.Nodes.JsonNode)name)];

        // An enum lists every value a validator accepts, so the null is one of them too.
        if (admitsNull) schema.Enum.Add(null!);
    }

    /// <summary>The set a parameter binds to: minimal APIs name it in the parameter's type, MVC in its model metadata.</summary>
    private static GeneratedClosedSetAttribute? ParameterSet(ApiParameterDescription parameter) =>
        Describe(parameter.Type)
     ?? (parameter.ModelMetadata?.ModelType is { } model ? Describe(model) : null);
}
