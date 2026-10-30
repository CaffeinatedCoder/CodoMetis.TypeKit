using System.Numerics;
using System.Reflection;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Declares its own <c>+</c>, which rounds: it is kept, and so is the checked context's <c>+</c>, which is the
/// same one. <c>-</c> is not declared by hand and is generated.
/// </summary>
public readonly partial record struct RoundedMoney : IValue<decimal>,
    IAdditionOperators<RoundedMoney, RoundedMoney, RoundedMoney>,
    ISubtractionOperators<RoundedMoney, RoundedMoney, RoundedMoney>
{
    public static RoundedMoney operator +(RoundedMoney left, RoundedMoney right) => From(Math.Round(left.Value + right.Value, 2, MidpointRounding.AwayFromZero));
}

/// <summary>Implements <c>+</c> explicitly, as code written for 2.3 could: kept, and no public one is generated beside it.</summary>
public readonly partial record struct ExplicitlyAdded : IValue<int>, IAdditionOperators<ExplicitlyAdded, ExplicitlyAdded, ExplicitlyAdded>
{
    static ExplicitlyAdded IAdditionOperators<ExplicitlyAdded, ExplicitlyAdded, ExplicitlyAdded>.operator +(ExplicitlyAdded left, ExplicitlyAdded right) =>
        From(left.Value + right.Value + 1_000);
}

/// <summary>A base record that implements <c>+</c> for the value object derived from it, explicitly.</summary>
public abstract record AddingBase<TSelf> : IAdditionOperators<TSelf, TSelf, TSelf> where TSelf : AddingBase<TSelf>
{
    static TSelf IAdditionOperators<TSelf, TSelf, TSelf>.operator +(TSelf left, TSelf right) => throw new NotSupportedException(nameof(AddingBase<>));
}

/// <summary>Its <c>+</c> is its base record's, which every caller through the interface reaches: none is generated.</summary>
public sealed partial record InheritsAddition : AddingBase<InheritsAddition>, IValue<int>;

/// <summary>A validated value object with the <c>+</c> it had to write by hand before 2.4, which still compiles.</summary>
public readonly partial record struct Score : IValidatedValue<Score, int, ScoreFault>, IAdditionOperators<Score, Score, Score>
{
    public static Result<Score, ScoreFault> Create(int value) => value is >= 0 and <= 100 ? new Score(value) : Result.Error(ScoreFault.OutOfRange);

    public static Score operator +(Score left, Score right) => FromKnownGood(Math.Min(left.Value + right.Value, 100));
}

public enum ScoreFault { OutOfRange }

/// <summary>
/// A point in time of the tests' own: a later one is a point plus an <see cref="Elapsed"/>, with a checked form
/// of its own, and two points' difference is an <see cref="Elapsed"/>, implemented explicitly. A
/// <see cref="DateTime"/> implements neither interface, so a value object over one gets neither.
/// </summary>
public readonly record struct Tick(long Count) : IAdditionOperators<Tick, Elapsed, Tick>, ISubtractionOperators<Tick, Tick, Elapsed>
{
    public static Tick operator +(Tick left, Elapsed right) => new(unchecked(left.Count + right.Count));

    public static Tick operator checked +(Tick left, Elapsed right) => new(checked(left.Count + right.Count));

    static Elapsed ISubtractionOperators<Tick, Tick, Elapsed>.operator -(Tick left, Tick right) => new(left.Count - right.Count);
}

/// <summary>What lies between two <see cref="Tick"/>s.</summary>
public readonly record struct Elapsed(long Count);

/// <summary>Another type as the operand and as the result, as its wrapped type has them.</summary>
public readonly partial record struct Stamp : IValue<Tick>,
    IAdditionOperators<Stamp, Elapsed, Stamp>,
    ISubtractionOperators<Stamp, Stamp, Elapsed>;

/// <summary>
/// The arithmetic operators a plain value object declares, as a caller writes them: on the type itself, in
/// and out of a checked context, across an assembly boundary, with another operand type; and an operator
/// written by hand, which is kept with its checked form.
/// </summary>
/// <remarks>
/// <see cref="ArithmeticParityTests"/> compares every operator with the wrapped type's through generic code;
/// these call the operators the way most code does, on the concrete type.
/// </remarks>
public sealed class ArithmeticTests
{
    [Fact]
    public void A_declared_operator_is_generated_on_the_type()
    {
        var price = ProbeMoney.From(10.50m);

        (price + ProbeMoney.From(0.25m)).ShouldBe(ProbeMoney.From(10.75m));
        (price - ProbeMoney.From(0.50m)).ShouldBe(ProbeMoney.From(10m));
        (-price).ShouldBe(ProbeMoney.From(-10.50m));
        (+price).ShouldBe(price);
    }

    [Fact]
    public void The_wrapped_type_as_the_right_operand_scales()
    {
        var price = ProbeMoney.From(10m);

        (price * 3m).ShouldBe(ProbeMoney.From(30m));
        (price / 4m).ShouldBe(ProbeMoney.From(2.5m));
        (price % 3m).ShouldBe(ProbeMoney.From(1m));
    }

    [Fact]
    public void A_result_of_the_wrapped_type_is_not_wrapped()
    {
        decimal ratio = ProbeMoney.From(3m) / ProbeMoney.From(4m);

        ratio.ShouldBe(0.75m);
    }

    [Fact]
    public void Another_operand_type_is_the_wrapped_types_own()
    {
        var start = Stamp.From(new Tick(10));

        (start + new Elapsed(5)).Value.ShouldBe(new Tick(15));
        (start + new Elapsed(5) - start).ShouldBe(new Elapsed(5));
        unchecked(Stamp.From(new Tick(long.MaxValue)) + new Elapsed(1)).Value.ShouldBe(new Tick(long.MinValue));
        Should.Throw<OverflowException>(() => checked(Stamp.From(new Tick(long.MaxValue)) + new Elapsed(1)));
    }

    /// <summary>The checked context picks the checked operator, as it does for an <see cref="int"/>.</summary>
    [Fact]
    public void The_checked_context_is_the_wrapped_types()
    {
        var largest = Int32Amount.From(int.MaxValue);
        var one     = Int32Amount.From(1);

        unchecked(largest + one).Value.ShouldBe(int.MinValue);
        Should.Throw<OverflowException>(() => checked(largest + one));
        Should.Throw<OverflowException>(() => checked(-Int32Amount.From(int.MinValue)));
        Should.Throw<DivideByZeroException>(() => one / Int32Amount.From(0));
    }

    /// <summary>Two bytes are added as bytes, as <see cref="IAdditionOperators{TSelf,TOther,TResult}"/> of <see cref="byte"/> adds them.</summary>
    [Fact]
    public void A_small_integer_wraps_around_at_its_own_width()
    {
        unchecked(ByteAmount.From(byte.MaxValue) + ByteAmount.From(1)).Value.ShouldBe((byte)0);
        Should.Throw<OverflowException>(() => checked(ByteAmount.From(byte.MaxValue) + ByteAmount.From(1)));
    }

    [Fact]
    public void A_null_operand_of_a_record_class_is_refused_by_name()
    {
        Int32AmountClass? missing = null;

        Should.Throw<ArgumentNullException>(() => missing! + Int32AmountClass.From(1)).ParamName.ShouldBe("left");
        Should.Throw<ArgumentNullException>(() => Int32AmountClass.From(1) - missing!).ParamName.ShouldBe("right");
        Should.Throw<ArgumentNullException>(() => -missing!).ParamName.ShouldBe("value");
    }

    [Fact]
    public void A_hand_written_operator_is_kept_with_its_checked_form()
    {
        var sum = RoundedMoney.From(0.004m) + RoundedMoney.From(0.001m);

        sum.ShouldBe(RoundedMoney.From(0.01m));
        checked(RoundedMoney.From(0.004m) + RoundedMoney.From(0.001m)).ShouldBe(RoundedMoney.From(0.01m));
        Add(RoundedMoney.From(0.004m), RoundedMoney.From(0.001m)).ShouldBe(RoundedMoney.From(0.01m));
        CheckedAdd(RoundedMoney.From(0.004m), RoundedMoney.From(0.001m)).ShouldBe(RoundedMoney.From(0.01m));
        Operators(typeof(RoundedMoney), "op_Addition").ShouldHaveSingleItem();
        Operators(typeof(RoundedMoney), "op_CheckedAddition").ShouldBeEmpty();
    }

    [Fact]
    public void An_operator_not_written_by_hand_is_generated_beside_one_that_is()
    {
        (RoundedMoney.From(0.004m) - RoundedMoney.From(0.001m)).ShouldBe(RoundedMoney.From(0.003m));
        Operators(typeof(RoundedMoney), "op_CheckedSubtraction").ShouldHaveSingleItem();
    }

    [Fact]
    public void An_explicit_implementation_is_kept_and_nothing_public_is_generated()
    {
        Add(ExplicitlyAdded.From(1), ExplicitlyAdded.From(2)).ShouldBe(ExplicitlyAdded.From(1_003));
        CheckedAdd(ExplicitlyAdded.From(1), ExplicitlyAdded.From(2)).ShouldBe(ExplicitlyAdded.From(1_003));
        Operators(typeof(ExplicitlyAdded), "op_Addition").ShouldBeEmpty();
        Operators(typeof(ExplicitlyAdded), "op_CheckedAddition").ShouldBeEmpty();
    }

    /// <summary>
    /// A public <c>+</c> generated here would not implement the interface, which the base record does: generic
    /// code would reach the base's and <c>a + b</c> the generated one.
    /// </summary>
    [Fact]
    public void A_base_records_explicit_implementation_is_kept()
    {
        Should.Throw<NotSupportedException>(() => Add(InheritsAddition.From(1), InheritsAddition.From(2)));
        Operators(typeof(InheritsAddition), "op_Addition").ShouldBeEmpty();
        Operators(typeof(InheritsAddition), "op_CheckedAddition").ShouldBeEmpty();
    }

    [Fact]
    public void A_validated_value_object_keeps_its_own_operator_and_gets_none()
    {
        (Score.FromKnownGood(70) + Score.FromKnownGood(50)).ShouldBe(Score.FromKnownGood(100));
        Operators(typeof(Score), "op_Addition").ShouldHaveSingleItem();
        Operators(typeof(Score), "op_CheckedAddition").ShouldBeEmpty();
    }

    /// <summary>No interface declared, nothing generated: the arithmetic is opt-in.</summary>
    [Fact]
    public void A_value_object_that_declares_no_operator_interface_gets_none()
    {
        typeof(ProbeCount).GetMethods(BindingFlags.Public | BindingFlags.Static)
                          .Where(method => method.Name is "op_Addition" or "op_Subtraction" or "op_Multiply" or "op_Division" or "op_Modulus" or "op_UnaryNegation" or "op_UnaryPlus")
                          .ShouldBeEmpty();
    }

    private static T Add<T>(T left, T right) where T : IAdditionOperators<T, T, T> => unchecked(left + right);

    private static T CheckedAdd<T>(T left, T right) where T : IAdditionOperators<T, T, T> => checked(left + right);

    private static IEnumerable<MethodInfo> Operators(Type type, string name) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == name);
}
