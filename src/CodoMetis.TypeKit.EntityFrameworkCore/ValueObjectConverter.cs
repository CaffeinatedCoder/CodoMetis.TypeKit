using System.ComponentModel;
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
    // Both directions call a public static method of this class. EF's compiled model
    // (dotnet ef dbcontext optimize, which Native AOT requires) writes these expressions out as C# in
    // the application's own assembly, where an internal helper does not compile; and an expression
    // tree cannot call the static abstract Materialize itself (CS8927).
    internal static readonly Expression<Func<TValueObject, T>> ToProvider   = instance => ProviderValue(instance);
    internal static readonly Expression<Func<T, TValueObject>> FromProvider = value => Materialize(value);

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
    /// The value to store: <c>Value</c>, read through the type parameter, so a struct is not boxed.
    /// </summary>
    /// <remarks>Public for the code EF's compiled model generates; nothing else needs to call it.</remarks>
    /// <param name="instance">The value object.</param>
    /// <returns>The value it wraps.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T ProviderValue(TValueObject instance) => instance.Value;

    /// <summary>
    /// The value object over a stored value, <b>without validation</b>
    /// (<see cref="IValueObjectMaterializer{TSelf,T}"/>).
    /// </summary>
    /// <remarks>
    /// Public for the code EF's compiled model generates. It is this converter's read path, which a
    /// public converter already exposes: never call it on input.
    /// </remarks>
    /// <param name="value">A value the application itself stored.</param>
    /// <returns>The value object.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValueObject Materialize(T value) => TValueObject.Materialize(value);
}
