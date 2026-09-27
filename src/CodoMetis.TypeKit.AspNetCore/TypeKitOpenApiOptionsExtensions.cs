using Microsoft.AspNetCore.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

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
    /// Value objects are recognised by <see cref="IValueObject{TValueObject,T}"/>. Nothing is
    /// registered per type, and no assembly is scanned. The order against other schema transformers
    /// does not matter.
    /// </para>
    /// </remarks>
    /// <param name="options">The options of one OpenAPI document.</param>
    /// <returns>The same options.</returns>
    public static OpenApiOptions AddTypeKit(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.AddSchemaTransformer<ValueObjectSchemaTransformer>();
    }
}
