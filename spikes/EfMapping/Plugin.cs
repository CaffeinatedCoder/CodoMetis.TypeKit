using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Candidate 2: an additive type-mapping-source plugin. EF allows many plugins, so nothing is
/// replaced. The wrapped type's mapping is fetched lazily, at lookup time, from the (by then
/// constructed) type mapping source, which avoids a cycle at construction.
/// </summary>
public sealed class ValueObjectTypeMappingSourcePlugin(IServiceProvider services) : IRelationalTypeMappingSourcePlugin
{
    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.ClrType is not { } clrType) return null;

        var model = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (ValueObjects.UnderlyingType(model) is not { } valueType) return null;

        var wrapped = services.GetRequiredService<IRelationalTypeMappingSource>().FindMapping(
            valueType, mappingInfo.StoreTypeName, mappingInfo.IsKeyOrIndex, mappingInfo.IsUnicode, mappingInfo.Size,
            mappingInfo.IsRowVersion, mappingInfo.IsFixedLength, mappingInfo.Precision, mappingInfo.Scale);

        return (RelationalTypeMapping?)wrapped?.WithComposedConverter(ValueObjects.Converter(model, valueType));
    }
}

/// <summary>What a strongly-typed-id library commonly registers: its own selector, via ReplaceService.</summary>
public sealed class OtherLibrarySelector(ValueConverterSelectorDependencies dependencies) : ValueConverterSelector(dependencies);
