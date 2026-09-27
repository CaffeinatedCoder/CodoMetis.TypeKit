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
/// Ordering follows the wrapped type's <c>CompareTo</c>, ordinal for a string, so it agrees with
/// equality wherever the wrapped type's own does. A custom wrapped type must keep its <c>CompareTo</c>
/// consistent with its <c>Equals</c>, as any sorted collection already requires of it. To order
/// differently, declare <c>CompareTo(TSelf)</c>: it is kept, and the object overload, the operators
/// and the interfaces are derived from it. Any other hand-written comparison member is CMTK1008.
/// </para>
/// <para>
/// Without a reference to CodoMetis.TypeKit.Generators nothing is generated, and the analyzer
/// reports CMTK0002 on the declaration.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IValue<T> where T : notnull;
