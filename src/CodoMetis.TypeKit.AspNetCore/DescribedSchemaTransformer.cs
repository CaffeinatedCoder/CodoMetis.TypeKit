using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Describes a type the way its JSON converter says it writes it (<see cref="IDescribedJsonConverter"/>), or
/// the way the host's options say (<see cref="JsonSchemaDescriptions"/>), wherever it appears: as a
/// property, a body or a response, a nullable one, a list's element or a dictionary's value, and inside a
/// value object or a patch, which ask for their argument's schema.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET cannot see through a converter of one's own: it writes the type as <c>{}</c>, or with only the
/// <c>format</c> its CLR type maps to (a decimal's <c>double</c>), and drops <c>items</c> and
/// <c>additionalProperties</c> for an element of one (measured with Microsoft.AspNetCore.OpenApi 10).
/// Only what the converter or the host says is written; a type nothing describes is left as ASP.NET writes
/// it ([decision 69](design/adr/0069-plain-enum-schemas-left-to-host.md)).
/// </para>
/// <para>
/// A value object and a closed set have transformers of their own, which describe them as the wrapped type
/// and as their names; a description the host gives one of them comes first, and is applied here. So this
/// transformer runs after those two, which <c>AddTypeKit()</c> registers first: it replaces the shape they
/// wrote, where they would only have filled in what it left open.
/// </para>
/// <para>
/// A route, query or header parameter binds through <c>TryParse</c>, not JSON, so a described converter
/// says nothing of it. ASP.NET describes a parameter of a primitive, a date, a <see cref="decimal"/> or a
/// <see cref="Guid"/> by the type's JSON schema, which the converter emptied; the parameter gets back the
/// schema the type has without converters of one's own, beside the <c>format</c> ASP.NET kept.
/// </para>
/// </remarks>
internal sealed class DescribedSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>The types described as another on this call path, to refuse a description that never ends.</summary>
    private static readonly AsyncLocal<ImmutableStack<Type>?> SameAsPath = new();

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        DescribedOptionsAgreement.ThrowIfDisagreeing(context.ApplicationServices);

        var info = context.JsonTypeInfo;
        var options = info.Options;

        // A body is a parameter too, and so is every schema inside one: only a parameter bound from the
        // route, the query, a header or a form field is text TryParse reads.
        if (context is { JsonPropertyInfo: null, ParameterDescription: { } parameter } && parameter.Source != BindingSource.Body)
        {
            if (Description(info.Type, options, property: null) is not null) Parameter(schema, info.Type, options);
            return;
        }

        if (Description(info.Type, options, context.JsonPropertyInfo) is { } description)
        {
            var admitsNull = (Nullable.GetUnderlyingType(info.Type) is not null || context.JsonPropertyInfo is { IsGetNullable: true } or { IsSetNullable: true })
                          && ValueObjectSchemaTransformer.SchemaReferenceId(info, context) is null;

            Reshape(schema);
            var described = await SchemaAsync(description, info.Type, context, cancellationToken);
            if (admitsNull) PatchSchemaTransformer.AdmitNull(schema, described);
            else schema.FillFrom(described);
            return;
        }

        if (info.ElementType is not { } element || info.Kind is not (JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary)) return;
        if (Description(element, options, property: null) is not { } elementDescription) return;

        // Only what ASP.NET left out: a schema it did build is its own description of the elements.
        var missing = info.Kind == JsonTypeInfoKind.Enumerable ? schema.Items is null : schema.AdditionalProperties is null;
        if (!missing) return;

        var elementSchema = new OpenApiSchema();
        var elementDescribed = await SchemaAsync(elementDescription, element, context, cancellationToken);
        if (Nullable.GetUnderlyingType(element) is not null) PatchSchemaTransformer.AdmitNull(elementSchema, elementDescribed);
        else elementSchema.FillFrom(elementDescribed);

        if (info.Kind == JsonTypeInfoKind.Enumerable) schema.Items = elementSchema;
        else schema.AdditionalProperties = elementSchema;
    }

    /// <summary>
    /// The description this transformer applies: the host's for any type, a converter's for a type that is
    /// neither a value object nor a closed set, whose own transformers describe them otherwise.
    /// </summary>
    private static JsonSchemaDescription? Description(Type type, JsonSerializerOptions options, JsonPropertyInfo? property)
    {
        if (JsonSchemaDescriptions.Registered(type, options) is { } registered) return registered;
        if (ValueObjectTypes.IsValueObject(type) || ClosedSetSchemaTransformer.Describe(type) is not null) return null;
        return JsonSchemaDescription.For(type, options, property);
    }

    /// <summary>The shape keywords the description replaces; annotations and the property's own constraints stay.</summary>
    private static void Reshape(OpenApiSchema schema)
    {
        schema.Type = null;
        schema.Format = null;
        schema.Pattern = null;
        schema.Enum = null;
        schema.Items = null;
        schema.Properties = null;
        schema.AdditionalProperties = null;
        schema.AnyOf = null;
        schema.OneOf = null;
        schema.AllOf = null;
        schema.Not = null;
    }

    private static async Task<OpenApiSchema> SchemaAsync(JsonSchemaDescription description, Type type, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (description.Schema is { } schema) return Parse(schema, type);

        var sameAs = description.SameAsType!;
        var path = SameAsPath.Value ?? [];
        if (path.Contains(sameAs))
            throw new InvalidOperationException(
                $"'{type}' is described as the schema of '{sameAs}', which is described as itself again " +
                $"({string.Join(" -> ", path.Reverse().Append(sameAs).Select(t => t.Name))}): the schema has no end.");

        SameAsPath.Value = path.Push(sameAs);
        try
        {
            var other = await context.GetOrCreateSchemaAsync(sameAs, parameterDescription: null, cancellationToken);
            var copy = new OpenApiSchema();
            copy.FillFrom(other is OpenApiSchema concrete ? concrete : new OpenApiSchema());
            return copy;
        }
        finally
        {
            SameAsPath.Value = path;
        }
    }

    /// <summary>A described JSON Schema as Microsoft.OpenApi reads one; a schema it cannot read stops the document, naming the type.</summary>
    internal static OpenApiSchema Parse(JsonObject schema, Type type)
    {
        var parsed = OpenApiModelFactory.Parse<OpenApiSchema>(schema.ToJsonString(), OpenApiSpecVersion.OpenApi3_1, new OpenApiDocument(), out var diagnostic, "json");

        if (parsed is null || diagnostic is { Errors.Count: > 0 })
            throw new InvalidOperationException(
                $"The JSON schema described for '{type}' is not one an OpenAPI document can hold: " +
                string.Join("; ", diagnostic?.Errors.Select(error => error.Message) ?? ["it did not parse"]) + ".");

        return parsed;
    }

    /// <summary>
    /// A parameter of <paramref name="type"/> as ASP.NET describes it without converters of one's own: the
    /// schema the options' resolver gives the type with none registered, beside the format ASP.NET kept.
    /// </summary>
    private static void Parameter(OpenApiSchema schema, Type type, JsonSerializerOptions options)
    {
        var plain = new JsonSerializerOptions(options);
        plain.Converters.Clear();

        if (!plain.TryGetTypeInfo(type, out var info)) return;
        if (info.GetJsonSchemaAsNode() is not JsonObject native) return;

        var format = schema.Format;
        var description = schema.Description;
        Reshape(schema);
        schema.FillFrom(Parse(native, type));
        schema.Format ??= format;
        schema.Description ??= description;
    }
}
