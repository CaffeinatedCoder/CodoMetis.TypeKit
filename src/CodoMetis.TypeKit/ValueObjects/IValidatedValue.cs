namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// Declares a value object that can refuse its input, and says which rule refused it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Create"/> is the only factory written by hand. CodoMetis.TypeKit.Generators generates
/// two more on the implementing type, which is why their signatures are spelled out here:
/// </para>
/// <list type="bullet">
///   <item><c>public static Option&lt;TValueObject&gt; TryFrom(T value)</c>, which is <c>Create(value).ToOption()</c>;</item>
///   <item>
///     <c>public static TValueObject FromKnownGood(T value, string? source = null)</c>, which is
///     <c>KnownGood.OrThrow(Create(value), source)</c>.
///   </item>
/// </list>
/// <para>
/// Every other generated way in (the JSON converter, parsing, the type converter) applies
/// <see cref="Create"/> too. The one exception is <see cref="IValueObjectMaterializer{TSelf,T}"/>,
/// which is for values the application wrote itself.
/// </para>
/// <para>
/// <b>Which factory to call.</b> <see cref="Create"/> when the caller has to tell somebody what to
/// fix. <c>TryFrom</c> when "is it valid" is the whole question. <c>FromKnownGood</c> when the
/// caller owns the input, such as a literal in source or a value it has just produced, so a
/// refusal is the caller's own bug. All three apply the same rules to the same input.
/// </para>
/// <para>
/// <c>FromKnownGood</c> is therefore the one factory that must never see input from outside: it
/// throws. The C# compiler fills in its <c>source</c> parameter with the caller's argument
/// expression, so it is never passed by hand.
/// </para>
/// <para>
/// An implementing type may declare its own <c>TryFrom</c> or <c>FromKnownGood</c>, and then that
/// one is generated no longer. Comparison has one such seam, <c>CompareTo(TSelf)</c>, from which
/// the rest is derived; any other hand-written comparison member is CMTK1008, and ordering
/// otherwise follows the wrapped type, as <see cref="IValue{T}"/> describes.
/// </para>
/// <para>
/// Without a reference to CodoMetis.TypeKit.Generators nothing is generated, and the analyzer
/// reports CMTK0002 on the declaration.
/// </para>
/// </remarks>
/// <typeparam name="TValueObject">The value object type itself.</typeparam>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
/// <typeparam name="TFault">
/// Why a value is refused. Typically an enum declared beside the value object, with one member per
/// rule.
/// </typeparam>
public interface IValidatedValue<TValueObject, in T, TFault>
    where T : notnull
    where TFault : notnull
    where TValueObject : IValidatedValue<TValueObject, T, TFault>
{
    /// <summary>Applies the value object's rules to <paramref name="value"/>.</summary>
    /// <param name="value">The value to validate and wrap.</param>
    /// <returns>The value object, or the fault naming the rule that refused <paramref name="value"/>.</returns>
    abstract static Result<TValueObject, TFault> Create(T value);
}
