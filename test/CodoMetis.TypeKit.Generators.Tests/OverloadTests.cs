using System.Text;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

// What a value object or a closed set declares beside a generated member: a method of the same name and
// parameter types that C# still tells apart, by its type parameters or by a parameter's ref kind. Each was
// refused as the generated member written by hand (CMTK1011, #226); each is another method, and the generated
// one sits beside it. A ref kind against out (ref, in), and nullability, are no overload, and stay refused
// (BuildOutcomeTests). Every hand-written overload here throws, so a call that reached one fails.

/// <summary>Picks a hand-written generic overload.</summary>
public sealed class OverloadFormat;

/// <summary>A generic <c>From</c>.</summary>
public readonly partial record struct FromOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static FromOverloadedByArity From<TFormat>(int value) => throw new NotSupportedException("hand-written");
}

/// <summary>A <c>From</c> by <c>in</c>.</summary>
public readonly partial record struct FromOverloadedByIn : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static FromOverloadedByIn From(in int value) => throw new NotSupportedException("hand-written");
}

/// <summary>A <c>From</c> by <c>ref</c>.</summary>
public readonly partial record struct FromOverloadedByRef : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static FromOverloadedByRef From(ref int value) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>Parse</c> over text, the issue's scenario.</summary>
public readonly partial record struct ParseOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static ParseOverloadedByArity Parse<TFormat>(string text, IFormatProvider? provider) where TFormat : class => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>TryParse</c> over text.</summary>
public readonly partial record struct TryParseOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static bool TryParse<TFormat>(string? text, IFormatProvider? provider, out TryParseOverloadedByArity result) => throw new NotSupportedException("hand-written");
}

/// <summary>A <c>TryParse</c> over text whose last parameter is by value.</summary>
public readonly partial record struct TryParseOverloadedByValue : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static bool TryParse(string? text, IFormatProvider? provider, TryParseOverloadedByValue fallback) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>Parse</c> and a by-value <c>TryParse</c> over a span.</summary>
public readonly partial record struct SpanParseOverloaded : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static SpanParseOverloaded Parse<TFormat>(ReadOnlySpan<char> text, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, IFormatProvider? provider, SpanParseOverloaded fallback) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>Parse</c> and a by-value <c>TryParse</c> over UTF-8.</summary>
public readonly partial record struct Utf8ParseOverloaded : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static Utf8ParseOverloaded Parse<TFormat>(ReadOnlySpan<byte> text, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(ReadOnlySpan<byte> text, IFormatProvider? provider, Utf8ParseOverloaded fallback) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>ToString(string?, IFormatProvider?)</c>.</summary>
public readonly partial record struct FormatOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public string ToString<TFormat>(string? format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");
}

/// <summary>A <c>ToString(string?, IFormatProvider?)</c> whose format is by <c>in</c>.</summary>
public readonly partial record struct FormatOverloadedByIn : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public string ToString(in string? format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>TryFormat</c>, and one whose count is by value, for both the generated <c>TryFormat</c>s.</summary>
public readonly partial record struct TryFormatOverloaded : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public bool TryFormat<TFormat>(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public bool TryFormat(Span<char> destination, int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public bool TryFormat(Span<byte> destination, int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");
}

/// <summary>A record class with a generic <c>Parse</c>.</summary>
public sealed partial record ParseOverloadedInARecordClass : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public static ParseOverloadedInARecordClass Parse<TFormat>(string text, IFormatProvider? provider) => throw new NotSupportedException("hand-written");
}

/// <summary>A base record in this assembly whose methods are overloads of the generated ones.</summary>
public abstract record OverloadingBase<TSelf>
{
    /// <summary>Hand-written.</summary>
    public static TSelf From<TFormat>(int value) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static TSelf From(in int value) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static TSelf Parse<TFormat>(string text, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(string? text, IFormatProvider? provider, TSelf fallback) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static TSelf Parse<TFormat>(ReadOnlySpan<char> text, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(ReadOnlySpan<byte> text, IFormatProvider? provider, TSelf fallback) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public string ToString<TFormat>(string? format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public bool TryFormat<TFormat>(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => throw new NotSupportedException("hand-written");
}

/// <summary>Over a base record in this assembly with an overload of each generated member.</summary>
public sealed partial record OverAnOverloadingBase : OverloadingBase<OverAnOverloadingBase>, IValue<int>;

/// <summary>Over a base record in another assembly with overloads of <c>Parse</c>, <c>From</c> and <c>TryParse</c>.</summary>
public sealed partial record OverALibraryOverloadingBase : ProbeOverloadingRecord<OverALibraryOverloadingBase>, IValue<int>;

/// <summary>A closed set with a generic <c>Parse</c> of each form, a generic <c>TryParse</c>, and by-value ones.</summary>
public sealed partial class OverloadingSet : IClosedSet<OverloadingSet>
{
    /// <summary>The first member.</summary>
    public static readonly OverloadingSet First = new();

    /// <summary>The second member.</summary>
    public static readonly OverloadingSet Second = new();

    /// <summary>Hand-written.</summary>
    public static OverloadingSet Parse<TFormat>(string name) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static OverloadingSet Parse<TFormat>(string name, bool ignoreCase) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse<TFormat>(string? name, out OverloadingSet member) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(string? name, OverloadingSet fallback) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(string? name, bool ignoreCase, OverloadingSet fallback) => throw new NotSupportedException("hand-written");
}

/// <summary>A base class of a closed set whose <c>Parse</c> and <c>TryParse</c> are overloads of the generated ones.</summary>
public abstract class OverloadingSetBase<TSelf>
{
    /// <summary>Hand-written.</summary>
    public static TSelf Parse<TFormat>(string name) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static bool TryParse(string? name, bool ignoreCase, TSelf fallback) => throw new NotSupportedException("hand-written");
}

/// <summary>A closed set over a base class with overloads of the generated <c>Parse</c> and <c>TryParse</c>.</summary>
public sealed partial class SetOverAnOverloadingBase : OverloadingSetBase<SetOverAnOverloadingBase>, IClosedSet<SetOverAnOverloadingBase>
{
    /// <summary>The first member.</summary>
    public static readonly SetOverAnOverloadingBase First = new();

    /// <summary>The second member.</summary>
    public static readonly SetOverAnOverloadingBase Second = new();
}

// The seams and the members a record synthesizes, by the same rule: a generic or by-reference overload of
// ToString(), CompareTo, TryFrom, Revalidate, Equals, GetHashCode or PrintMembers is another method. A generic
// ToString() was taken for the seam, and the value object printed what a record prints; a generic or by-in
// CompareTo(TSelf) for the ordering seam, which marks the ordering hand-written; the rest were refused.

/// <summary>A fault for the validated value objects below.</summary>
public enum OverloadFault
{
    /// <summary>Below zero.</summary>
    Negative
}

/// <summary>A generic <c>ToString()</c>, which is no seam.</summary>
public readonly partial record struct ToStringOverloadedByArity : IValue<string>
{
    /// <summary>Hand-written.</summary>
    public string ToString<TFormat>() => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>CompareTo(TSelf)</c>, which is no seam.</summary>
public readonly partial record struct CompareToOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public int CompareTo<TFormat>(CompareToOverloadedByArity other) => throw new NotSupportedException("hand-written");
}

/// <summary>A <c>CompareTo(TSelf)</c> by <c>in</c>, which is no seam.</summary>
public readonly partial record struct CompareToOverloadedByIn : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public int CompareTo(in CompareToOverloadedByIn other) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>CompareTo(object?)</c> and one by <c>in</c>.</summary>
public readonly partial record struct ObjectCompareToOverloaded : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public int CompareTo<TFormat>(object? other) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public int CompareTo(in object? other) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>Equals(TSelf)</c> and <c>GetHashCode()</c>, which are not its equality.</summary>
public readonly partial record struct EqualityOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public bool Equals<TFormat>(EqualityOverloadedByArity other) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public int GetHashCode<TFormat>() => throw new NotSupportedException("hand-written");
}

/// <summary>A record class with a generic <c>PrintMembers</c>, which its synthesized <c>ToString()</c> never calls.</summary>
public sealed partial record PrintMembersOverloadedByArity : IValue<int>
{
    /// <summary>Hand-written.</summary>
    public bool PrintMembers<TFormat>(StringBuilder builder) => throw new NotSupportedException("hand-written");
}

/// <summary>A generic <c>TryFrom</c> and one by <c>in</c>, beside the generated <c>TryFrom(int)</c>.</summary>
public readonly partial record struct TryFromOverloaded : IValidatedValue<TryFromOverloaded, int, OverloadFault>
{
    /// <summary>Zero or more.</summary>
    public static Result<TryFromOverloaded, OverloadFault> Create(int value) => value < 0 ? Result.Error(OverloadFault.Negative) : new TryFromOverloaded(value);

    /// <summary>Hand-written.</summary>
    public static Option<TryFromOverloaded> TryFrom<TFormat>(int value) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static Option<TryFromOverloaded> TryFrom(in int value) => throw new NotSupportedException("hand-written");
}

/// <summary>A base record whose seams, equality and <c>ToString()</c> are overloads of the generated ones.</summary>
public abstract record SeamOverloadingBase<TSelf> where TSelf : notnull
{
    /// <summary>Hand-written.</summary>
    public static Option<TSelf> TryFrom<TFormat>(int value) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public static Option<TSelf> TryFrom(in int value) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public Result<TSelf, OverloadFault> Revalidate<TFormat>() => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public int CompareTo<TFormat>(object? other) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public int CompareTo<TFormat>(TSelf? other) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public string ToString<TFormat>() => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public bool Equals<TFormat>(SeamOverloadingBase<TSelf>? other) => throw new NotSupportedException("hand-written");

    /// <summary>Hand-written.</summary>
    public int GetHashCode<TFormat>() => throw new NotSupportedException("hand-written");
}

/// <summary>A plain value object over a base record whose seams are overloads.</summary>
public sealed partial record PlainOverASeamOverloadingBase : SeamOverloadingBase<PlainOverASeamOverloadingBase>, IValue<int>;

/// <summary>A validated value object over a base record whose <c>TryFrom</c> and <c>Revalidate</c> are overloads.</summary>
public sealed partial record ValidatedOverASeamOverloadingBase : SeamOverloadingBase<ValidatedOverASeamOverloadingBase>, IValidatedValue<ValidatedOverASeamOverloadingBase, int, OverloadFault>
{
    /// <summary>Zero or more.</summary>
    public static Result<ValidatedOverASeamOverloadingBase, OverloadFault> Create(int value) => value < 0 ? Result.Error(OverloadFault.Negative) : new ValidatedOverASeamOverloadingBase(value);
}

// CMTK1023 names the base's equality, which the generated one does not call.
#pragma warning disable CMTK1023
/// <summary>
/// Two levels above an explicit <c>IEquatable</c> by reference, with a generic <c>Equals</c> of that base's type: the
/// value object implements the base's interface again all the same, since the generic one implements nothing.
/// </summary>
public sealed partial record EqualsOverloadOverADeeperExplicitEquality : LocalBetweenRecord, IValue<int>
{
    /// <summary>Hand-written.</summary>
    public bool Equals<TFormat>(LocalExplicitEqualityRecord? other) => throw new NotSupportedException("hand-written");
}
#pragma warning restore CMTK1023

/// <summary>A closed set with a generic <c>ToString()</c>, which is no seam.</summary>
public sealed partial class SetWithAGenericToString : IClosedSet<SetWithAGenericToString>
{
    /// <summary>The first member.</summary>
    public static readonly SetWithAGenericToString First = new();

    /// <summary>Hand-written.</summary>
    public string ToString<TFormat>() => throw new NotSupportedException("hand-written");
}

/// <summary>
/// The generated members beside a hand-written overload of each, on the type, on a base record of this assembly
/// and of another, and on a closed set and its base class (#226). Refusals are build errors and live in
/// <see cref="BuildOutcomeTests"/>.
/// </summary>
public sealed class OverloadTests
{
    public static TheoryData<string, Func<object>, object> Generated => new()
    {
        { "From<T>(int)", () => FromOverloadedByArity.From(7).Value, 7 },
        { "From(in int)", () => FromOverloadedByIn.From(7).Value, 7 },
        { "From(ref int)", () => FromOverloadedByRef.From(7).Value, 7 },
        { "Parse<T>(string, IFormatProvider?)", () => ParseOverloadedByArity.Parse("7", null).Value, 7 },
        { "TryParse<T>(string?, IFormatProvider?, out T)", () => TryParseOverloadedByArity.TryParse("7", null, out var parsed) ? parsed.Value : -1, 7 },
        { "TryParse(string?, IFormatProvider?, T)", () => TryParseOverloadedByValue.TryParse("7", null, out var parsed) ? parsed.Value : -1, 7 },
        { "Parse<T>(ReadOnlySpan<char>, IFormatProvider?)", () => SpanParseOverloaded.Parse("7".AsSpan(), null).Value, 7 },
        { "TryParse(ReadOnlySpan<char>, IFormatProvider?, T)", () => SpanParseOverloaded.TryParse("7".AsSpan(), null, out var parsed) ? parsed.Value : -1, 7 },
        { "Parse<T>(ReadOnlySpan<byte>, IFormatProvider?)", () => Utf8ParseOverloaded.Parse("7"u8, null).Value, 7 },
        { "TryParse(ReadOnlySpan<byte>, IFormatProvider?, T)", () => Utf8ParseOverloaded.TryParse("7"u8, null, out var parsed) ? parsed.Value : -1, 7 },
        { "IParsable<T>.Parse beside Parse<T>", () => ParseThroughInterface<ParseOverloadedByArity>("7"), ParseOverloadedByArity.From(7) },
        { "ToString<T>(string?, IFormatProvider?)", () => FormatOverloadedByArity.From(7).ToString("D3", null), "007" },
        { "ToString(in string?, IFormatProvider?)", () => FormatOverloadedByIn.From(7).ToString("D3", null), "007" },
        { "interpolation beside ToString<T>(string?, IFormatProvider?)", () => $"{FormatOverloadedByArity.From(7):D3}", "007" },
        { "TryFormat<T>(Span<char>, out int, ...) and TryFormat(Span<char>, int, ...)", () => Format(TryFormatOverloaded.From(7)), "007" },
        { "TryFormat(Span<byte>, int, ...)", () => FormatUtf8(TryFormatOverloaded.From(7)), "007" },
        { "a record class's Parse<T>(string, IFormatProvider?)", () => ParseOverloadedInARecordClass.Parse("7", null).Value, 7 },
        { "a base record's From<T>(int) and From(in int)", () => OverAnOverloadingBase.From(7).Value, 7 },
        { "a base record's Parse<T>(string, IFormatProvider?)", () => OverAnOverloadingBase.Parse("7", null).Value, 7 },
        { "a base record's TryParse(string?, IFormatProvider?, T)", () => OverAnOverloadingBase.TryParse("7", null, out var parsed) ? parsed.Value : -1, 7 },
        { "a base record's Parse<T>(ReadOnlySpan<char>, IFormatProvider?)", () => OverAnOverloadingBase.Parse("7".AsSpan(), null).Value, 7 },
        { "a base record's TryParse(ReadOnlySpan<byte>, IFormatProvider?, T)", () => OverAnOverloadingBase.TryParse("7"u8, null, out var parsed) ? parsed.Value : -1, 7 },
        { "a base record's ToString<T>(string?, IFormatProvider?)", () => OverAnOverloadingBase.From(7).ToString("D3", null), "007" },
        { "a base record's TryFormat<T>(Span<char>, out int, ...)", () => Format(OverAnOverloadingBase.From(7)), "007" },
        { "another assembly's From(in int)", () => OverALibraryOverloadingBase.From(7).Value, 7 },
        { "another assembly's Parse<T>(string, IFormatProvider?)", () => OverALibraryOverloadingBase.Parse("7", null).Value, 7 },
        { "another assembly's TryParse(string?, IFormatProvider?, T)", () => OverALibraryOverloadingBase.TryParse("7", null, out var parsed) ? parsed.Value : -1, 7 },
        { "a set's Parse<T>(string)", () => OverloadingSet.Parse("Second"), OverloadingSet.Second },
        { "a set's Parse<T>(string, bool)", () => OverloadingSet.Parse("second", ignoreCase: true), OverloadingSet.Second },
        { "a set's TryParse<T>(string?, out T) and TryParse(string?, T)", () => OverloadingSet.TryParse("Second", out var member) ? member : OverloadingSet.First, OverloadingSet.Second },
        { "a set's TryParse(string?, bool, T)", () => OverloadingSet.TryParse("second", ignoreCase: true, out var member) ? member : OverloadingSet.First, OverloadingSet.Second },
        { "a set's IParsable<T>.Parse beside Parse<T>(string)", () => ParseThroughInterface<OverloadingSet>("Second"), OverloadingSet.Second },
        { "a set's base class's Parse<T>(string)", () => SetOverAnOverloadingBase.Parse("Second"), SetOverAnOverloadingBase.Second },
        { "a set's base class's TryParse(string?, bool, T)", () => SetOverAnOverloadingBase.TryParse("second", ignoreCase: true, out var member) ? member : SetOverAnOverloadingBase.First, SetOverAnOverloadingBase.Second },
        { "ToString() beside ToString<T>()", () => ToStringOverloadedByArity.From("a").ToString(), "a" },
        { "interpolation beside ToString<T>()", () => $"{ToStringOverloadedByArity.From("a")}", "a" },
        { "CompareTo(T) beside CompareTo<T>(T)", () => Math.Sign(CompareToOverloadedByArity.From(1).CompareTo(CompareToOverloadedByArity.From(2))), -1 },
        { "IComparable<T> beside CompareTo<T>(T)", () => Math.Sign(((IComparable<CompareToOverloadedByArity>)CompareToOverloadedByArity.From(1)).CompareTo(CompareToOverloadedByArity.From(2))), -1 },
        { "CompareTo(T) beside CompareTo(in T)", () => Math.Sign(CompareToOverloadedByIn.From(1).CompareTo(CompareToOverloadedByIn.From(2))), -1 },
        { "IComparable<T> beside CompareTo(in T)", () => Math.Sign(((IComparable<CompareToOverloadedByIn>)CompareToOverloadedByIn.From(1)).CompareTo(CompareToOverloadedByIn.From(2))), -1 },
        { "CompareTo(object) beside CompareTo<T>(object?) and CompareTo(in object?)", () => Math.Sign(((IComparable)ObjectCompareToOverloaded.From(1)).CompareTo(ObjectCompareToOverloaded.From(2))), -1 },
        { "< beside CompareTo<T>(object?)", () => ObjectCompareToOverloaded.From(1) < ObjectCompareToOverloaded.From(2), true },
        { "Equals(T) beside Equals<T>(T)", () => EqualityOverloadedByArity.From(1).Equals(EqualityOverloadedByArity.From(1)), true },
        { "GetHashCode() beside GetHashCode<T>()", () => EqualityOverloadedByArity.From(1).GetHashCode() == EqualityOverloadedByArity.From(1).GetHashCode(), true },
        { "ToString() beside PrintMembers<T>(StringBuilder)", () => PrintMembersOverloadedByArity.From(1).ToString(), "1" },
        { "TryFrom(int) beside TryFrom<T>(int) and TryFrom(in int)", () => TryFromOverloaded.TryFrom(1).IsSome() && TryFromOverloaded.TryFrom(-1).IsNone(), true },
        { "a base record's TryFrom<T>(int) and TryFrom(in int)", () => ValidatedOverASeamOverloadingBase.TryFrom(1).IsSome() && ValidatedOverASeamOverloadingBase.TryFrom(-1).IsNone(), true },
        { "a base record's Revalidate<T>()", () => ValidatedOverASeamOverloadingBase.FromKnownGood(1).Revalidate().TryGetValue(out _, out _), true },
        { "a base record's CompareTo<T>(object?) and CompareTo<T>(TSelf?)", () => PlainOverASeamOverloadingBase.From(1) < PlainOverASeamOverloadingBase.From(2), true },
        { "a base record's ToString<T>()", () => PlainOverASeamOverloadingBase.From(1).ToString(), "1" },
        { "a base record's Equals<T>(TBase?) and GetHashCode<T>()", () => new HashSet<SeamOverloadingBase<PlainOverASeamOverloadingBase>> { PlainOverASeamOverloadingBase.From(1), PlainOverASeamOverloadingBase.From(1) }.Count, 1 },
        { "a deeper base's IEquatable beside Equals<T>(TBase?)", () => new HashSet<LocalExplicitEqualityRecord> { EqualsOverloadOverADeeperExplicitEquality.From(42), EqualsOverloadOverADeeperExplicitEquality.From(42) }.Count, 1 },
        { "a set's ToString() beside ToString<T>()", () => $"{SetWithAGenericToString.First}", "First" },
    };

    /// <summary>
    /// Each generated member is there beside the hand-written overload, and a call that names neither type arguments
    /// nor a ref kind reaches the generated one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Generated))]
    public void The_generated_member_sits_beside_a_hand_written_overload(string overload, Func<object> generated, object expected) =>
        generated().ShouldBe(expected, overload);

    public static TheoryData<string, Action> HandWritten => new()
    {
        { "From<T>(int)", () => FromOverloadedByArity.From<OverloadFormat>(7) },
        { "From(in int)", () => FromOverloadedByIn.From(in Seven) },
        { "Parse<T>(string, IFormatProvider?)", () => ParseOverloadedByArity.Parse<OverloadFormat>("7", null) },
        { "TryParse(string?, IFormatProvider?, T)", () => TryParseOverloadedByValue.TryParse("7", null, TryParseOverloadedByValue.From(1)) },
        { "ToString<T>(string?, IFormatProvider?)", () => FormatOverloadedByArity.From(7).ToString<OverloadFormat>("D3", null) },
        { "TryFormat(Span<char>, int, ...)", () => TryFormatOverloaded.From(7).TryFormat(new char[8], 0, "", null) },
        { "a base record's Parse<T>(string, IFormatProvider?)", () => OverAnOverloadingBase.Parse<OverloadFormat>("7", null) },
        { "another assembly's From(in int)", () => OverALibraryOverloadingBase.From(in Seven) },
        { "a set's Parse<T>(string)", () => OverloadingSet.Parse<OverloadFormat>("Second") },
        { "a set's TryParse(string?, T)", () => OverloadingSet.TryParse("Second", OverloadingSet.First) },
        { "ToString<T>()", () => ToStringOverloadedByArity.From("a").ToString<OverloadFormat>() },
        { "CompareTo(in T)", () => CompareToOverloadedByIn.From(1).CompareTo(in OneByIn) },
        { "TryFrom<T>(int)", () => _ = TryFromOverloaded.TryFrom<OverloadFormat>(1) },
        { "a set's ToString<T>()", () => SetWithAGenericToString.First.ToString<OverloadFormat>() },
    };

    /// <summary>The hand-written overload is kept, and a call that names its type arguments or its ref kind reaches it.</summary>
    [Theory]
    [MemberData(nameof(HandWritten))]
    public void The_hand_written_overload_is_kept(string overload, Action handWritten) =>
        Should.Throw<NotSupportedException>(handWritten, overload).Message.ShouldBe("hand-written");

    public static TheoryData<Type> NotHandWrittenOrdering => [typeof(CompareToOverloadedByArity), typeof(CompareToOverloadedByIn), typeof(PlainOverASeamOverloadingBase)];

    /// <summary>
    /// A generic or by-in <c>CompareTo(TSelf)</c> is no seam, so the ordering is the generated one and is not marked
    /// hand-written: the EF Core satellite refuses to order by a column whose value object is marked.
    /// </summary>
    [Theory]
    [MemberData(nameof(NotHandWrittenOrdering))]
    public void An_overload_of_CompareTo_does_not_mark_the_ordering_hand_written(Type type) =>
        type.IsDefined(typeof(CodoMetis.TypeKit.CompilerServices.HandWrittenOrderingAttribute), inherit: false).ShouldBeFalse();

    private static readonly int Seven = 7;

    private static readonly CompareToOverloadedByIn OneByIn = CompareToOverloadedByIn.From(1);

    private static T ParseThroughInterface<T>(string text) where T : IParsable<T> => T.Parse(text, null);

    private static string Format<T>(T value) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[16];
        return value.TryFormat(buffer, out var written, "D3", null) ? new string(buffer[..written]) : "";
    }

    private static string FormatUtf8<T>(T value) where T : IUtf8SpanFormattable
    {
        Span<byte> buffer = stackalloc byte[16];
        return value.TryFormat(buffer, out var written, "D3", null) ? Encoding.UTF8.GetString(buffer[..written]) : "";
    }
}
