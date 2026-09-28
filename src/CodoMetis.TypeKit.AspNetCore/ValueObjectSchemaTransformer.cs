using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
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
/// and ASP.NET still hoists it into the value object's own component.</item>
/// <item>A container of value objects arrives without its element: ASP.NET drops <c>items</c> or
/// <c>additionalProperties</c> for a converter-backed element before any transformer runs. The
/// value object's own schema is put back, only where it is missing, and for a nullable element that
/// schema or null.</item>
/// <item>A parameter bound through the generated <c>TryParse</c> arrives as <see cref="string"/>,
/// ASP.NET's placeholder for any parsable type, which the wrapped type's schema replaces.
/// Minimal APIs name the value object in the parameter's <c>Type</c>, MVC in its model metadata.
/// The parameter's descriptor is not used: for an MVC <c>[FromQuery]</c> object it names the
/// container, not the property.</item>
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
            await DescribeAsync(schema, info.Type, wrapped, parameter: null, context, cancellationToken);
            return;
        }

        if (context is { JsonPropertyInfo: null, ParameterDescription: { } parameter }
         && ParameterValueObject(parameter) is { } valueObject
         && ValueObjectTypes.WrappedType(valueObject) is { } parameterWrapped
         // Handed the wrapped type itself, this is the call below asking for it: nothing to do.
         && parameterWrapped != info.Type)
        {
            // ASP.NET's placeholder for any TryParse-bound parameter; the wrapped type says what it is.
            schema.Type = null;
            await DescribeAsync(schema, valueObject, parameterWrapped, parameter, context, cancellationToken);
            return;
        }

        if (info.ElementType is not { } element || !ValueObjectTypes.IsValueObject(element)) return;

        // Only what ASP.NET left out: a schema it did build is its own description of the elements.
        if (info.Kind == JsonTypeInfoKind.Enumerable && schema.Items is null)
            schema.Items = await ElementSchemaAsync(element, context, cancellationToken);
        else if (info.Kind == JsonTypeInfoKind.Dictionary && schema.AdditionalProperties is null)
            schema.AdditionalProperties = await ElementSchemaAsync(element, context, cancellationToken);
    }

    /// <summary>
    /// The value object's schema, or, for a nullable one (<c>List&lt;Quantity?&gt;</c>, whose JSON is
    /// <c>[1,null]</c>), that schema or null, in the form ASP.NET gives a nullable value-object property.
    /// </summary>
    /// <remarks>
    /// Asking ASP.NET for the <see cref="Nullable{T}"/> itself returned the bare value object: its component
    /// is shared with every non-nullable use, so it cannot admit null, and ASP.NET adds the null only for a
    /// property, a body or a response, never for an element it did not build.
    /// </remarks>
    private static async Task<IOpenApiSchema> ElementSchemaAsync(Type element, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var valueObject = Nullable.GetUnderlyingType(element);
        var schema = await context.GetOrCreateSchemaAsync(valueObject ?? element, parameterDescription: null, cancellationToken);
        if (valueObject is null) return schema;

        var valueObjectOrNull = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, schema] };
        NullableElements.AddOrUpdate(valueObjectOrNull, element);

        return valueObjectOrNull;
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
            schema.FillFrom(wrappedSchema);
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
