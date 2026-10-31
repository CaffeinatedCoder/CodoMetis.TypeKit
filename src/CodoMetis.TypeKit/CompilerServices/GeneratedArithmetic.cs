using System.ComponentModel;
using System.Numerics;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Calls the wrapped type's arithmetic operators through a type parameter, for the operators that
/// CodoMetis.TypeKit.Generators generates where a plain value object declares an operator interface.
/// </summary>
/// <remarks>
/// <para>
/// A call through a type parameter constrained to the interface is what the value object promises:
/// the wrapped type's own implementation of that interface. Written as <c>left.Value + right.Value</c>,
/// two <see cref="byte"/>s or two <see cref="char"/>s were added as <see cref="int"/>s, and an operator
/// a type implements explicitly could not be reached at all.
/// </para>
/// <para>
/// Each unchecked operator is <see langword="unchecked"/> and each checked one <see langword="checked"/>
/// here, whatever the calling project's overflow checking: the context picks the operator, as it picks
/// the wrapped type's, and the operator does what the wrapped type's does in it.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedArithmetic
{
    /// <summary><see cref="IAdditionOperators{TSelf, TOther, TResult}"/>' <c>+</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The sum.</returns>
    public static TResult Add<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IAdditionOperators<TSelf, TOther, TResult> =>
        unchecked(left + right);

    /// <summary><see cref="IAdditionOperators{TSelf, TOther, TResult}"/>' <c>checked +</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The sum.</returns>
    public static TResult CheckedAdd<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IAdditionOperators<TSelf, TOther, TResult> =>
        checked(left + right);

    /// <summary><see cref="ISubtractionOperators{TSelf, TOther, TResult}"/>' <c>-</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The difference.</returns>
    public static TResult Subtract<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : ISubtractionOperators<TSelf, TOther, TResult> =>
        unchecked(left - right);

    /// <summary><see cref="ISubtractionOperators{TSelf, TOther, TResult}"/>' <c>checked -</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The difference.</returns>
    public static TResult CheckedSubtract<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : ISubtractionOperators<TSelf, TOther, TResult> =>
        checked(left - right);

    /// <summary><see cref="IMultiplyOperators{TSelf, TOther, TResult}"/>' <c>*</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The product.</returns>
    public static TResult Multiply<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IMultiplyOperators<TSelf, TOther, TResult> =>
        unchecked(left * right);

    /// <summary><see cref="IMultiplyOperators{TSelf, TOther, TResult}"/>' <c>checked *</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The product.</returns>
    public static TResult CheckedMultiply<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IMultiplyOperators<TSelf, TOther, TResult> =>
        checked(left * right);

    /// <summary><see cref="IDivisionOperators{TSelf, TOther, TResult}"/>' <c>/</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The quotient.</returns>
    public static TResult Divide<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IDivisionOperators<TSelf, TOther, TResult> =>
        unchecked(left / right);

    /// <summary><see cref="IDivisionOperators{TSelf, TOther, TResult}"/>' <c>checked /</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The quotient.</returns>
    public static TResult CheckedDivide<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IDivisionOperators<TSelf, TOther, TResult> =>
        checked(left / right);

    /// <summary><see cref="IModulusOperators{TSelf, TOther, TResult}"/>' <c>%</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TOther">The right operand's type.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The remainder.</returns>
    public static TResult Remainder<TSelf, TOther, TResult>(TSelf left, TOther right) where TSelf : IModulusOperators<TSelf, TOther, TResult> =>
        unchecked(left % right);

    /// <summary><see cref="IUnaryNegationOperators{TSelf, TResult}"/>' <c>-</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="value">The operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The negation.</returns>
    public static TResult Negate<TSelf, TResult>(TSelf value) where TSelf : IUnaryNegationOperators<TSelf, TResult> =>
        unchecked(-value);

    /// <summary><see cref="IUnaryNegationOperators{TSelf, TResult}"/>' <c>checked -</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="value">The operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The negation.</returns>
    public static TResult CheckedNegate<TSelf, TResult>(TSelf value) where TSelf : IUnaryNegationOperators<TSelf, TResult> =>
        checked(-value);

    /// <summary><see cref="IUnaryPlusOperators{TSelf, TResult}"/>' <c>+</c> of <typeparamref name="TSelf"/>.</summary>
    /// <param name="value">The operand.</param>
    /// <typeparam name="TSelf">The type whose operator it is.</typeparam>
    /// <typeparam name="TResult">The result's type.</typeparam>
    /// <returns>The operand, as the type's unary plus gives it.</returns>
    public static TResult Plus<TSelf, TResult>(TSelf value) where TSelf : IUnaryPlusOperators<TSelf, TResult> =>
        +value;
}
