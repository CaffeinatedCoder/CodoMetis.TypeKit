namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// A plain value object: it has no rules, so <see cref="From"/> accepts any <typeparamref name="T"/>.
/// </summary>
/// <remarks>
/// Not implemented by hand. CodoMetis.TypeKit.Generators implements it on every type that declares
/// <see cref="IValue{T}"/>. A <see cref="IValidatedValue{TSelf,T,TFault}"/>
/// never implements it: its only factories are the ones that apply its rules.
/// </remarks>
/// <typeparam name="TSelf">The value object type itself.</typeparam>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IPlainValueObject<out TSelf, in T> where TSelf : IValueObject<TSelf, T> where T : notnull
{
    /// <summary>Wraps <paramref name="value"/>.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A value object wrapping <paramref name="value"/>.</returns>
    abstract static TSelf From(T value);
}
