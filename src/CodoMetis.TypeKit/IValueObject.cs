namespace CodoMetis.TypeKit;

/// <summary>
/// A value object: a type that wraps exactly one <typeparamref name="T"/> and is equal to another
/// instance when the wrapped values are equal.
/// </summary>
/// <remarks>
/// <para>
/// Not implemented by hand. CodoMetis.TypeKit.Generators implements it on every type that declares
/// <see cref="ValueObjects.IValue{T}"/> or <see cref="ValueObjects.IValidatedValue{TValueObject,T,TFault}"/>.
/// </para>
/// <para>
/// Run-time code (the EF Core and OpenAPI satellites, a host's own mapping) recognises a value object
/// by testing assignability to this interface, never by its name or namespace.
/// </para>
/// </remarks>
/// <typeparam name="TValueObject">The value object type itself.</typeparam>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IValueObject<in TValueObject, T> where TValueObject : IValueObject<TValueObject, T> where T : notnull
{
    /// <summary>The wrapped value.</summary>
    T Value { get; }

    /// <summary>Whether two value objects wrap equal values.</summary>
    /// <param name="left">The first value object.</param>
    /// <param name="right">The second value object.</param>
    /// <returns><see langword="true"/> if the wrapped values are equal.</returns>
    abstract static bool operator ==(TValueObject left, TValueObject right);

    /// <summary>Whether two value objects wrap different values.</summary>
    /// <param name="left">The first value object.</param>
    /// <param name="right">The second value object.</param>
    /// <returns><see langword="true"/> if the wrapped values differ.</returns>
    abstract static bool operator !=(TValueObject left, TValueObject right);
}
