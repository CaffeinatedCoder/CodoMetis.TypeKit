using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Describes every value object in the document with the schema of the type it wraps: the schema
/// ASP.NET itself publishes for that type in this host.
/// </summary>
/// <remarks>
/// <para>
/// Without it, a value object is the empty schema: ASP.NET cannot see through the generated JSON
/// converter. The wrapped type's schema is asked of ASP.NET (<c>GetOrCreateSchemaAsync</c>), so it
/// follows the host's JSON options and every other schema transformer, exactly as the generated
/// converter follows those options. Nothing here knows a format of its own.
/// </para>
/// <para>
/// Three positions, measured in spikes/OpenApiSchemas:
/// </para>
/// <list type="bullet">
/// <item>A value object in a JSON position (property, body, response, or an element reached below)
/// arrives as itself, including one that only ever appears as a property. Its schema is filled in,
/// and ASP.NET still hoists it into the value object's own component. A host that inlines value
/// objects gets no component, and a nullable one then admits null in its own schema.</item>
/// <item>A container of value objects arrives without its element: ASP.NET drops <c>items</c> or
/// <c>additionalProperties</c> for a converter-backed element before any transformer runs. The
/// value object's own schema is put back, only where it is missing, and for a nullable element that
/// schema or null.</item>
/// <item>A parameter bound through the generated <c>TryParse</c> arrives as <see cref="string"/>,
/// ASP.NET's placeholder for any parsable type. The wrapped type's schema replaces it where ASP.NET
/// describes a parameter of the wrapped type by its schema too (a number, a Guid, a date); a parameter
/// of any other wrapped type is that same placeholder, and so it stays.
/// Minimal APIs name the value object in the parameter's <c>Type</c>, MVC in its model metadata.
/// The parameter's descriptor is not used: for an MVC <c>[FromQuery]</c> object it names the
/// container, not the property.</item>
/// <item>A value object the options give a converter of their own, which writes it instead of the
/// generated one, is not the wrapped type's in a JSON position: it is what that converter says it writes
/// (<see cref="DescribedSchemaTransformer"/>), or, where it says nothing, what ASP.NET writes for a
/// converter of one's own. A parameter binds through <c>TryParse</c> and is unchanged.</item>
/// <item>Outside the body, a value object binds through <c>TryParse</c>, as a parameter and as an element of a query or
/// header array: the type it wraps is asked for as the parameter's, so a converter that describes that type, such as
/// <c>DecimalStringJsonConverter</c>, says nothing of it there. Such a parameter is written inline where value objects
/// are components, since the component is what its JSON positions say
/// ([decision 106](design/adr/0106-a-parameter-of-a-value-object-over-a-described-type-is-inlined.md)).</item>
/// </list>
/// </remarks>
internal sealed class ValueObjectSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>The value objects being described on this call path, to refuse one that wraps itself.</summary>
    private static readonly AsyncLocal<ImmutableStack<Type>?> Describing = new();

    /// <summary>
    /// The null-or-value-object schemas put in as the elements of a container of nullable value objects,
    /// with the element type. ASP.NET goes on to visit each as that nullable value object.
    /// </summary>
    private static readonly ConditionalWeakTable<OpenApiSchema, Type> NullableElements = new();

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        // Already complete: describing it as the value object would put the wrapped type's keywords beside
        // its oneOf, and a null then no longer matched them.
        if (NullableElements.TryGetValue(schema, out _)) return;

        var info = context.JsonTypeInfo;

        if (ValueObjectTypes.WrappedType(info.Type) is { } wrapped)
        {
            // Written by a converter the options put in charge, not the generated one: not the wrapped type's.
            if (DescribedSchemaTransformer.Replaced(info.Type, info.Options)) return;

            // Where ASP.NET makes the value object a component, it adds the null around the reference itself.
            // Inlined, the schema is this position's own, and the null goes in it where ASP.NET's exporter puts
            // it into the wrapped type's schema in the same position: for a nullable property of that type, and
            // elsewhere for the Nullable<T> of a wrapped value type, a parameter outside the body included. Not at
            // the root of a wrapped reference type, which has none (a string? body is a string, inside ASP.NET's null-or).
            //
            // Outside the body, as a parameter or an element of one, the value object binds through TryParse: the type it
            // wraps is asked for as that parameter's, so a converter that describes it says nothing here, as it says
            // nothing of a parameter of that type. Then the schema is not its JSON's, and it is written inline, never as
            // the component its JSON positions share (decision 106).
            var outside = ParameterOutsideTheBody(context) is { } given ? Detached(given) : null;
            var inline = outside is not null && WrapsDescribed(info.Type, info.Options);
            if (inline)
            {
                Inline(schema);
                DescribedSchemaTransformer.ParameterDefault(schema, info.Type, info.Options);
            }

            var position = !inline && SchemaReferenceId(info, context) is not null ? NullablePosition.None
                : context.JsonPropertyInfo is { IsGetNullable: true } or { IsSetNullable: true } ? NullablePosition.Property
                : Nullable.GetUnderlyingType(info.Type) is null || !wrapped.IsValueType ? NullablePosition.None
                : IsParameterOutsideTheBody(context) ? NullablePosition.ParameterOutsideTheBody
                : NullablePosition.Root;

            await DescribeAsync(schema, info.Type, wrapped, outside, position, context, cancellationToken);
            return;
        }

        if (context is { JsonPropertyInfo: null, ParameterDescription: { } parameter }
         && ParameterValueObject(parameter) is { } valueObject
         && ValueObjectTypes.WrappedType(valueObject) is { } parameterWrapped
         // Handed the wrapped type itself, this is the call below asking for it: nothing to do.
         && parameterWrapped != info.Type)
        {
            // A parameter of a wrapped type ASP.NET does not describe gets the same placeholder: it stays.
            if (!DescribesParameterAsItself(parameterWrapped)) return;

            // MVC hands a value object bound from the body here too, as the string it would parse. There
            // ASP.NET's exporter admits null in the schema of the Nullable<T> of a wrapped value type, inside
            // the null-or ASP.NET puts around a nullable body, and outside the body among its values alone.
            var position = Nullable.GetUnderlyingType(valueObject) is null || !parameterWrapped.IsValueType ? NullablePosition.None
                : parameter.Source == BindingSource.Body ? NullablePosition.Root
                : NullablePosition.ParameterOutsideTheBody;

            // ASP.NET's placeholder for any TryParse-bound parameter; the wrapped type says what it is.
            schema.Type = null;
            await DescribeAsync(schema, valueObject, parameterWrapped, parameter, position, context, cancellationToken);
            return;
        }

        if (info.ElementType is not { } element || !ValueObjectTypes.IsValueObject(element) || DescribedSchemaTransformer.Replaced(element, info.Options)) return;

        // Only what ASP.NET left out: a schema it did build is its own description of the elements.
        if (info.Kind == JsonTypeInfoKind.Enumerable && schema.Items is null)
            schema.Items = await ElementSchemaAsync(element, context, cancellationToken);
        else if (info.Kind == JsonTypeInfoKind.Dictionary && schema.AdditionalProperties is null)
            schema.AdditionalProperties = await ElementSchemaAsync(element, context, cancellationToken);
    }

    /// <summary>
    /// The value object's schema, or, for a nullable one (<c>List&lt;Quantity?&gt;</c>, whose JSON is
    /// <c>[1,null]</c>), that schema or null: in the form ASP.NET gives a nullable value-object property
    /// where the value object is a component, and its own schema admitting null where the host inlines it.
    /// </summary>
    /// <remarks>
    /// Asking ASP.NET for the <see cref="Nullable{T}"/> itself returned the bare value object where it is a
    /// component: the component is shared with every non-nullable use, so it cannot admit null, and ASP.NET
    /// adds the null only for a property, a body or a response, never for an element it did not build. The
    /// same holds for the component of the type an inlined value object wraps (an enum's), whose content is
    /// then the element's, with the null put in.
    /// </remarks>
    private static async Task<IOpenApiSchema> ElementSchemaAsync(Type element, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        // An element of a parameter outside the body binds through TryParse, as the parameter does: asked for as the
        // parameter's element, it is described as such and written inline where that is not its JSON (decision 106).
        if (ParameterOutsideTheBody(context) is { } parameter && WrapsDescribed(element, context.JsonTypeInfo.Options))
            return await context.GetOrCreateSchemaAsync(element, Detached(parameter), cancellationToken);

        var valueObject = Nullable.GetUnderlyingType(element);
        if (valueObject is null) return await context.GetOrCreateSchemaAsync(element, parameterDescription: null, cancellationToken);

        var options = context.JsonTypeInfo.Options;
        if (SchemaReferenceId(options.GetTypeInfo(element), context) is null)
        {
            // Inlined over an inlined wrapped type, the nullable value object's own schema admits null (above).
            if (SchemaReferenceId(options.GetTypeInfo(ValueObjectTypes.WrappedType(valueObject)!), context) is null)
                return await context.GetOrCreateSchemaAsync(element, parameterDescription: null, cancellationToken);

            // Over the wrapped type's component, the content admits null as it does over an inlined wrapped type: a
            // null-or around it is what OpenAPI 2.0 writes as a bare x-nullable, without the type.
            var content = new OpenApiSchema();
            content.FillFrom(await context.GetOrCreateSchemaAsync(valueObject, parameterDescription: null, cancellationToken));
            AdmitNull(content, inType: true);
            NullableElements.AddOrUpdate(content, element);

            return content;
        }

        var schema = await context.GetOrCreateSchemaAsync(valueObject, parameterDescription: null, cancellationToken);

        var valueObjectOrNull = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, schema] };
        NullableElements.AddOrUpdate(valueObjectOrNull, element);

        return valueObjectOrNull;
    }

    /// <summary>
    /// Puts the null into an inlined schema: into its type, as ASP.NET's exporter writes a nullable property of a
    /// number, a string or a Guid, unless <paramref name="inType"/> says not, and among its values, since an enum
    /// lists every value a validator accepts. A new list: the old one may be the wrapped type's.
    /// </summary>
    private static void AdmitNull(OpenApiSchema schema, bool inType)
    {
        if (inType && schema.Type is { } type) schema.Type = type | JsonSchemaType.Null;
        if (schema.Enum is { Count: > 0 } values && !values.Contains(null!)) schema.Enum = [.. values, null!];
    }

    /// <summary>
    /// The component a type gets in this document, or <see langword="null"/> where the host inlines it: the
    /// document's own <see cref="OpenApiOptions.CreateSchemaReferenceId"/>, which ASP.NET asks too.
    /// </summary>
    internal static string? SchemaReferenceId(JsonTypeInfo type, OpenApiSchemaTransformerContext context) =>
        context.ApplicationServices.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName).CreateSchemaReferenceId(type);

    /// <summary>
    /// Whether a <c>TryParse</c>-bound parameter of <paramref name="type"/> is described by the type's
    /// own schema rather than ASP.NET's placeholder <see cref="string"/>.
    /// </summary>
    /// <remarks>
    /// ASP.NET's own list, from <c>EndpointModelMetadata.GetDisplayType</c>, which is internal. Every
    /// other parsable type is a string as a parameter, whatever its JSON schema says: a type with a JSON
    /// converter of its own has the empty schema (a range literal's type does), which would claim a
    /// parameter could be any JSON value. Each entry has a control in the tests, so a change to ASP.NET's
    /// list fails them.
    /// </remarks>
    private static bool DescribesParameterAsItself(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(Uri);
    }

    /// <summary>
    /// Whether the schema is a parameter's own, bound from anywhere but the body (the route, the query string,
    /// a header, a form field), rather than a body's, a response's, or an element's or property's below one.
    /// </summary>
    private static bool IsParameterOutsideTheBody(OpenApiSchemaTransformerContext context) =>
        ParameterOutsideTheBody(context) is { } parameter && IsParameterItself(parameter, context.JsonTypeInfo.Type);

    /// <summary>
    /// The parameter bound from anywhere but the body whose schema this is, its own or an element's or a wrapped type's
    /// below it; <see langword="null"/> in a JSON position: a body, a response, or a property below either.
    /// </summary>
    private static ApiParameterDescription? ParameterOutsideTheBody(OpenApiSchemaTransformerContext context) =>
        context is { JsonPropertyInfo: null, ParameterDescription: { } parameter } && parameter.Source != BindingSource.Body ? parameter : null;

    /// <summary>
    /// Whether the value object wraps a type the options describe, itself or through the value objects it wraps: as
    /// a parameter it is then described otherwise than in JSON.
    /// </summary>
    private static bool WrapsDescribed(Type valueObject, System.Text.Json.JsonSerializerOptions options)
    {
        var seen = new HashSet<Type>();
        for (var type = Nullable.GetUnderlyingType(valueObject) ?? valueObject; seen.Add(type) && ValueObjectTypes.WrappedType(type) is { } wrapped; type = wrapped)
        {
            if (DescribedSchemaTransformer.Description(wrapped, options, property: null) is not null) return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the schema where it is used rather than as the component ASP.NET would make of it. ASP.NET reads the
    /// component's name from the schema's metadata, and puts a parameter's default there to write it on the reference
    /// instead of the shared schema, each under a key of its own (<c>OpenApiConstants.SchemaId</c> and
    /// <c>RefDefaultAnnotation</c>, internal; measured with Microsoft.AspNetCore.OpenApi 10.0). Without the name it writes
    /// the schema inline, so the default goes back on the schema.
    /// </summary>
    private static void Inline(OpenApiSchema schema)
    {
        if (schema.Metadata is not { } metadata || !metadata.Remove("x-schema-id")) return;

        if (metadata.TryGetValue("x-ref-default", out var value) && metadata.Remove("x-ref-default") && value is JsonNode defaultValue) schema.Default ??= defaultValue;
    }

    /// <summary>
    /// Whether a schema of <paramref name="type"/> visited with <paramref name="parameter"/> is the parameter's own,
    /// rather than an element's below it (<c>decimal</c> under <c>decimal[]</c>) or the type a value object wraps:
    /// ASP.NET hands each of those to the transformers with the parameter's description too.
    /// </summary>
    internal static bool IsParameterItself(ApiParameterDescription parameter, Type type) =>
        (Nullable.GetUnderlyingType(parameter.Type) ?? parameter.Type) == (Nullable.GetUnderlyingType(type) ?? type);

    /// <summary>
    /// The description a schema below a parameter is asked for with, an element's or the type a value object wraps:
    /// where the parameter binds from, and its type, so the schema reads as below it (<see cref="IsParameterItself"/>).
    /// Not the parameter's own: ASP.NET applies a parameter's default and its validation attributes to the schema it is
    /// asked for, and they are the parameter's, already on its schema. Asked for again, they would be the array's on each
    /// element, and a value object's default would be written as the wrapped type, which throws.
    /// </summary>
    internal static ApiParameterDescription Detached(ApiParameterDescription parameter) =>
        new() { Name = parameter.Name, Source = parameter.Source, Type = parameter.Type };

    /// <summary>Where a schema of a value object stands, which decides where its null goes.</summary>
    private enum NullablePosition
    {
        /// <summary>Not nullable, or a component, around whose reference ASP.NET puts the null itself.</summary>
        None,

        /// <summary>A nullable property: ASP.NET's null-or-reference where the wrapped type is a component.</summary>
        Property,

        /// <summary>
        /// The <see cref="Nullable{T}"/> of a value type in a body, a response or a container's element: inside the
        /// null-or ASP.NET puts around a nullable body or response, or the element's own.
        /// </summary>
        Root,

        /// <summary>
        /// The <see cref="Nullable{T}"/> of a value type as a route, query, header or form parameter, which ASP.NET
        /// describes without the null in its type (a query <c>int?</c> is an integer) and with it among its values
        /// (a query <c>DayOfWeek?</c> written by name lists null).
        /// </summary>
        ParameterOutsideTheBody,
    }

    private static Type? ParameterValueObject(ApiParameterDescription parameter) =>
        ValueObjectTypes.IsValueObject(parameter.Type) ? parameter.Type
        : parameter.ModelMetadata?.ModelType is { } model && ValueObjectTypes.IsValueObject(model) ? model
        : null;

    private static async Task DescribeAsync(
        OpenApiSchema schema,
        Type valueObject,
        Type wrapped,
        ApiParameterDescription? parameter,
        NullablePosition position,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var self = Nullable.GetUnderlyingType(valueObject) ?? valueObject;
        var describing = Describing.Value ?? [];

        // A value object that reaches itself through what it wraps has no finite schema; asking for
        // one would recurse until the process dies.
        if (describing.Contains(self))
            throw new InvalidOperationException(
                $"'{self}' cannot be described in an OpenAPI document: it wraps itself " +
                $"({string.Join(" -> ", describing.Reverse().Append(self).Select(type => type.Name))}).");

        Describing.Value = describing.Push(self);
        try
        {
            var wrappedSchema = await context.GetOrCreateSchemaAsync(wrapped, parameter, cancellationToken);

            if (position == NullablePosition.None)
            {
                schema.FillFrom(wrappedSchema);
            }
            else if (SchemaReferenceId(context.JsonTypeInfo.Options.GetTypeInfo(wrapped), context) is not null)
            {
                if (position != NullablePosition.Property)
                {
                    // The wrapped type's component holds no null, and ASP.NET puts none beside it but a property's:
                    // a body's or a response's null-or holds it as it is, a parameter is the reference alone, and an
                    // element gets its null from ElementSchemaAsync. So its content goes here as it is.
                    schema.FillFrom(wrappedSchema);
                    return;
                }

                // A nullable property of the wrapped type is ASP.NET's null-or-reference to the wrapped type's
                // component (an enum's): the same, with the component's content in place of the reference.
                // Visited again, it is rebuilt the same way; the wrapped keywords never reach its top level.
                var content = new OpenApiSchema();
                content.FillFrom(wrappedSchema);
                schema.OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, content];
            }
            else
            {
                // An inlined wrapped type admits null in its type and among its values. Outside the body ASP.NET
                // takes the null out of the type and leaves it among the values (a query DayOfWeek? written by
                // name), so it goes there alone.
                schema.FillFrom(wrappedSchema);
                AdmitNull(schema, inType: position != NullablePosition.ParameterOutsideTheBody);
            }
        }
        catch (NotSupportedException unsupported)
        {
            // ASP.NET builds the wrapped type's schema from the host's JSON contract for it. A
            // source-generated context, the only resolver under Native AOT, has none for a type the
            // host never serializes itself, and the serializer's message then names a Guid the host
            // never asked for.
            throw new InvalidOperationException(
                $"'{self}' is described with the schema of the '{wrapped}' it wraps, and the host's JSON options have no contract " +
                $"for '{wrapped}'. With source-generated JSON (as under Native AOT), add [JsonSerializable(typeof({wrapped.Name}))] " +
                "to the host's JsonSerializerContext.",
                unsupported);
        }
        finally
        {
            Describing.Value = describing;
        }
    }
}
