using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.ValueGeneration;
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

    /// <summary>
    /// Whether a column converted by <paramref name="converter"/> holds the wrapped value, as the
    /// wrapped type's own mapping (whose converter is <paramref name="wrappedConverter"/>) stores it:
    /// whether the converter is this value object's <see cref="ValueObjectConverter{TValueObject,T}"/>,
    /// alone or composed onto the wrapped type's.
    /// </summary>
    /// <remarks>
    /// Read from the conversion expression, not the converter's type: <c>UseTypeKit()</c> composes a plain
    /// <see cref="ValueConverter{TModel,TProvider}"/>, a compiled model creates its converters again from
    /// the expressions it wrote out, and an application can name <see cref="ValueObjectConverter{TValueObject,T}"/>
    /// itself. Every one of them calls <see cref="ValueObjectConverter{TValueObject,T}.ProviderValue"/>
    /// on the value object and nothing else, before the wrapped type's own conversion, if it has one.
    /// </remarks>
    public abstract bool StoresTheWrappedValue(ValueConverter converter, ValueConverter? wrappedConverter);

    public RelationalTypeMapping ComposeOnto(RelationalTypeMapping wrapped) =>
        (RelationalTypeMapping)wrapped.WithComposedConverter(Converter, jsonValueReaderWriter: ReaderWriterOver(wrapped.JsonValueReaderWriter));

    private sealed class Of<TValueObject, T> : ValueObjectMapping
        where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
        where T : notnull
    {
        public Of() =>
            Converter = new ValueConverter<TValueObject, T>(
                ValueObjectConverter<TValueObject, T>.ToProvider,
                ValueObjectConverter<TValueObject, T>.FromProvider,
                IsTemporaryNumber ? new ConverterMappingHints(valueGeneratorFactory: TemporaryNumber) : null);

        public override ValueConverter Converter { get; }

        /// <summary>
        /// The types EF gives a generated property a temporary number of (<c>RelationalValueGeneratorSelector.FindForType</c>
        /// in EF Core 10), chosen by the property's type mapping: for a value object that is its own, so EF fell back to
        /// the column's type, and a key over <see cref="ulong"/> got a negative temporary <see cref="decimal"/> where the
        /// column is one, which threw <see cref="OverflowException"/> converted back.
        /// </summary>
        private static readonly bool IsTemporaryNumber =
            typeof(T) == typeof(int) || typeof(T) == typeof(long) || typeof(T) == typeof(short) || typeof(T) == typeof(byte)
         || typeof(T) == typeof(char) || typeof(T) == typeof(ulong) || typeof(T) == typeof(uint) || typeof(T) == typeof(ushort)
         || typeof(T) == typeof(sbyte) || typeof(T) == typeof(decimal) || typeof(T) == typeof(float) || typeof(T) == typeof(double);

        /// <summary>
        /// The temporary values a property of the wrapped type gets, as the value object: EF's own where the column is of
        /// the wrapped type, as before, and the provider's for a key of the wrapped type where it is stored as another.
        /// A type mapping's factory, which EF consults after a generator configured on the property and after the
        /// providers' HiLo, and which a mapping built on the converter takes from its hints (decision 114).
        /// </summary>
        private ValueGenerator TemporaryNumber(IProperty property, IEntityType entityType) =>
            property.GetTypeMapping().Converter?.ProviderClrType == typeof(T)
                ? new WrappedTypeValueGenerator(Converter, new TemporaryNumberValueGeneratorFactory().Create(property, entityType))
                : new WrappedTypeValueGenerator(Converter, WrappedTypeValueGenerator.ProviderGenerator<T>);

        private static readonly MethodInfo ProviderValue = ((MethodCallExpression)ValueObjectConverter<TValueObject, T>.ToProvider.Body).Method;

        public override bool StoresTheWrappedValue(ValueConverter converter, ValueConverter? wrappedConverter)
        {
            if (converter.ConvertToProviderExpression is not { Parameters: [var instance] } toProvider || instance.Type != typeof(TValueObject)) return false;

            // The conversion with ProviderValue(instance) taken out is what remains to be applied to the
            // wrapped value: nothing, or exactly the wrapped type's own conversion.
            var wrapped   = Expression.Parameter(typeof(T), "value");
            var unwrapped = new ProviderValueReplacer(instance, wrapped);
            var remaining = unwrapped.Visit(toProvider.Body);

            if (unwrapped.Replaced != 1 || unwrapped.InstanceLeft) return false;

            var expected = wrappedConverter?.ConvertToProviderExpression is { Parameters: [var value] } wrappedToProvider
                               ? ReplacingExpressionVisitor.Replace(value, wrapped, wrappedToProvider.Body)
                               : wrapped;

            return ExpressionEqualityComparer.Instance.Equals(remaining, expected);
        }

        /// <summary>Replaces <c>ProviderValue(instance)</c> with the wrapped value, and notes any other use of the instance.</summary>
        private sealed class ProviderValueReplacer(ParameterExpression instance, ParameterExpression wrapped) : ExpressionVisitor
        {
            public int Replaced { get; private set; }

            public bool InstanceLeft { get; private set; }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Object is null && node.Method == ProviderValue && node.Arguments is [var argument] && argument == instance)
                {
                    Replaced++;
                    return wrapped;
                }

                return base.VisitMethodCall(node);
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == instance) InstanceLeft = true;
                return node;
            }
        }

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
