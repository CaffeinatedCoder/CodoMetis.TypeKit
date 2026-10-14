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
        var info = context.JsonTypeInfo;
        var options = info.Options;

        // A body is a parameter too, and so is every schema inside one: only a parameter bound from the
        // route, the query, a header or a form field is text TryParse reads.
        if (context is { JsonPropertyInfo: null, ParameterDescription: { } parameter } && parameter.Source != BindingSource.Body)
        {
            if (Description(info.Type, options, property: null) is not null) Parameter(schema, info.Type, options);
            return;
        }

        DescribedOptionsAgreement.ThrowIfDisagreeing(context.ApplicationServices, info.Type, options);
        if (info.ElementType is { } contained) DescribedOptionsAgreement.ThrowIfDisagreeing(context.ApplicationServices, contained, options);

        if (Description(info.Type, options, context.JsonPropertyInfo) is { } description)
        {
            // ASP.NET writes a type with a reference id once, as a component every position refers to, so a
            // converter one property names cannot be told there: the component would say it for every
            // position, or for none, by whichever came first.
            if (context.JsonPropertyInfo is { CustomConverter: not null } own && ValueObjectSchemaTransformer.SchemaReferenceId(info, context) is { } component)
                throw new InvalidOperationException(
                    $"The property '{own.DeclaringType}.{own.Name}' names a converter of its own for '{info.Type}', which the document describes once, as the " +
                    $"component '{component}', for every position. Register the converter on the JSON options, or give the property a type of its own.");

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
    /// The description this transformer applies: the host's for any type, and a converter's; for a value
    /// object or a closed set, whose own transformers describe what the generated converter writes, only a
    /// converter that replaces the generated one, named on the property or put in charge by the options.
    /// </summary>
    internal static JsonSchemaDescription? Description(Type type, JsonSerializerOptions options, JsonPropertyInfo? property)
    {
        if (JsonSchemaDescriptions.Registered(type, options) is { } registered) return registered;

        var value = Nullable.GetUnderlyingType(type) ?? type;
        if ((ValueObjectTypes.IsValueObject(value) || ClosedSetSchemaTransformer.Describe(value) is not null) && !Replaced(value, options, property)) return null;

        return JsonSchemaDescription.For(type, options, property);
    }

    /// <summary>Whether a converter other than the generated one writes <paramref name="value"/>: the property's own, or the options'.</summary>
    private static bool Replaced(Type value, JsonSerializerOptions options, JsonPropertyInfo? property) =>
        property?.CustomConverter is not null || WrittenByTheOptions(value, options);

    /// <summary>
    /// Whether the first of the options' converters that converts <paramref name="type"/>, as the serializer
    /// picks, writes it instead of the generated converter; a factory that says nothing of itself, such as
    /// <c>StoredJsonConverterFactory</c>, writes what the generated converter writes. The value-object and
    /// closed-set transformers then leave the type to this one: what that converter says, or, where it says
    /// nothing, what ASP.NET writes for a converter of one's own.
    /// </summary>
    internal static bool WrittenByTheOptions(Type type, JsonSerializerOptions options)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        var first = options.Converters.FirstOrDefault(converter => converter is not JsonSchemaDescriptions && converter.CanConvert(value));
        return first is IDescribedJsonConverter || first is not null and not System.Text.Json.Serialization.JsonConverterFactory;
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

    internal static async Task<OpenApiSchema> SchemaAsync(JsonSchemaDescription description, Type type, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
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

    /// <summary>
    /// A described JSON Schema as Microsoft.OpenApi reads one. A schema it cannot read, or reads as something
    /// else, stops the document, naming the type: a keyword it does not model (<c>prefixItems</c>) is written
    /// aside as an unrecognised one, where no validator sees it, and the schema then means something else.
    /// </summary>
    internal static OpenApiSchema Parse(JsonObject schema, Type type)
    {
        var parsed = OpenApiModelFactory.Parse<OpenApiSchema>(schema.ToJsonString(), OpenApiSpecVersion.OpenApi3_1, new OpenApiDocument(), out var diagnostic, "json");

        if (parsed is null || diagnostic is { Errors.Count: > 0 })
            throw new InvalidOperationException(
                $"The JSON schema described for '{type}' is not one an OpenAPI document can hold: " +
                string.Join("; ", diagnostic?.Errors.Select(error => error.Message) ?? ["it did not parse"]) + ".");

        if (FirstChanged(schema, Written(parsed), "") is { } changed)
            throw new InvalidOperationException(
                $"The JSON schema described for '{type}' does not survive into an OpenAPI document: '{changed}' is written otherwise than described, " +
                "so validators would read another schema. Describe it with the keywords OpenAPI 3.1 models.");

        return parsed;
    }

    /// <summary>The schema as the document writes it in OpenAPI 3.1.</summary>
    private static JsonObject Written(OpenApiSchema schema)
    {
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        schema.SerializeAsV31(new OpenApiJsonWriter(text));
        return JsonNode.Parse(text.ToString())!.AsObject();
    }

    /// <summary>
    /// The pointer to the first keyword the written schema drops, adds or changes; a type list is a set, which
    /// the writer orders its own way.
    /// </summary>
    private static string? FirstChanged(JsonNode? described, JsonNode? written, string pointer)
    {
        switch (described, written)
        {
            case (JsonObject left, JsonObject right):
                foreach (var key in left.Select(p => p.Key).Union(right.Select(p => p.Key)))
                {
                    var at = pointer + "/" + key;
                    if (!right.ContainsKey(key) && FalseByDefault.Contains(key) && left[key] is JsonValue unset && unset.GetValueKind() == JsonValueKind.False) continue;
                    if (!left.ContainsKey(key) || !right.ContainsKey(key)) return at;
                    if (key == "type" && Types(left[key]).SetEquals(Types(right[key]))) continue;
                    if (FirstChanged(left[key], right[key], at) is { } inner) return inner;
                }

                return null;
            case (JsonArray left, JsonArray right):
                if (left.Count != right.Count) return pointer;
                for (var i = 0; i < left.Count; i++)
                {
                    if (FirstChanged(left[i], right[i], pointer + "/" + i) is { } inner) return inner;
                }

                return null;
            case (JsonValue left, JsonValue right) when left.GetValueKind() is JsonValueKind.Number && right.GetValueKind() is JsonValueKind.Number:
                return JsonNode.DeepEquals(left, right) || Number(left) is { } leftNumber && Number(right) is { } rightNumber && leftNumber == rightNumber ? null : pointer;
            default:
                return JsonNode.DeepEquals(described, written) ? null : pointer.Length == 0 ? "/" : pointer;
        }
    }

    /// <summary>Annotations whose <c>false</c> is their default, which the writer leaves out.</summary>
    private static readonly string[] FalseByDefault = ["readOnly", "writeOnly", "deprecated", "uniqueItems"];

    /// <summary>A number as written, <c>1</c> and <c>1.0</c> alike, or null where a decimal cannot hold it.</summary>
    private static decimal? Number(JsonValue value) =>
        decimal.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : null;

    private static HashSet<string> Types(JsonNode? type) => type switch
    {
        JsonArray list => [.. list.Select(t => t?.ToString() ?? "")],
        JsonValue single => [single.ToString()],
        _ => [],
    };

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
