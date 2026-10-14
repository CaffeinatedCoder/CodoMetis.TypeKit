using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A <see cref="Patch{T}"/> property in the document: its argument's schema, inlined, required only
/// where the serializer requires it, and admitting null, since the Null state is part of the wire format whatever the
/// argument is.
/// </summary>
/// <remarks>
/// <para>
/// Inlined rather than a <c>PatchOfstring</c> component: <see cref="Apply"/> makes ASP.NET's
/// reference-id hook answer null for a patch, so the schema is written where the property is, and
/// this transformer then fills it from the argument's.
/// </para>
/// <para>
/// A value object that appears only inside a patch has no metadata in a source-generated context,
/// since the patch's converter hides it, and ASP.NET cannot make its schema there (measured under
/// Native AOT in spikes/MergePatch). Its wrapped type's schema is asked for instead, which is what
/// the value-object transformer would have put in.
/// </para>
/// </remarks>
internal sealed class PatchSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>A patch is inlined where it is used, never hoisted into a component of its own.</summary>
    public static void Apply(OpenApiOptions options)
    {
        var inner = options.CreateSchemaReferenceId;
        options.CreateSchemaReferenceId = type => IsPatch(type.Type) ? null : inner(type);
    }

    private static bool IsPatch(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Patch<>);

    /// <summary>
    /// The argument's schema, as it is, with null admitted beside every value it admits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How depends on what restricts the values. A type gains <c>null</c>, which 3.0 writes as
    /// <c>nullable</c>. An enum lists every value there is, so it gains <c>null</c> as a value too:
    /// without it a closed set's <c>"type": ["null", "string"]</c> still refused null, and a string
    /// enum, which has no type, became <c>"type": "null"</c> beside its names and admitted nothing.
    /// A subschema refuses null whatever the type says, so a schema that combines subschemas, such as
    /// a host's <c>oneOf</c> of a string and a number, is kept whole in a branch beside a null one.
    /// (ASP.NET's own polymorphic schema does not need it: its branches state no type.) A schema that
    /// restricts nothing, an <see cref="object"/>'s, admits null as it is; it became
    /// <c>"type": "null"</c> and admitted only null. Every other keyword (<c>required</c>,
    /// <c>pattern</c>, <c>items</c>) applies to its own type only and lets null through.
    /// </para>
    /// </remarks>
    internal static void AdmitNull(OpenApiSchema schema, OpenApiSchema inner)
    {
        if (inner.AnyOf is { Count: > 0 } || inner.OneOf is { Count: > 0 } || inner.AllOf is { Count: > 0 } || inner.Not is not null || inner.Const is not null)
        {
            var value = new OpenApiSchema();
            value.FillFrom(inner);
            schema.AnyOf = [value, new OpenApiSchema { Type = JsonSchemaType.Null }];
            return;
        }

        schema.FillFrom(inner);

        if (schema.Enum is { Count: > 0 } values && !values.Any(value => value is null || value.GetValueKind() == System.Text.Json.JsonValueKind.Null))
            values.Add(null!);

        if (schema.Type is { } type) schema.Type = type | JsonSchemaType.Null;
    }

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;

        // The object holding patch properties: none of them is required, since absent is a state, unless
        // the serializer requires it, as RespectRequiredConstructorParameters does a constructor
        // parameter without a default: then a body without it is refused, and the document says so.
        if (info.Kind == JsonTypeInfoKind.Object && schema.Required is { Count: > 0 } required)
        {
            foreach (var property in info.Properties)
            {
                if (IsPatch(property.PropertyType) && !property.IsRequired) required.Remove(property.Name);
            }
        }

        if (!IsPatch(info.Type)) return;

        var argument = info.Type.GetGenericArguments()[0];
        var described = ValueObjectTypes.WrappedType(argument) ?? argument;
        var inner = await context.GetOrCreateSchemaAsync(described, parameterDescription: null, cancellationToken);

        schema.Properties?.Clear();
        AdmitNull(schema, inner);
        schema.Description = "A JSON Merge Patch field: absent leaves the value as it is, null clears it, a value sets it."
                           + (inner.Description is null ? "" : " " + inner.Description);
    }
}
