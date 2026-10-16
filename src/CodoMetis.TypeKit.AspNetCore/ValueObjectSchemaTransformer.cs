using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
            // elsewhere for the Nullable<T> of a wrapped value type. Not at the root of a parameter outside the
            // body, which ASP.NET describes without its null (a query int? is an integer), and not at the root
            // of a wrapped reference type, which has none (a string? body is a string, inside ASP.NET's null-or).
            var admitsNull = (context.JsonPropertyInfo is { IsGetNullable: true } or { IsSetNullable: true }
                           || Nullable.GetUnderlyingType(info.Type) is not null && wrapped.IsValueType && !IsParameterOutsideTheBody(context))
                          && SchemaReferenceId(info, context) is null;

            await DescribeAsync(schema, info.Type, wrapped, parameter: null, admitsNull, context, cancellationToken);
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
            // the null-or ASP.NET puts around a nullable body; a parameter outside the body has no null.
            var admitsNull = parameter.Source == BindingSource.Body && Nullable.GetUnderlyingType(valueObject) is not null && parameterWrapped.IsValueType;

            // ASP.NET's placeholder for any TryParse-bound parameter; the wrapped type says what it is.
            schema.Type = null;
            await DescribeAsync(schema, valueObject, parameterWrapped, parameter, admitsNull, context, cancellationToken);
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
    /// adds the null only for a property, a body or a response, never for an element it did not build.
    /// </remarks>
    private static async Task<IOpenApiSchema> ElementSchemaAsync(Type element, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var valueObject = Nullable.GetUnderlyingType(element);

        // Inlined, the nullable value object's own schema admits null (above).
        if (valueObject is null || SchemaReferenceId(context.JsonTypeInfo.Options.GetTypeInfo(element), context) is null)
            return await context.GetOrCreateSchemaAsync(element, parameterDescription: null, cancellationToken);

        var schema = await context.GetOrCreateSchemaAsync(valueObject, parameterDescription: null, cancellationToken);

        var valueObjectOrNull = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, schema] };
        NullableElements.AddOrUpdate(valueObjectOrNull, element);

        return valueObjectOrNull;
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
        context is { JsonPropertyInfo: null, ParameterDescription: { } parameter }
     && parameter.Source != BindingSource.Body
     && (Nullable.GetUnderlyingType(parameter.Type) ?? parameter.Type) == (Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type);

    private static Type? ParameterValueObject(ApiParameterDescription parameter) =>
        ValueObjectTypes.IsValueObject(parameter.Type) ? parameter.Type
        : parameter.ModelMetadata?.ModelType is { } model && ValueObjectTypes.IsValueObject(model) ? model
        : null;

    private static async Task DescribeAsync(
        OpenApiSchema schema,
        Type valueObject,
        Type wrapped,
        ApiParameterDescription? parameter,
        bool admitsNull,
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

            if (!admitsNull)
            {
                schema.FillFrom(wrappedSchema);
            }
            else if (SchemaReferenceId(context.JsonTypeInfo.Options.GetTypeInfo(wrapped), context) is not null)
            {
                if (parameter is not null)
                {
                    // A body's: the null-or ASP.NET puts around a nullable body holds the wrapped type's component
                    // as it is, so its content goes here as it is.
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
                // An inlined wrapped type (a number, a string, a Guid) admits null in its type, as ASP.NET's
                // exporter writes a nullable property of it; an inlined enum or closed set among its values
                // too, since an enum lists every value a validator accepts. A new list: the wrapped one is
                // the wrapped type's.
                schema.FillFrom(wrappedSchema);
                if (schema.Type is { } type) schema.Type = type | JsonSchemaType.Null;
                if (schema.Enum is { Count: > 0 } values && !values.Contains(null!)) schema.Enum = [.. values, null!];
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
