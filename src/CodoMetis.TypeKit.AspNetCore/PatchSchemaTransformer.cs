using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A <see cref="Patch{T}"/> property in the document: its argument's schema, inlined, required only
/// where the serializer requires it, and admitting null, since the Null state is part of the wire format whatever the
/// argument is. The same for each element of a list or a dictionary of patches, and for a nullable patch, which the
/// serializer reads as the argument or null but not as a merge-patch field (<see cref="Argument"/>).
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
/// the value-object transformer would have put in. A description of the argument, the host's or a
/// converter's that replaces the generated one, comes before both, as it does for a property of its own.
/// </para>
/// </remarks>
internal sealed class PatchSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>
    /// The argument's elements below a nullable patch's schema, which ASP.NET visits again as the patch: the serializer
    /// gives a <see cref="Nullable{T}"/> an element type, <c>T</c>, and ASP.NET visits <c>items</c> and
    /// <c>additionalProperties</c> as that type. Filled as a patch, a <c>Patch&lt;int[]&gt;?</c>'s numbers admitted null
    /// (KB-057).
    /// </summary>
    private static readonly ConditionalWeakTable<IOpenApiSchema, Type> ArgumentElements = new();

    /// <summary>A patch is inlined where it is used, never hoisted into a component of its own.</summary>
    public static void Apply(OpenApiOptions options)
    {
        var inner = options.CreateSchemaReferenceId;
        options.CreateSchemaReferenceId = type => IsPatch(type.Type) ? null : inner(type);
    }

    private static bool IsPatch(Type type) => Argument(type) is not null;

    /// <summary>
    /// The argument of a <see cref="Patch{T}"/>, or of a nullable one, whose JSON is the same values or null. The serializer
    /// reads a null into a nullable patch as null, never the patch's Null state, as it leaves an absent property: so a
    /// nullable patch admits null without being described as a merge-patch field (KB-057).
    /// </summary>
    private static Type? Argument(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type) is { IsGenericType: true } patch && patch.GetGenericTypeDefinition() == typeof(Patch<>)
            ? patch.GetGenericArguments()[0]
            : null;

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
    /// a host's <c>oneOf</c> of a string and a number or an <c>if</c> whose branches state a type, is kept
    /// whole in a branch beside a null one.
    /// (ASP.NET's own polymorphic schema does not need it: its branches state no type.) A schema that
    /// restricts nothing, an <see cref="object"/>'s, admits null as it is; it became
    /// <c>"type": "null"</c> and admitted only null. Every other keyword (<c>required</c>,
    /// <c>pattern</c>, <c>items</c>) applies to its own type only and lets null through.
    /// </para>
    /// </remarks>
    internal static void AdmitNull(OpenApiSchema schema, OpenApiSchema inner)
    {
        if (inner.AnyOf is { Count: > 0 } || inner.OneOf is { Count: > 0 } || inner.AllOf is { Count: > 0 } || inner.Not is not null
         || inner.If is not null || inner.Then is not null || inner.Else is not null || inner.Const is not null)
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
        // A non-abstract polymorphic base's own branch, which ASP.NET hands to no transformer.
        await PolymorphicBaseBranch.WalkAsync(schema, context, this, cancellationToken);

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

        // A container of patches arrives without its element, as one of value objects does: ASP.NET leaves out
        // items or additionalProperties for a converter-backed element before any transformer runs. The patch's
        // own schema is put back, only where it is missing; asked for, it is filled below, as a property's is.
        // The kind first: the serializer gives a Nullable<T> an element type too, T.
        if (info.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary && info.ElementType is { } element && IsPatch(element))
        {
            if (info.Kind == JsonTypeInfoKind.Enumerable && schema.Items is null)
                schema.Items = await PolymorphicBaseBranch.GetOrCreateSchemaAsync(context, element, parameterDescription: null, cancellationToken);
            else if (info.Kind == JsonTypeInfoKind.Dictionary && schema.AdditionalProperties is null)
                schema.AdditionalProperties = await PolymorphicBaseBranch.GetOrCreateSchemaAsync(context, element, parameterDescription: null, cancellationToken);
            return;
        }

        if (Argument(info.Type) is not { } argument || ArgumentElements.TryGetValue(schema, out _)) return;

        // What the argument is described as comes first, the host's word or a converter's that replaces a
        // value object's generated one, as it does where the argument is a property of its own.
        // The argument is held by the patch alone, where ASP.NET asks no transformer of it: a document and a
        // controller that describe it differently are told apart here.
        DescribedOptionsAgreement.ThrowIfDisagreeing(context.ApplicationServices, argument, info.Options);
        // One the options write with a converter that says nothing is what ASP.NET writes for any such: {}.
        var inner = DescribedSchemaTransformer.Description(argument, info.Options, property: null) is { } description
            ? await DescribedSchemaTransformer.SchemaAsync(description, argument, context, cancellationToken)
            : DescribedSchemaTransformer.Replaced(argument, info.Options)
                ? new OpenApiSchema()
                : await PolymorphicBaseBranch.GetOrCreateSchemaAsync(context, ValueObjectTypes.WrappedType(argument) ?? argument, parameterDescription: null, cancellationToken);

        schema.Properties?.Clear();
        AdmitNull(schema, inner);
        if (Nullable.GetUnderlyingType(info.Type) is not null)
        {
            foreach (var below in new[] { schema.Items, schema.AdditionalProperties })
                if (below is not null) ArgumentElements.AddOrUpdate(below, argument);
            return;
        }

        schema.Description = "A JSON Merge Patch field: absent leaves the value as it is, null clears it, a value sets it."
                           + (inner.Description is null ? "" : " " + inner.Description);
    }
}
