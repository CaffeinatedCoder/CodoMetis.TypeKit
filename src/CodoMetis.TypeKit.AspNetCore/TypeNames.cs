namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// How the satellite names a type: after its nesting chain, as the generators name a nested value
/// object's companion class (<c>ShopIdExtensions</c>) and a nested closed set's
/// <c>QualifiedName</c> (<c>Order.Status.Draft</c>).
/// </summary>
internal static class TypeNames
{
    /// <summary>
    /// The type's name after its containing types, joined by <paramref name="separator"/>, without a
    /// generic arity: <c>Order.Status</c> is <c>OrderStatus</c> with <c>""</c>, and <c>Order.Status</c>
    /// with <c>"."</c>.
    /// </summary>
    public static string NestingChain(Type type, string separator) =>
        type.DeclaringType is { } declaring ? NestingChain(declaring, separator) + separator + Simple(type) : Simple(type);

    /// <summary>The type's namespace and nesting chain, as a message names it.</summary>
    public static string Display(Type type) =>
        type.Namespace is { Length: > 0 } space ? $"{space}.{NestingChain(type, ".")}" : NestingChain(type, ".");

    private static string Simple(Type type) =>
        type.Name.IndexOf('`') is var tick and >= 0 ? type.Name[..tick] : type.Name;
}
