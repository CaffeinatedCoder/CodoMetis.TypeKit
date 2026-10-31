using System.ComponentModel;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Marks an operator of a value object that is the wrapped type's own operator over the wrapped values:
/// inside a relational query, it is translated as the wrapped type's arithmetic over the columns.
/// </summary>
/// <remarks>
/// <para>
/// CodoMetis.TypeKit.Generators puts it on every arithmetic operator it generates, which calls the
/// wrapped type's through <see cref="GeneratedArithmetic"/>: an operand of the value object's type is its
/// <c>Value</c>, any other operand is passed as it is, and a result of the value object's type wraps the
/// wrapped type's result. CodoMetis.TypeKit.EntityFrameworkCore translates the operator so, also where
/// an operand or the result is not the value object (<c>o.Price * 2m</c>, <c>o.Tax / o.Price</c> as a
/// <c>decimal</c>).
/// </para>
/// <para>
/// An operator without it, one written by hand, is refused in a query, naming the value object: EF
/// translates an operator as the database's arithmetic and never runs its body, so a hand-written one
/// that does anything more (rounds, converts, checks) would answer differently there than in memory.
/// </para>
/// <para>
/// Nothing to write by hand: on an operator that does anything but the wrapped type's arithmetic, it
/// breaks that promise only on the database side, where nothing checks it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TranslatedAsWrappedArithmeticAttribute : Attribute;
