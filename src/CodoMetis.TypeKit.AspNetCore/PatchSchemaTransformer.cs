using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A <see cref="Patch{T}"/> property in the document: its argument's schema, inlined, never
/// required, and admitting null, since the Null state is part of the wire format whatever the
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

    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;

        // The object holding patch properties: none of them is required, since absent is a state.
        if (info.Kind == JsonTypeInfoKind.Object && schema.Required is { Count: > 0 } required)
        {
            foreach (var property in info.Properties)
            {
                if (IsPatch(property.PropertyType)) required.Remove(property.Name);
            }
        }

        if (!IsPatch(info.Type)) return;

        var argument = info.Type.GetGenericArguments()[0];
        var described = ValueObjectTypes.WrappedType(argument) ?? argument;
        var inner = await context.GetOrCreateSchemaAsync(described, parameterDescription: null, cancellationToken);

        schema.Properties?.Clear();
        schema.FillFrom(inner);
        schema.Type = (schema.Type ?? JsonSchemaType.Null) | JsonSchemaType.Null;
        schema.Description = "A JSON Merge Patch field: absent leaves the value as it is, null clears it, a value sets it."
                           + (inner.Description is null ? "" : " " + inner.Description);
    }
}
