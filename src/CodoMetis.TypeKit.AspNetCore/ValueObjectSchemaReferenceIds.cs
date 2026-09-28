using Microsoft.AspNetCore.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Names a nested value object's component after its whole nesting chain: <c>Shop.Id</c> is
/// <c>ShopId</c>, as its generated companion class is <c>ShopIdExtensions</c>.
/// </summary>
/// <remarks>
/// ASP.NET names a component after the type's simple name, so <c>Shop.Id</c> and <c>Stock.Id</c>
/// shared one component <c>Id</c>, and whichever was described first described both: a stock id
/// wrapping an <see cref="int"/> was documented as a uuid (measured with Microsoft.AspNetCore.OpenApi
/// 10.0.12). Only ASP.NET's default name is replaced: a name the host's own
/// <see cref="OpenApiOptions.CreateSchemaReferenceId"/> chose, or <see langword="null"/> for a value
/// object it inlines, is kept.
/// </remarks>
internal static class ValueObjectSchemaReferenceIds
{
    public static void Apply(OpenApiOptions options)
    {
        var inner = options.CreateSchemaReferenceId;

        options.CreateSchemaReferenceId = type =>
        {
            var id = inner(type);

            return id is not null
                && (Nullable.GetUnderlyingType(type.Type) ?? type.Type) is { IsNested: true } valueObject
                && ValueObjectTypes.IsValueObject(valueObject)
                && id == OpenApiOptions.CreateDefaultSchemaReferenceId(type)
                       ? NestingChain(valueObject)
                       : id;
        };
    }

    private static string NestingChain(Type type) =>
        type.DeclaringType is { } declaring ? NestingChain(declaring) + type.Name : type.Name;
}
