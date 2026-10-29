using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.OpenApi;

// Beside AddOpenApi, in a namespace every web project imports implicitly, so
// AddOpenApi(options => options.AddTypeKit()) needs no using of its own.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds CodoMetis.TypeKit to an OpenAPI document.</summary>
public static class TypeKitOpenApiOptionsExtensions
{
    /// <summary>
    /// Describes every value object in the document with the schema of the type it wraps, wherever it
    /// appears: as a property, a request or response body, a list or array element, a dictionary
    /// value, and a route, query or header parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wrapped type's schema is the one ASP.NET publishes for that type in this host, so it follows
    /// the host's JSON options (number handling, enum converters) and the document's other schema
    /// transformers, as the generated JSON converter does. An <c>OrderId</c> wrapping a
    /// <see cref="Guid"/> is <c>{"type":"string","format":"uuid"}</c>, and keeps its own component.
    /// </para>
    /// <para>
    /// Value objects are recognised by the attribute the generators put on each. Nothing is
    /// registered per type, and no assembly is scanned. The order against other schema transformers
    /// does not matter.
    /// </para>
    /// <para>
    /// A type whose JSON converter describes itself (<see cref="CodoMetis.TypeKit.IDescribedJsonConverter"/>), such as
    /// <see cref="CodoMetis.TypeKit.DecimalStringJsonConverter"/>, or that the host's JSON options describe
    /// (<see cref="CodoMetis.TypeKit.JsonSchemaDescriptions"/>), is described as that says, in every position, a list's
    /// elements and a dictionary's values included; a parameter of it keeps ASP.NET's own description.
    /// </para>
    /// <para>
    /// An endpoint that returns <c>ToHttpResult()</c> also lists a problem-details response for every
    /// status its fault type is mapped to with <c>AddFaultResponses</c>, and the problem schema those
    /// responses refer to describes the <c>fault</c> member.
    /// </para>
    /// <para>
    /// A nested value object's component is named after its nesting chain (<c>Shop.Id</c> is
    /// <c>ShopId</c>), so two nested value objects of one name do not share a component. A name the
    /// host's own <see cref="OpenApiOptions.CreateSchemaReferenceId"/> gives, set before this call, is
    /// kept; one set after it replaces this naming.
    /// </para>
    /// </remarks>
    /// <param name="options">The options of one OpenAPI document.</param>
    /// <returns>The same options.</returns>
    public static OpenApiOptions AddTypeKit(this OpenApiOptions options) => AddTypeKit(options, static _ => { });

    /// <summary>
    /// Describes every value object in the document with the schema of the type it wraps, as
    /// <see cref="AddTypeKit(OpenApiOptions)"/> does, with the settings <paramref name="configure"/> chooses.
    /// </summary>
    /// <remarks>
    /// With <see cref="TypeKitOpenApiOptions.InlineValueObjects"/>, a value object is written in place wherever it
    /// appears, and no component is named after one. Without a setting, the document is the one
    /// <see cref="AddTypeKit(OpenApiOptions)"/> writes.
    /// </remarks>
    /// <param name="options">The options of one OpenAPI document.</param>
    /// <param name="configure">Chooses the settings.</param>
    /// <returns>The same options.</returns>
    public static OpenApiOptions AddTypeKit(this OpenApiOptions options, Action<TypeKitOpenApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var settings = new TypeKitOpenApiOptions();
        configure(settings);

        ValueObjectSchemaReferenceIds.Apply(options, settings.InlineValueObjects);
        PatchSchemaTransformer.Apply(options);

        return options.AddSchemaTransformer<ValueObjectSchemaTransformer>()
                      .AddSchemaTransformer<PatchSchemaTransformer>()
                      .AddSchemaTransformer<ClosedSetSchemaTransformer>()
                      .AddSchemaTransformer<DescribedSchemaTransformer>()   // after the two above: the host's description replaces theirs
                      .AddOperationTransformer<FaultResponsesOperationTransformer>();
    }
}
