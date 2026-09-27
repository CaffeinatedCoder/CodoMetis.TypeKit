namespace CodoMetis.TypeKit;

/// <summary>A value object that accepts any <typeparamref name="T"/>, so it can be created without validation.</summary>
/// <remarks>
/// Not implemented by hand. CodoMetis.TypeKit.Generators implements it on every type that declares
/// <see cref="ValueObjects.IValue{T}"/>. A <see cref="ValueObjects.IValidatedValue{TValueObject,T,TFault}"/>
/// never implements it: its only factories are the ones that apply its rules.
/// </remarks>
/// <typeparam name="TValueObject">The value object type itself.</typeparam>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IValueWrapper<out TValueObject, in T> where TValueObject : IValueObject<TValueObject, T> where T : notnull
{
    /// <summary>Wraps <paramref name="value"/>.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A value object wrapping <paramref name="value"/>.</returns>
    abstract static TValueObject From(T value);
}
