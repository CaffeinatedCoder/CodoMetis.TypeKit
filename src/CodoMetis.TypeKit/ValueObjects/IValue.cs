namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// Declares a value object that wraps any <typeparamref name="T"/>, without validation:
/// <c>public readonly partial record struct OrderId : IValue&lt;Guid&gt;;</c>
/// </summary>
/// <remarks>
/// <para>
/// CodoMetis.TypeKit.Generators generates everything else: the field, the private constructor,
/// <see cref="IValueObject{TValueObject,T}.Value"/>, <see cref="IValueWrapper{TValueObject,T}.From"/>,
/// the JSON converter, parsing, formatting, comparison and the type converter.
/// </para>
/// <para>
/// Without a reference to CodoMetis.TypeKit.Generators nothing is generated, and the analyzer
/// reports CMTK0002 on the declaration.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IValue<T> where T : notnull;
