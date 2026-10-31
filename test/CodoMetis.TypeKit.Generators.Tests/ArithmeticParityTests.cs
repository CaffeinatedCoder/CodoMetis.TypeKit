using System.Globalization;
using System.Numerics;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// The arithmetic operator interfaces, each with the value object as both operands and the result, declared
/// through one interface of the tests' own: the generators read what a value object declares through another
/// interface too.
/// </summary>
public interface IArithmetic<TSelf> :
    IAdditionOperators<TSelf, TSelf, TSelf>,
    ISubtractionOperators<TSelf, TSelf, TSelf>,
    IMultiplyOperators<TSelf, TSelf, TSelf>,
    IDivisionOperators<TSelf, TSelf, TSelf>,
    IModulusOperators<TSelf, TSelf, TSelf>,
    IUnaryNegationOperators<TSelf, TSelf>,
    IUnaryPlusOperators<TSelf, TSelf>
    where TSelf : IArithmetic<TSelf>;

// One value object per numeric wrapped type, a record struct each, and record classes over two of them.
public readonly partial record struct SByteAmount : IValue<sbyte>, IArithmetic<SByteAmount>;
public readonly partial record struct ByteAmount : IValue<byte>, IArithmetic<ByteAmount>;
public readonly partial record struct Int16Amount : IValue<short>, IArithmetic<Int16Amount>;
public readonly partial record struct UInt16Amount : IValue<ushort>, IArithmetic<UInt16Amount>;
public readonly partial record struct Int32Amount : IValue<int>, IArithmetic<Int32Amount>;
public readonly partial record struct UInt32Amount : IValue<uint>, IArithmetic<UInt32Amount>;
public readonly partial record struct Int64Amount : IValue<long>, IArithmetic<Int64Amount>;
public readonly partial record struct UInt64Amount : IValue<ulong>, IArithmetic<UInt64Amount>;
public readonly partial record struct Int128Amount : IValue<Int128>, IArithmetic<Int128Amount>;
public readonly partial record struct UInt128Amount : IValue<UInt128>, IArithmetic<UInt128Amount>;
public readonly partial record struct IntPtrAmount : IValue<nint>, IArithmetic<IntPtrAmount>;
public readonly partial record struct UIntPtrAmount : IValue<nuint>, IArithmetic<UIntPtrAmount>;
public readonly partial record struct BigIntegerAmount : IValue<BigInteger>, IArithmetic<BigIntegerAmount>;

/// <summary><see cref="char"/> implements its arithmetic interfaces explicitly: <c>a + b</c> on two chars is an <see cref="int"/>.</summary>
public readonly partial record struct CharAmount : IValue<char>, IArithmetic<CharAmount>;

public readonly partial record struct HalfAmount : IValue<Half>, IArithmetic<HalfAmount>;
public readonly partial record struct SingleAmount : IValue<float>, IArithmetic<SingleAmount>;
public readonly partial record struct DoubleAmount : IValue<double>, IArithmetic<DoubleAmount>;
public readonly partial record struct DecimalAmount : IValue<decimal>, IArithmetic<DecimalAmount>;

public sealed partial record Int32AmountClass : IValue<int>, IArithmetic<Int32AmountClass>;
public sealed partial record DoubleAmountClass : IValue<double>, IArithmetic<DoubleAmountClass>;

/// <summary>Nested in another type.</summary>
public static class Ledger
{
    public readonly partial record struct Entry : IValue<long>, IArithmetic<Entry>;
}

/// <summary>Internal, with its generated operators public, as a type's own operators must be.</summary>
internal readonly partial record struct InternalAmount : IValue<int>, IArithmetic<InternalAmount>;

/// <summary>
/// Every generated arithmetic operator is the wrapped type's: for each numeric wrapped type, each record shape
/// and each pair of a catalogue of values, the value object's <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>%</c>,
/// unary <c>-</c> and <c>+</c>, unchecked and checked, give what the wrapped type's own give, value or
/// exception.
/// </summary>
/// <remarks>
/// <para>
/// The oracle is the wrapped type's implementation of the same interface, called by the same generic code in
/// the same context. Written as <c>left.Value + right.Value</c>, two <see cref="byte"/>s were added as
/// <see cref="int"/>s and <see cref="char"/>'s explicit operators could not be reached; without its checked
/// form, a value object over <see cref="int"/> wrapped around in a <c>checked</c> context where an
/// <see cref="int"/> throws.
/// </para>
/// <para>
/// The catalogue holds zero, one, minus one where the type is signed, the type's bounds, and for the
/// floating-point types NaN, an infinity and the smallest positive value: the rows where overflow, division by
/// zero and the checked context decide the answer.
/// </para>
/// </remarks>
public sealed class ArithmeticParityTests
{
    public static TheoryData<string> Types => [.. Sweeps.Keys];

    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<string>>> Sweeps = new Dictionary<string, Func<IReadOnlyList<string>>>
    {
        ["sbyte"]             = () => Disagreements<SByteAmount, sbyte>([0, 1, -1, 7, sbyte.MinValue, sbyte.MaxValue]),
        ["byte"]              = () => Disagreements<ByteAmount, byte>([0, 1, 7, byte.MaxValue]),
        ["short"]             = () => Disagreements<Int16Amount, short>([0, 1, -1, 7, short.MinValue, short.MaxValue]),
        ["ushort"]            = () => Disagreements<UInt16Amount, ushort>([0, 1, 7, ushort.MaxValue]),
        ["int"]               = () => Disagreements<Int32Amount, int>([0, 1, -1, 7, int.MinValue, int.MaxValue]),
        ["uint"]              = () => Disagreements<UInt32Amount, uint>([0, 1, 7, uint.MaxValue]),
        ["long"]              = () => Disagreements<Int64Amount, long>([0, 1, -1, 7, long.MinValue, long.MaxValue]),
        ["ulong"]             = () => Disagreements<UInt64Amount, ulong>([0, 1, 7, ulong.MaxValue]),
        ["Int128"]            = () => Disagreements<Int128Amount, Int128>([0, 1, -1, 7, Int128.MinValue, Int128.MaxValue]),
        ["UInt128"]           = () => Disagreements<UInt128Amount, UInt128>([0, 1, 7, UInt128.MaxValue]),
        ["nint"]              = () => Disagreements<IntPtrAmount, nint>([0, 1, -1, 7, nint.MinValue, nint.MaxValue]),
        ["nuint"]             = () => Disagreements<UIntPtrAmount, nuint>([0, 1, 7, nuint.MaxValue]),
        ["BigInteger"]        = () => Disagreements<BigIntegerAmount, BigInteger>([0, 1, -1, 7, BigInteger.Pow(2, 100), -BigInteger.Pow(10, 30)]),
        ["char"]              = () => Disagreements<CharAmount, char>([(char)0, (char)1, 'a', char.MaxValue]),
        ["Half"]              = () => Disagreements<HalfAmount, Half>([Half.Zero, Half.One, Half.NegativeOne, Half.MinValue, Half.MaxValue, Half.Epsilon, Half.NaN, Half.PositiveInfinity]),
        ["float"]             = () => Disagreements<SingleAmount, float>([0, 1, -1, 0.1f, float.MinValue, float.MaxValue, float.Epsilon, float.NaN, float.PositiveInfinity]),
        ["double"]            = () => Disagreements<DoubleAmount, double>([0, 1, -1, 0.1, double.MinValue, double.MaxValue, double.Epsilon, double.NaN, double.PositiveInfinity]),
        ["decimal"]           = () => Disagreements<DecimalAmount, decimal>([0, 1, -1, 0.1m, decimal.MinValue, decimal.MaxValue]),
        ["int, record class"] = () => Disagreements<Int32AmountClass, int>([0, 1, -1, 7, int.MinValue, int.MaxValue]),
        ["double, record class"] = () => Disagreements<DoubleAmountClass, double>([0, 1, -1, double.MaxValue, double.NaN]),
        ["long, nested"]      = () => Disagreements<Ledger.Entry, long>([0, 1, -1, long.MinValue, long.MaxValue]),
        ["int, internal"]     = () => Disagreements<InternalAmount, int>([0, 1, -1, int.MinValue, int.MaxValue]),
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_operator_is_the_wrapped_types(string type)
    {
        var disagreements = Sweeps[type]();

        disagreements.ShouldBeEmpty($"{disagreements.Count} rows of {type} disagree:{Environment.NewLine}{string.Join(Environment.NewLine, disagreements.Take(25))}");
    }

    /// <summary>The floor: a sweep that compared nothing would pass everything.</summary>
    [Fact]
    public void The_sweep_covers_every_numeric_type_and_both_record_shapes() =>
        Sweeps.Count.ShouldBeGreaterThanOrEqualTo(22);

    private static IReadOnlyList<string> Disagreements<TSelf, T>(T[] values)
        where TSelf : IArithmetic<TSelf>, IPlainValueObject<TSelf, T>, IValueObject<TSelf, T>
        where T : notnull, INumberBase<T>, IModulusOperators<T, T, T>
    {
        List<string> found = [];
        var rows = 0;

        void Compare(string operation, Func<TSelf> wrapped, Func<T> unwrapped)
        {
            rows++;
            var expected = Outcome(unwrapped);
            var actual   = Outcome(() => wrapped().Value);

            if (!Equals(expected, actual))
                found.Add($"{typeof(TSelf).Name}: {operation}: the wrapped type gives {Show(expected)}, the value object {Show(actual)}");
        }

        foreach (var a in values)
        {
            var x = TSelf.From(a);

            Compare($"unchecked(-{Show(a)})", () => unchecked(-x), () => unchecked(-a));
            Compare($"checked(-{Show(a)})", () => checked(-x), () => checked(-a));
            Compare($"+{Show(a)}", () => +x, () => +a);

            foreach (var b in values)
            {
                var y = TSelf.From(b);

                Compare($"unchecked({Show(a)} + {Show(b)})", () => unchecked(x + y), () => unchecked(a + b));
                Compare($"checked({Show(a)} + {Show(b)})", () => checked(x + y), () => checked(a + b));
                Compare($"unchecked({Show(a)} - {Show(b)})", () => unchecked(x - y), () => unchecked(a - b));
                Compare($"checked({Show(a)} - {Show(b)})", () => checked(x - y), () => checked(a - b));
                Compare($"unchecked({Show(a)} * {Show(b)})", () => unchecked(x * y), () => unchecked(a * b));
                Compare($"checked({Show(a)} * {Show(b)})", () => checked(x * y), () => checked(a * b));
                Compare($"unchecked({Show(a)} / {Show(b)})", () => unchecked(x / y), () => unchecked(a / b));
                Compare($"checked({Show(a)} / {Show(b)})", () => checked(x / y), () => checked(a / b));
                Compare($"{Show(a)} % {Show(b)}", () => x % y, () => a % b);
            }
        }

        rows.ShouldBe(values.Length * 3 + values.Length * values.Length * 9, typeof(TSelf).Name);
        return found;
    }

    /// <summary>The value, or the type of the exception thrown instead.</summary>
    private static object Outcome<T>(Func<T> operation) where T : notnull
    {
        try
        {
            return operation();
        }
        catch (Exception exception)
        {
            return exception.GetType();
        }
    }

    private static string Show(object value) => value switch
    {
        char c => $"'\\u{(int)c:X4}'",
        Type exception => exception.Name,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
