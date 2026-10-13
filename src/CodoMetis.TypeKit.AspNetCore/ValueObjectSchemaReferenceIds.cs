using System.Collections.Concurrent;
using Microsoft.AspNetCore.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Names a nested value object's or closed set's component after its whole nesting chain:
/// <c>Shop.Id</c> is <c>ShopId</c>, as its generated companion class is <c>ShopIdExtensions</c>, and
/// <c>Order.Status</c> is <c>OrderStatus</c>. Refuses a document in which such a name is also another
/// type's.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET names a component after the type's simple name, so <c>Shop.Id</c> and <c>Stock.Id</c>
/// shared one component <c>Id</c>, and whichever was described first described both: a stock id
/// wrapping an <see cref="int"/> was documented as a uuid (measured with Microsoft.AspNetCore.OpenApi
/// 10.0.12), and <c>Order.Status</c> and <c>Shipment.Status</c> shared one enum of the first's
/// names. Only ASP.NET's default name is replaced: a name the host's own
/// <see cref="OpenApiOptions.CreateSchemaReferenceId"/> chose, or <see langword="null"/> for a value
/// object it inlines, is kept.
/// </para>
/// <para>
/// The chain's name can be another type's own: <c>Order.Status</c> and a top-level <c>OrderStatus</c>
/// both got <c>OrderStatus</c>, the one component described whichever ASP.NET met first, and the
/// property typed <c>Order.Status</c> referenced the other's names. Nothing at compile time sees it: a
/// closed set has no companion class to collide, and two assemblies never meet there. So a name this
/// package gave that another type claims too, or a name another type got that this package gives, is
/// an <see cref="InvalidOperationException"/> naming both. Two types this package does not rename
/// sharing ASP.NET's name stay ASP.NET's behaviour, as for any two types of one name.
/// </para>
/// <para>
/// The names claimed are kept per <see cref="OpenApiOptions"/> instance, which is per document: two
/// documents are two sets of components. A type's name is the same on every call, so a document
/// generated again, or two generated at once, claim the same names.
/// </para>
/// </remarks>
internal static class ValueObjectSchemaReferenceIds
{
    public static void Apply(OpenApiOptions options)
    {
        var inner = options.CreateSchemaReferenceId;
        var claims = new ConcurrentDictionary<string, Claim>(StringComparer.Ordinal);

        options.CreateSchemaReferenceId = type =>
        {
            if (inner(type) is not { } id) return null;

            var claimant = Nullable.GetUnderlyingType(type.Type) ?? type.Type;
            var renamed = claimant.IsNested
                       && (ValueObjectTypes.IsValueObject(claimant) || ClosedSetSchemaTransformer.Describe(claimant) is not null)
                       && id == OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            if (renamed) id = TypeNames.NestingChain(claimant, "");

            var claim = new Claim(claimant, renamed);
            var first = claims.GetOrAdd(id, claim);
            if (first.Type != claimant && (first.Renamed || renamed))
                throw Collision(id, first, claim);

            return id;
        };
    }

    private static InvalidOperationException Collision(string id, Claim first, Claim second) =>
        new($"Two types are named '{id}' in the OpenAPI document: {first.Describe()} and {second.Describe()}. " +
            "One component would describe both as one of them. Rename one of the types, or give one of them a " +
            $"component name of its own with OpenApiOptions.CreateSchemaReferenceId, set before AddTypeKit().");

    private sealed record Claim(Type Type, bool Renamed)
    {
        public string Describe() =>
            Renamed ? $"{TypeNames.Display(Type)} (named after its nesting chain)" : TypeNames.Display(Type);
    }
}
