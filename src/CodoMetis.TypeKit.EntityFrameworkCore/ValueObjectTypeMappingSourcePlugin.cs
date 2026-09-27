using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Maps any value object as the type it wraps: the provider's mapping for the wrapped type, with
/// <see cref="ValueObjectConverter{TValueObject,T}"/> composed onto it.
/// </summary>
/// <remarks>
/// <para>
/// EF asks the type mapping source per CLR type wherever it maps one (property discovery, keys,
/// foreign keys, primitive-collection elements, query parameters), and the source asks its plugins
/// first. So every value object in a model is mapped without a list of types or a scan
/// (spikes/EfMapping).
/// </para>
/// <para>
/// A plugin rather than a replaced <c>IValueConverterSelector</c>: plugins are additive, so this
/// coexists with a library that replaces the selector, where two replacements would leave one of
/// them without its mappings.
/// </para>
/// <para>
/// The wrapped type's mapping comes from the type mapping source itself, resolved at lookup time:
/// injecting it would be a cycle, since the source is built from its plugins. The facets of the
/// lookup (store type, size, precision, Unicode, key) are passed on, so a configured column keeps
/// them.
/// </para>
/// </remarks>
internal sealed class ValueObjectTypeMappingSourcePlugin(IServiceProvider services) : IRelationalTypeMappingSourcePlugin
{
    private IRelationalTypeMappingSource? _typeMappingSource;

    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.ClrType is not { } clrType) return null;

        if (ValueObjectTypes.Describe(Nullable.GetUnderlyingType(clrType) ?? clrType) is not { } valueObject) return null;

        _typeMappingSource ??= services.GetRequiredService<IRelationalTypeMappingSource>();

        var wrapped = _typeMappingSource.FindMapping(
            valueObject.WrappedType, mappingInfo.StoreTypeName, mappingInfo.IsKeyOrIndex, mappingInfo.IsUnicode, mappingInfo.Size,
            mappingInfo.IsRowVersion, mappingInfo.IsFixedLength, mappingInfo.Precision, mappingInfo.Scale);

        return wrapped is null ? null : ValueObjectMapping.For(valueObject).ComposeOnto(wrapped);
    }
}
