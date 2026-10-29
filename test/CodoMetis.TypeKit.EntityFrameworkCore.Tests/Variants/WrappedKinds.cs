using System.Globalization;
using System.Reflection;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;

/// <summary>How a value object orders: as what it wraps, or by a <c>CompareTo(TSelf)</c> of its own.</summary>
public enum Ordering
{
    Wrapped,

    /// <summary>Declares the comparison seam, descending, so a database that orders the column instead is seen.</summary>
    HandWritten,
}

/// <summary>
/// One type a value object wraps, as the EF satellite meets it: the value object, the type itself, how
/// the value object orders, and three distinct values in ascending order.
/// </summary>
/// <remarks>
/// The values are chosen so that a column read as text orders differently from the type (<c>10</c>
/// before <c>2.25</c>, <c>1.02:00:00</c> before <c>10:00:00</c>): a converter that stores another
/// representation, or a provider that orders the stored text, cannot answer like memory by accident. Text and
/// <c>char</c> mix case (<c>"Bravo"</c> before <c>"alpha"</c> ordinally), and the Guids ascend by their first group
/// and not by their last, so a database that orders text ignoring case, or a <c>uniqueidentifier</c> by its last
/// group (SQL Server), cannot either.
/// </remarks>
public sealed record WrappedKind(string Name, Type ValueObject, Type Wrapped, Ordering Ordering, object[] Values)
{
    public override string ToString() => Name;

    /// <summary>The value object over <paramref name="wrapped"/>, through its generated <c>From</c>.</summary>
    public object Wrap(object wrapped) => ValueObject.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [Wrapped])!.Invoke(null, [wrapped])!;

    /// <summary>The generated <c>GetValue()</c> or <c>ValueOrNull()</c> of the value object's companion class.</summary>
    public MethodInfo Companion(string name) =>
        ValueObject.Assembly.GetType($"{ValueObject.Namespace}.{ValueObject.Name}Extensions")?.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
     ?? throw new InvalidOperationException($"{ValueObject.Name} has no generated {name}()");

    /// <summary>Whether C# compares two values of the wrapped type with <c>&lt;</c> and <c>&gt;</c>.</summary>
    public bool HasComparisonOperators
    {
        get
        {
            try
            {
                System.Linq.Expressions.Expression.GreaterThanOrEqual(System.Linq.Expressions.Expression.Default(Wrapped), System.Linq.Expressions.Expression.Default(Wrapped));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>A key or a value as comparable text: a value object by what it wraps.</summary>
    public static string Text(object? value) => value switch
    {
        null                                                    => "<null>",
        DateTime dateTime                                       => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset                           => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        _ when IsValueObject(value.GetType()) is { } valueProperty => Text(valueProperty.GetValue(value)),
        _                                                       => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>",
    };

    private static PropertyInfo? IsValueObject(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValue<>)) ? type.GetProperty("Value") : null;
}

/// <summary>The wrapped types of the EF catalogue, one value object each.</summary>
/// <remarks>
/// Every integer type EF generates a key of, and <c>byte</c>, which it does not; <c>char</c> and <c>Guid</c>,
/// the two documented exceptions to key parity; the types a key can be and is never generated; and the
/// families a query reads: text, numbers, a flag, dates and times, an enum, and a record class over text and
/// over a number, whose generated operators sort null first. A value object ordered by a
/// <c>CompareTo</c> of its own is a kind too, as a struct over a number and a date and as a record class.
/// </remarks>
public static class WrappedKinds
{
    public static readonly WrappedKind Int32   = new("int", typeof(WrapsInt32), typeof(int), Ordering.Wrapped, [2, 7, 10]);
    public static readonly WrappedKind Int64   = new("long", typeof(WrapsInt64), typeof(long), Ordering.Wrapped, [2L, 7L, 10L]);
    public static readonly WrappedKind Int16   = new("short", typeof(WrapsInt16), typeof(short), Ordering.Wrapped, [(short)2, (short)7, (short)10]);
    public static readonly WrappedKind SByte   = new("sbyte", typeof(WrapsSByte), typeof(sbyte), Ordering.Wrapped, [(sbyte)-3, (sbyte)2, (sbyte)10]);
    public static readonly WrappedKind Byte    = new("byte", typeof(WrapsByte), typeof(byte), Ordering.Wrapped, [(byte)2, (byte)7, (byte)10]);
    public static readonly WrappedKind UInt16  = new("ushort", typeof(WrapsUInt16), typeof(ushort), Ordering.Wrapped, [(ushort)2, (ushort)7, (ushort)10]);
    public static readonly WrappedKind UInt32  = new("uint", typeof(WrapsUInt32), typeof(uint), Ordering.Wrapped, [2u, 7u, 10u]);
    public static readonly WrappedKind UInt64  = new("ulong", typeof(WrapsUInt64), typeof(ulong), Ordering.Wrapped, [2ul, 7ul, 10ul]);
    public static readonly WrappedKind Char    = new("char", typeof(WrapsChar), typeof(char), Ordering.Wrapped, ['B', 'a', 'c']);
    public static readonly WrappedKind Guid    = new("Guid", typeof(WrapsGuid), typeof(Guid), Ordering.Wrapped,
                                                     [System.Guid.Parse("00000001-0000-0000-0000-000000000010"), System.Guid.Parse("00000002-0000-0000-0000-000000000002"), System.Guid.Parse("00000003-0000-0000-0000-000000000007")]);
    public static readonly WrappedKind String  = new("string", typeof(WrapsString), typeof(string), Ordering.Wrapped, ["Bravo", "alpha", "charlie"]);
    public static readonly WrappedKind StringRecordClass = new("string, record class", typeof(WrapsStringRecord), typeof(string), Ordering.Wrapped, ["Bravo", "alpha", "charlie"]);
    public static readonly WrappedKind Int32RecordClass = new("int, record class", typeof(WrapsInt32Record), typeof(int), Ordering.Wrapped, [2, 7, 10]);
    public static readonly WrappedKind Decimal = new("decimal", typeof(WrapsDecimal), typeof(decimal), Ordering.Wrapped, [1.5m, 2.25m, 10m]);
    public static readonly WrappedKind Double  = new("double", typeof(WrapsDouble), typeof(double), Ordering.Wrapped, [1.5, 2.25, 10.0]);
    public static readonly WrappedKind Boolean = new("bool", typeof(WrapsBoolean), typeof(bool), Ordering.Wrapped, [false, true]);
    public static readonly WrappedKind DateTime = new("DateTime", typeof(WrapsDateTime), typeof(DateTime), Ordering.Wrapped,
                                                      [new DateTime(2026, 1, 2), new DateTime(2026, 1, 10), new DateTime(2026, 11, 1)]);
    public static readonly WrappedKind DateOnly = new("DateOnly", typeof(WrapsDateOnly), typeof(DateOnly), Ordering.Wrapped,
                                                      [new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 10), new DateOnly(2026, 11, 1)]);
    public static readonly WrappedKind TimeOnly = new("TimeOnly", typeof(WrapsTimeOnly), typeof(TimeOnly), Ordering.Wrapped,
                                                      [new TimeOnly(2, 0), new TimeOnly(9, 30), new TimeOnly(10, 0)]);
    public static readonly WrappedKind DateTimeOffset = new("DateTimeOffset", typeof(WrapsDateTimeOffset), typeof(DateTimeOffset), Ordering.Wrapped,
                                                            [new DateTimeOffset(2026, 1, 2, 0, 0, 0, System.TimeSpan.Zero), new DateTimeOffset(2026, 1, 10, 0, 0, 0, System.TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, System.TimeSpan.Zero)]);
    public static readonly WrappedKind TimeSpan = new("TimeSpan", typeof(WrapsTimeSpan), typeof(TimeSpan), Ordering.Wrapped,
                                                      [System.TimeSpan.FromHours(2), System.TimeSpan.FromHours(10), System.TimeSpan.FromHours(26)]);
    public static readonly WrappedKind Enum    = new("enum", typeof(WrapsEnum), typeof(DayOfWeek), Ordering.Wrapped, [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);

    public static readonly WrappedKind DescendingInt32 = new("int, own CompareTo", typeof(DescendingInt32), typeof(int), Ordering.HandWritten, [2, 7, 10]);
    public static readonly WrappedKind DescendingDateOnly = new("DateOnly, own CompareTo", typeof(DescendingDateOnly), typeof(DateOnly), Ordering.HandWritten,
                                                                [new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 10), new DateOnly(2026, 11, 1)]);
    public static readonly WrappedKind DescendingStringRecordClass = new("string, record class, own CompareTo", typeof(DescendingStringRecord), typeof(string), Ordering.HandWritten,
                                                                         ["alpha", "bravo", "charlie"]);

    /// <summary>What a key can wrap: every integer type, the two exceptions, and types EF never generates a key of.</summary>
    public static readonly WrappedKind[] Keys = [Int32, Int64, Int16, SByte, Byte, UInt16, UInt32, UInt64, Char, Guid, String, Decimal, DateTime];

    /// <summary>What a query reads: one kind per family, the unsigned and the text-stored ones included, and the three own orderings.</summary>
    public static readonly WrappedKind[] Queries =
    [
        Int32, Int64, Int16, Byte, UInt32, UInt64, Char, Guid, String, StringRecordClass, Int32RecordClass, Decimal, Double, Boolean,
        DateTime, DateOnly, TimeOnly, DateTimeOffset, TimeSpan, Enum,
        DescendingInt32, DescendingDateOnly, DescendingStringRecordClass,
    ];

    public static WrappedKind Named(string name) => Keys.Concat(Queries).First(kind => kind.Name == name);
}

public readonly partial record struct WrapsInt32 : IValue<int>;

public readonly partial record struct WrapsInt64 : IValue<long>;

public readonly partial record struct WrapsInt16 : IValue<short>;

public readonly partial record struct WrapsSByte : IValue<sbyte>;

public readonly partial record struct WrapsByte : IValue<byte>;

public readonly partial record struct WrapsUInt16 : IValue<ushort>;

public readonly partial record struct WrapsUInt32 : IValue<uint>;

public readonly partial record struct WrapsUInt64 : IValue<ulong>;

public readonly partial record struct WrapsChar : IValue<char>;

public readonly partial record struct WrapsGuid : IValue<Guid>;

public readonly partial record struct WrapsString : IValue<string>;

public sealed partial record WrapsStringRecord : IValue<string>;

public sealed partial record WrapsInt32Record : IValue<int>;

public readonly partial record struct WrapsDecimal : IValue<decimal>;

public readonly partial record struct WrapsDouble : IValue<double>;

public readonly partial record struct WrapsBoolean : IValue<bool>;

public readonly partial record struct WrapsDateTime : IValue<DateTime>;

public readonly partial record struct WrapsDateOnly : IValue<DateOnly>;

public readonly partial record struct WrapsTimeOnly : IValue<TimeOnly>;

public readonly partial record struct WrapsDateTimeOffset : IValue<DateTimeOffset>;

public readonly partial record struct WrapsTimeSpan : IValue<TimeSpan>;

public readonly partial record struct WrapsEnum : IValue<DayOfWeek>;

/// <summary>Orders descending by its own <c>CompareTo</c>, as <c>ProbeDescending</c> does.</summary>
public readonly partial record struct DescendingInt32 : IValue<int>
{
    public int CompareTo(DescendingInt32 other) => other.Value.CompareTo(Value);
}

public readonly partial record struct DescendingDateOnly : IValue<DateOnly>
{
    public int CompareTo(DescendingDateOnly other) => other.Value.CompareTo(Value);
}

public sealed partial record DescendingStringRecord : IValue<string>
{
    public int CompareTo(DescendingStringRecord? other) => other is null ? 1 : string.CompareOrdinal(other.Value, Value);
}
