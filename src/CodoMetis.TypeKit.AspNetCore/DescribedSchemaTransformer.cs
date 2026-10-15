using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            if (Description(info.Type, options, property: null) is not null) Parameter(schema, info.Type, options, Version(context));
            return;
        }

        DescribedOptionsAgreement.ThrowIfDisagreeing(context.ApplicationServices, info.Type, options);
        if (info.ElementType is { } contained) DescribedOptionsAgreement.ThrowIfDisagreeing(context.ApplicationServices, contained, options);

        // A nullable position of a type with a reference id refers to the component the type is written as
        // once, for every position: a converter registered for the Nullable<T> alone, which writes those
        // positions otherwise than T's converter writes the rest, cannot be told there.
        if (Nullable.GetUnderlyingType(info.Type) is { } held && ValueObjectSchemaTransformer.SchemaReferenceId(info, context) is { } shared
         && !DescribedOptionsAgreement.Same(JsonSchemaDescription.For(info.Type, options), JsonSchemaDescription.For(held, options)))
            throw new InvalidOperationException(
                $"The JSON options write '{info.Type}' with a converter of its own, and '{held}' otherwise, and the document describes both once, as the " +
                $"component '{shared}', for every position. Register the converter for '{held}', or give the nullable positions a type of their own.");

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
    /// object or a closed set, whose own transformers describe what the generated converter writes, only
    /// what a converter that replaces the generated one says.
    /// </summary>
    internal static JsonSchemaDescription? Description(Type type, JsonSerializerOptions options, JsonPropertyInfo? property)
    {
        if (JsonSchemaDescriptions.Registered(type, options) is { } registered) return registered;

        var description = JsonSchemaDescription.For(type, options, property);
        return description is not null && Generated(Nullable.GetUnderlyingType(type) ?? type) is { } generated && DescribedOptionsAgreement.Same(description, generated) ? null : description;
    }

    /// <summary>
    /// Whether the options have <paramref name="type"/> written by a converter other than the generated one,
    /// as the serializer picks it (<see cref="JsonSchemaDescription.For"/>): a converter registered for the
    /// type, for a <see cref="Nullable{T}"/> of it, or made by a factory, or a description the host gives it.
    /// The value-object and closed-set transformers then leave the type to this one: what that converter
    /// says, or, where it says nothing, what ASP.NET writes for a converter of one's own. A factory that
    /// makes the generated converter, <c>StoredJsonConverterFactory</c>, writes what it writes.
    /// </summary>
    internal static bool Replaced(Type type, JsonSerializerOptions options) =>
        Generated(Nullable.GetUnderlyingType(type) ?? type) is { } generated && !DescribedOptionsAgreement.Same(JsonSchemaDescription.For(type, options), generated);

    private static readonly ConcurrentDictionary<Type, JsonSchemaDescription?> GeneratedDescriptions = new();

    /// <summary>What the generated converter writes: a value object as the type it wraps, a closed set as one of its names; null for any other type.</summary>
    private static JsonSchemaDescription? Generated(Type type) => GeneratedDescriptions.GetOrAdd(type, static candidate =>
        ValueObjectTypes.WrappedType(candidate) is { } wrapped
            ? JsonSchemaDescription.SameAs(wrapped)
            : ClosedSetSchemaTransformer.Describe(candidate) is { } set
                ? JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. set.Names.Select(name => JsonValue.Create(name))]) })
                : null);

    /// <summary>The version the document is written in, which decides what a described schema survives into.</summary>
    internal static OpenApiSpecVersion Version(OpenApiSchemaTransformerContext context) =>
        context.ApplicationServices.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName).OpenApiVersion;

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
        if (description.Schema is { } schema) return Parse(schema, type, Version(context));

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
    /// A described JSON Schema as Microsoft.OpenApi reads one, for a document of <paramref name="version"/>.
    /// A schema it cannot read, or writes as something else, stops the document, naming the type: a keyword
    /// the model does not hold (<c>prefixItems</c>) is written aside as an unrecognised one, and one a version
    /// below 3.1 does not hold (<c>if</c>, <c>contains</c>, <c>patternProperties</c>) as an
    /// <c>x-jsonschema-</c> extension or not at all, where no validator reads it, and the schema then means
    /// something else.
    /// </summary>
    internal static OpenApiSchema Parse(JsonObject schema, Type type, OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1)
    {
        var parsed = OpenApiModelFactory.Parse<OpenApiSchema>(schema.ToJsonString(), OpenApiSpecVersion.OpenApi3_1, new OpenApiDocument(), out var diagnostic, "json");

        if (parsed is null || diagnostic is { Errors.Count: > 0 })
            throw new InvalidOperationException(
                $"The JSON schema described for '{type}' is not one an OpenAPI document can hold: " +
                string.Join("; ", diagnostic?.Errors.Select(error => error.Message) ?? ["it did not parse"]) + ".");

        if (FirstChanged(schema, Written(parsed, OpenApiSpecVersion.OpenApi3_1), "") is { } changed)
            throw new InvalidOperationException(
                $"The JSON schema described for '{type}' does not survive into an OpenAPI document: '{changed}' is written otherwise than described, " +
                "so validators would read another schema. Describe it with the keywords OpenAPI 3.1 models.");

        if (version < OpenApiSpecVersion.OpenApi3_1 && FirstLost(schema, Written(parsed, version), "") is { } lost)
            throw new InvalidOperationException(
                $"The JSON schema described for '{type}' does not survive into an OpenAPI {Name(version)} document: '{lost}' is written aside as an extension or left out, " +
                $"where no validator reads it. Describe it with the keywords OpenAPI {Name(version)} models, or write the document in 3.1.");

        return parsed;
    }

    private static string Name(OpenApiSpecVersion version) => version switch
    {
        OpenApiSpecVersion.OpenApi2_0 => "2.0",
        OpenApiSpecVersion.OpenApi3_0 => "3.0",
        _ => "3.1",
    };

    /// <summary>The schema as the document writes it in <paramref name="version"/>.</summary>
    private static JsonObject Written(OpenApiSchema schema, OpenApiSpecVersion version)
    {
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var writer = new OpenApiJsonWriter(text);
        if (version >= OpenApiSpecVersion.OpenApi3_1) schema.SerializeAsV31(writer);
        else if (version == OpenApiSpecVersion.OpenApi3_0) schema.SerializeAsV3(writer);
        else schema.SerializeAsV2(writer);
        return JsonNode.Parse(text.ToString())!.AsObject();
    }

    /// <summary>Where JSON Schema holds schemas inside a schema: one, a list of them, or named ones.</summary>
    private static readonly string[] One = ["items", "additionalItems", "additionalProperties", "unevaluatedItems", "unevaluatedProperties", "not", "contains", "propertyNames", "if", "then", "else", "contentSchema"];
    private static readonly string[] Many = ["allOf", "anyOf", "oneOf", "prefixItems", "items"];
    private static readonly string[] Named = ["properties", "patternProperties", "dependentSchemas"];

    /// <summary>
    /// The pointer to the first described keyword that does not reach a document of a version below 3.1
    /// under its own name or that version's spelling of it (null in the type as <c>nullable</c>, several
    /// types as branches, a <c>const</c> as an <c>enum</c>, an example as <c>example</c>): written aside as an
    /// <c>x-jsonschema-</c> extension, where no validator reads it, or left out (measured with
    /// Microsoft.OpenApi 2.12: <c>if</c>, <c>contains</c>, <c>propertyNames</c>, <c>patternProperties</c>,
    /// <c>dependentSchemas</c> and <c>unevaluatedProperties</c> go aside; <c>dependentRequired</c> and
    /// <c>$comment</c> are left out).
    /// </summary>
    private static string? FirstLost(JsonObject described, JsonObject written, string pointer)
    {
        foreach (var (keyword, value) in described)
        {
            var kept = keyword switch
            {
                "type" => written.ContainsKey("type") || written.ContainsKey("nullable") || written.ContainsKey("anyOf"),
                "const" => written.ContainsKey("const") || written.ContainsKey("enum"),
                "examples" => written.ContainsKey("examples") || written.ContainsKey("example"),
                _ => written.ContainsKey(keyword) || Unwritten(keyword, value),
            };
            if (!kept) return pointer + "/" + keyword;
        }

        foreach (var keyword in written.Select(property => property.Key))
        {
            if (keyword.StartsWith("x-jsonschema-", StringComparison.Ordinal)) return pointer + "/" + keyword["x-jsonschema-".Length..];
        }

        foreach (var (keyword, value) in described)
        {
            var at = pointer + "/" + keyword;
            switch (value, written[keyword])
            {
                case (JsonObject inner, JsonObject innerWritten) when One.Contains(keyword):
                    if (FirstLost(inner, innerWritten, at) is { } lostInside) return lostInside;
                    break;
                case (JsonArray list, JsonArray listWritten) when Many.Contains(keyword):
                    for (var i = 0; i < list.Count && i < listWritten.Count; i++)
                    {
                        if (list[i] is JsonObject item && listWritten[i] is JsonObject itemWritten && FirstLost(item, itemWritten, at + "/" + i) is { } lostItem) return lostItem;
                    }

                    break;
                case (JsonObject map, JsonObject mapWritten) when Named.Contains(keyword):
                    foreach (var (name, item) in map)
                    {
                        if (item is JsonObject named && mapWritten[name] is JsonObject namedWritten && FirstLost(named, namedWritten, at + "/" + name) is { } lostNamed) return lostNamed;
                    }

                    break;
            }
        }

        return null;
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
                    if (!right.ContainsKey(key) && Unwritten(key, left[key])) continue;
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

    /// <summary>
    /// Keywords that restrict nothing when empty, which the writer leaves out: no named subschemas, no
    /// required property (measured with Microsoft.OpenApi 2.12; <c>"properties": {}</c> was refused, reported
    /// 2026-10-14). An empty <c>enum</c> admits nothing, and is no default.
    /// </summary>
    private static readonly string[] EmptyByDefault = ["properties", "patternProperties", "dependentSchemas", "dependentRequired", "required"];

    /// <summary>Whether <paramref name="value"/> is the default of <paramref name="keyword"/>, which the writer leaves out and which means nothing there.</summary>
    private static bool Unwritten(string keyword, JsonNode? value) =>
        FalseByDefault.Contains(keyword) && value is JsonValue unset && unset.GetValueKind() == JsonValueKind.False
     || EmptyByDefault.Contains(keyword) && value is JsonObject { Count: 0 } or JsonArray { Count: 0 };

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
    private static void Parameter(OpenApiSchema schema, Type type, JsonSerializerOptions options, OpenApiSpecVersion version)
    {
        var plain = new JsonSerializerOptions(options);
        plain.Converters.Clear();

        if (!plain.TryGetTypeInfo(type, out var info)) return;
        if (info.GetJsonSchemaAsNode() is not JsonObject native) return;

        var format = schema.Format;
        var description = schema.Description;
        Reshape(schema);
        schema.FillFrom(Parse(native, type, version));
        schema.Format ??= format;
        schema.Description ??= description;
    }
}
