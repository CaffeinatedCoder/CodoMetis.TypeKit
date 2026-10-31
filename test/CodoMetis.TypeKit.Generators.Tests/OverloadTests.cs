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
    };

    /// <summary>The hand-written overload is kept, and a call that names its type arguments or its ref kind reaches it.</summary>
    [Theory]
    [MemberData(nameof(HandWritten))]
    public void The_hand_written_overload_is_kept(string overload, Action handWritten) =>
        Should.Throw<NotSupportedException>(handWritten, overload).Message.ShouldBe("hand-written");

    private static readonly int Seven = 7;

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
