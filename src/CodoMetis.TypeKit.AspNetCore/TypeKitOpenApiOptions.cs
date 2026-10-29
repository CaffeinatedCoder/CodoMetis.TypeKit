namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>How <c>AddTypeKit()</c> describes value objects: <c>options.AddTypeKit(typeKit =&gt; …)</c>.</summary>
public sealed class TypeKitOpenApiOptions
{
    /// <summary>
    /// Whether every value object is described in place, by its wrapped type's schema, rather than as a component
    /// named after it: as a property, a request or response body, a list's or a dictionary's elements, a parameter,
    /// and below any of them. The document then holds no component of a value object, and a nullable one admits null
    /// in its own schema, as a nullable property of the wrapped type does. Off by default: each value object keeps its
    /// component.
    /// </summary>
    /// <remarks>
    /// A closed set and the type a value object wraps (an enum, a record) keep the component ASP.NET gives them. A
    /// <see cref="Microsoft.AspNetCore.OpenApi.OpenApiOptions.CreateSchemaReferenceId"/> of the host's own set before
    /// <c>AddTypeKit</c> still names every other type; one set after it replaces this, as it replaces the naming.
    /// </remarks>
    public bool InlineValueObjects { get; set; }
}
