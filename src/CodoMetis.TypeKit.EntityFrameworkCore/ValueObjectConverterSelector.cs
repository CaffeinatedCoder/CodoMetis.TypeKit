using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Answers "a value object converts to the type it wraps" when EF looks for a conversion for a CLR
/// type it cannot map directly, the way it finds one for an enum.
/// </summary>
/// <remarks>
/// EF asks per type, so every value object in the model is mapped without a list of types or a
/// scan: as a property, a key, a foreign key, an element of a primitive collection, and a query
/// parameter (spikes/EfMapping).
/// </remarks>
internal sealed class ValueObjectConverterSelector(ValueConverterSelectorDependencies dependencies) : ValueConverterSelector(dependencies)
{
    public override IEnumerable<ValueConverterInfo> Select(Type modelClrType, Type? providerClrType = null)
    {
        var model = Nullable.GetUnderlyingType(modelClrType) ?? modelClrType;

        if (ValueObjectTypes.UnderlyingType(model) is { } valueType
         && (providerClrType is null || (Nullable.GetUnderlyingType(providerClrType) ?? providerClrType) == valueType))
        {
            yield return new ValueConverterInfo(model, valueType, _ => ValueObjectConverter.For(model, valueType));
        }

        foreach (var info in base.Select(modelClrType, providerClrType))
            yield return info;
    }
}
