using System.Collections.Concurrent;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// What <c>UseTypeKit()</c> composes onto the wrapped type's mapping for one value object: its
/// converter, and a JSON reader/writer (primitive collections, JSON columns) built on the wrapped
/// type's.
/// </summary>
/// <remarks>
/// <para>
/// Made per value object through <see cref="GeneratedValueObjectAttribute.Accept{TResult}"/>, which
/// hands over the two types as type arguments, so nothing here needs
/// <see cref="Type.MakeGenericType"/>: Native AOT has no code for an instantiation over a struct that
/// it did not see at compile time.
/// </para>
/// <para>
/// The converter is a plain <see cref="ValueConverter{TModel,TProvider}"/> over the expressions of
/// <see cref="ValueObjectConverter{TValueObject,T}"/>, the type EF's compiled model creates for it: the
/// precompiled queries EF generates cast a property's converter to the type it had at design time,
/// and a subclass there failed that cast at run time (measured with EF Core 10.0.12).
/// </para>
/// <para>
/// The JSON reader/writer is composed here, with both types known, because EF's own composition
/// builds it with <see cref="Type.MakeGenericType"/> and throws under Native AOT, where a precompiled
/// query still maps a value-object parameter at run time.
/// </para>
/// </remarks>
internal abstract class ValueObjectMapping
{
    private static readonly ConcurrentDictionary<Type, ValueObjectMapping> Mappings = new();

    public static ValueObjectMapping For(GeneratedValueObjectAttribute valueObject) =>
        Mappings.GetOrAdd(valueObject.ValueObjectType, static (_, description) => description.Accept(Factory.Instance), valueObject);

    public abstract ValueConverter Converter { get; }

    /// <summary>The value object's reader/writer over the wrapped type's, or <see langword="null"/> where the wrapped type has none.</summary>
    protected abstract JsonValueReaderWriter? ReaderWriterOver(JsonValueReaderWriter? wrapped);

    public RelationalTypeMapping ComposeOnto(RelationalTypeMapping wrapped) =>
        (RelationalTypeMapping)wrapped.WithComposedConverter(Converter, jsonValueReaderWriter: ReaderWriterOver(wrapped.JsonValueReaderWriter));

    private sealed class Of<TValueObject, T> : ValueObjectMapping
        where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
        where T : notnull
    {
        public override ValueConverter Converter { get; } =
            new ValueConverter<TValueObject, T>(ValueObjectConverter<TValueObject, T>.ToProvider, ValueObjectConverter<TValueObject, T>.FromProvider);

        // The wrapped mapping's reader/writer handles values of the wrapped type, converted or not, so
        // it is a JsonValueReaderWriter<T>; anything else is left to EF's own composition.
        protected override JsonValueReaderWriter? ReaderWriterOver(JsonValueReaderWriter? wrapped) =>
            wrapped is JsonValueReaderWriter<T> typed ? new JsonConvertedValueReaderWriter<TValueObject, T>(typed, Converter) : null;
    }

    private sealed class Factory : IValueObjectVisitor<ValueObjectMapping>
    {
        public static readonly Factory Instance = new();

        public ValueObjectMapping Visit<TValueObject, T>()
            where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
            where T : notnull =>
            new Of<TValueObject, T>();
    }
}
