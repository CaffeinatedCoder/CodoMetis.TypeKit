namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// Declares a value object that wraps any <typeparamref name="T"/>, without validation:
/// <c>public readonly partial record struct OrderId : IValue&lt;Guid&gt;;</c>
/// </summary>
/// <remarks>
/// <para>
/// CodoMetis.TypeKit.Generators generates everything else: the field, the private constructor,
/// <see cref="IValueObject{TSelf,T}.Value"/>, <see cref="IPlainValueObject{TSelf,T}.From"/>,
/// the JSON converter, parsing, formatting, comparison and the type converter. The type therefore
/// declares no constructor of its own, not even a record's parameter list (CMTK1009); another way in
/// is a static method that calls <c>From</c>.
/// </para>
/// <para>
/// Ordering follows the wrapped type's <c>CompareTo</c>, ordinal for a string, so it agrees with
/// equality wherever the wrapped type's own does. A custom wrapped type must keep its <c>CompareTo</c>
/// consistent with its <c>Equals</c>, as any sorted collection already requires of it. To order
/// differently, declare <c>CompareTo(TSelf)</c>: it is kept, and the object overload, the operators
/// and the interfaces are derived from it. Any other hand-written comparison member is CMTK1008.
/// </para>
/// <para>
/// A hand-written <c>ToString()</c> is kept too, and then none of the formatting interfaces is
/// generated, so interpolation, <c>string.Format</c> and <c>Convert.ToString</c> reach it; JSON and the
/// type converter still write the wrapped value. Any other member the generators introduce, written by
/// hand, is CMTK1011, and a value object is declared in one part (CMTK1010).
/// </para>
/// <para>
/// A value object holds its wrapped value and nothing else: an instance field, auto-property,
/// <c>required</c> member or field-like event, declared or inherited, is CMTK1012, since JSON, parsing
/// and the type converter carry the wrapped value alone. Compute anything else from the value.
/// </para>
/// <para>
/// Without a reference to CodoMetis.TypeKit.Generators nothing is generated, and the analyzer
/// reports CMTK0002 on the declaration.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IValue<T> where T : notnull;
