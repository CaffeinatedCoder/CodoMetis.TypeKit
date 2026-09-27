using System.Collections.Concurrent;
using System.Linq.Expressions;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Stores a value object as the value it wraps, and reads it back <b>without validation</b>.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseTypeKit()</c> applies it to every value-object property by itself, so it only needs to be
/// named to configure a property explicitly.
/// </para>
/// <para>
/// Reading goes through <see cref="IValueObjectMaterializer{TSelf,T}"/>, not <c>Create</c>: the
/// column holds what the application wrote, and a rule added later must not make existing rows
/// unreadable.
/// </para>
/// </remarks>
/// <typeparam name="TValueObject">The value object type.</typeparam>
/// <typeparam name="T">The wrapped type, which is what the column stores.</typeparam>
public sealed class ValueObjectConverter<TValueObject, T> : ValueConverter<TValueObject, T>
    where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
    where T : notnull
{
    private static readonly Expression<Func<TValueObject, T>> ToProvider = BuildToProvider();

    // An expression tree cannot call a static abstract member (CS8927), so it calls a plain generic
    // method that does the constrained call.
    private static readonly Expression<Func<T, TValueObject>> FromProvider = value => Materializer.Create<TValueObject, T>(value);

    /// <summary>Creates the converter.</summary>
    public ValueObjectConverter()
        : this(null)
    {
    }

    /// <summary>Creates the converter with hints for the column the wrapped type maps to.</summary>
    /// <param name="mappingHints">Size, precision, scale or Unicode hints for the column.</param>
    public ValueObjectConverter(ConverterMappingHints? mappingHints)
        : base(ToProvider, FromProvider, mappingHints)
    {
    }

    /// <summary>
    /// <c>instance =&gt; instance.Value</c> through the value object's own public property, rather
    /// than through the interface, which would box a struct.
    /// </summary>
    private static Expression<Func<TValueObject, T>> BuildToProvider()
    {
        var instance = Expression.Parameter(typeof(TValueObject), "instance");
        var value    = typeof(TValueObject).GetProperty(nameof(IValueObject<TValueObject, T>.Value), typeof(T))
                    ?? throw new InvalidOperationException($"{typeof(TValueObject)} has no public Value property of type {typeof(T)}.");

        return Expression.Lambda<Func<TValueObject, T>>(Expression.Property(instance, value), instance);
    }
}

/// <summary>Creates a <see cref="ValueObjectConverter{TValueObject,T}"/> for a type known only at run time.</summary>
internal static class ValueObjectConverter
{
    private static readonly ConcurrentDictionary<(Type ValueObject, Type Value), ValueConverter> Converters = new();

    public static ValueConverter For(Type valueObject, Type value) =>
        Converters.GetOrAdd((valueObject, value), static key =>
            (ValueConverter)Activator.CreateInstance(typeof(ValueObjectConverter<,>).MakeGenericType(key.ValueObject, key.Value))!);
}

/// <summary>The constrained call behind the converter's read path.</summary>
internal static class Materializer
{
    public static TValueObject Create<TValueObject, T>(T value)
        where TValueObject : IValueObjectMaterializer<TValueObject, T>
        where T : notnull =>
        TValueObject.Materialize(value);
}
