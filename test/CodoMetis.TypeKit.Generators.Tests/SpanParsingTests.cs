using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

public readonly partial record struct OverLong : IValue<long>;

public readonly partial record struct OverByte : IValue<byte>;

public readonly partial record struct OverUInt64 : IValue<ulong>;

public readonly partial record struct OverChar : IValue<char>;

/// <summary><see cref="Version"/> reads UTF-8 but neither a string nor a span through an interface: it parses through its string constructor.</summary>
public readonly partial record struct OverVersion : IValue<Version>;

[Flags]
public enum ProbeAccess
{
    None  = 0,
    Read  = 1,
    Write = 2
}

public readonly partial record struct OverAccess : IValue<ProbeAccess>;

/// <summary>
/// Span and UTF-8 parsing are generated exactly where the wrapped type has them, and read and refuse
/// what <c>Parse(string)</c> reads and refuses, one row per wrapped type of the shape catalogue
/// (<c>shape.wrapped</c> in <c>.claude/probe-axes.md</c>) and per parse strategy.
/// </summary>
/// <remarks>
/// A value object over <see cref="string"/> got no <c>ISpanParsable</c>, although <see cref="string"/>
/// implements it: its strategy was picked before the span check and implemented <c>IParsable</c> alone
/// (found by the prober, claim C11, 2026-10-17).
/// </remarks>
public sealed class SpanParsingTests
{
    private const string Secret = "SECRET-7f3a";

    /// <param name="ValueObject">The value object.</param>
    /// <param name="Wrapped">The type it wraps.</param>
    /// <param name="Valid">Text the value object reads.</param>
    /// <param name="Refused">Text the value object refuses, the wrapped type's refusal or <c>Create</c>'s, none for a plain string.</param>
    public sealed record Wrapping(Type ValueObject, Type Wrapped, string Valid, params string[] Refused);

    private static readonly Dictionary<string, Wrapping> Cases = new()
    {
        ["string"]                  = new(typeof(ProbeName), typeof(string), "text"),
        ["string, record class"]    = new(typeof(ProbeLabel), typeof(string), "text"),
        ["string, validated"]       = new(typeof(ProbeCode), typeof(string), "  ABC  ", "s3cr3t"),
        ["string, fault set"]       = new(typeof(ProbeArticle), typeof(string), "  ABC  ", "s3cr3t"),
        ["Guid"]                    = new(typeof(ProbeId), typeof(Guid), "0199f3a4-8c5e-7b2a-9d41-3c6e5f708192", Secret),
        ["int"]                     = new(typeof(ProbeCount), typeof(int), "42", Secret),
        ["int, validated"]          = new(typeof(ProbePercentage), typeof(int), "50", Secret, "1010101"),
        ["long"]                    = new(typeof(OverLong), typeof(long), "42", Secret),
        ["byte"]                    = new(typeof(OverByte), typeof(byte), "42", Secret, "2560"),
        ["ulong"]                   = new(typeof(OverUInt64), typeof(ulong), "42", Secret, "-4242"),
        ["decimal"]                 = new(typeof(ProbeAmount), typeof(decimal), "1.5", Secret),
        ["double"]                  = new(typeof(ProbeRatio), typeof(double), "0.25", Secret),
        ["bool"]                    = new(typeof(ProbeFlag), typeof(bool), "true", Secret),
        ["char"]                    = new(typeof(OverChar), typeof(char), "c", Secret),
        ["DateTime"]                = new(typeof(ProbeTimestamp), typeof(DateTime), "2026-10-17T12:00:00", Secret),
        ["DateTimeOffset"]          = new(typeof(ProbeMoment), typeof(DateTimeOffset), "2026-10-17T12:00:00+02:00", Secret),
        ["DateOnly"]                = new(typeof(ProbeDate), typeof(DateOnly), "2026-10-17", Secret),
        ["TimeOnly"]                = new(typeof(ProbeTime), typeof(TimeOnly), "12:30", Secret),
        ["TimeSpan"]                = new(typeof(ProbeDuration), typeof(TimeSpan), "01:02:03", Secret),
        ["enum"]                    = new(typeof(ProbeWeekday), typeof(DayOfWeek), "Monday", Secret),
        ["flags enum"]              = new(typeof(OverAccess), typeof(ProbeAccess), "Read, Write", Secret),
        ["Uri"]                     = new(typeof(ProbeUri), typeof(Uri), "/orders/7", "http://[SECRET-7f3a"),
        ["NodaTime.Instant"]        = new(typeof(ProbeInstant), typeof(NodaTime.Instant), "2026-10-17T12:00:00Z", Secret),
        ["Version"]                 = new(typeof(OverVersion), typeof(Version), "1.2.3", Secret),
        ["own IParsable class"]     = new(typeof(ProbeSpan), typeof(ProbeSpanText), "1..2", Secret),
        ["own static Parse"]        = new(typeof(ProbeSku), typeof(ProbeSkuText), "ABC-123", Secret),
        ["own string constructor"]  = new(typeof(ProbeIsbn), typeof(ProbeIsbnText), "9783161484100", Secret),
        ["closed set"]              = new(typeof(OverProbeStatus), typeof(ProbeStatus), "Shipped", Secret),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Span_and_UTF_8_parsing_are_generated_exactly_where_the_wrapped_type_has_them(string name)
    {
        var (valueObject, wrapped, _, _) = Cases[name];

        SpanParsing.Implements(valueObject, typeof(ISpanParsable<>)).ShouldBe(
            SpanParsing.Implements(wrapped, typeof(ISpanParsable<>)),
            $"{valueObject.Name} and {wrapped.Name} disagree on ISpanParsable");

        SpanParsing.Implements(valueObject, typeof(IUtf8SpanParsable<>)).ShouldBe(
            SpanParsing.Implements(wrapped, typeof(IUtf8SpanParsable<>)),
            $"{valueObject.Name} and {wrapped.Name} disagree on IUtf8SpanParsable");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Span_and_UTF_8_parsing_read_what_Parse_reads(string name)
    {
        var (valueObject, _, valid, _) = Cases[name];
        var parsed = SpanParsing.Parse(valueObject, valid);

        foreach (var (entry, parse, tryParse) in SpanParsing.EntriesOf(valueObject))
        {
            parse(valid).ShouldBe(parsed, $"{entry}.Parse");

            var (ok, result) = tryParse(valid);
            ok.ShouldBeTrue($"{entry}.TryParse");
            result.ShouldBe(parsed, $"{entry}.TryParse");
        }
    }

    /// <summary>
    /// The same refusal as <c>Parse(string)</c>'s, the wrapped type's or <c>Create</c>'s: a
    /// <see cref="FormatException"/> naming the value object, with no inner exception, never quoting the
    /// input; and a <see langword="false"/> from <c>TryParse</c>, which throws nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Span_and_UTF_8_parsing_refuse_what_Parse_refuses_without_quoting_it(string name)
    {
        var (valueObject, _, _, refused) = Cases[name];

        foreach (var text in refused)
        {
            var expected = Should.Throw<FormatException>(() => SpanParsing.Parse(valueObject, text));

            foreach (var (entry, parse, tryParse) in SpanParsing.EntriesOf(valueObject))
            {
                var exception = Should.Throw<FormatException>(() => parse(text), $"{entry}.Parse('{text}')");

                exception.Message.ShouldBe(expected.Message, $"{entry}.Parse('{text}')");
                exception.Message.ShouldContain(valueObject.Name);
                exception.Message.ShouldNotContain(text, Case.Insensitive);
                exception.InnerException.ShouldBeNull();

                tryParse(text).Ok.ShouldBeFalse($"{entry}.TryParse('{text}')");
            }
        }
    }
}

/// <summary>
/// A value object's <c>Parse</c>/<c>TryParse</c> through <c>IParsable</c>, <c>ISpanParsable</c> and
/// <c>IUtf8SpanParsable</c>, by reflection, so a test of a value object that lacks one compiles and fails
/// rather than not compiling.
/// </summary>
internal static class SpanParsing
{
    public static bool Implements(Type type, Type definition) =>
        type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == definition && implemented.GenericTypeArguments[0] == type);

    /// <summary><c>IParsable&lt;T&gt;.Parse(text, null)</c>.</summary>
    public static object Parse(Type type, string text) => Call(nameof(ParseText), typeof(IParsable<>), type, text);

    /// <summary><c>ISpanParsable&lt;T&gt;.Parse(text, null)</c> over the characters.</summary>
    public static object ParseSpan(Type type, string text) => Call(nameof(ParseChars), typeof(ISpanParsable<>), type, text);

    public static (bool Ok, object? Result) TryParseSpan(Type type, string text) => ((bool, object?))Call(nameof(TryParseChars), typeof(ISpanParsable<>), type, text);

    /// <summary><c>IUtf8SpanParsable&lt;T&gt;.Parse(text, null)</c> over the text's UTF-8.</summary>
    public static object ParseUtf8(Type type, string text) => Call(nameof(ParseBytes), typeof(IUtf8SpanParsable<>), type, text);

    public static (bool Ok, object? Result) TryParseUtf8(Type type, string text) => ((bool, object?))Call(nameof(TryParseBytes), typeof(IUtf8SpanParsable<>), type, text);

    /// <summary>The span and UTF-8 entry points <paramref name="type"/> has.</summary>
    public static IEnumerable<(string Entry, Func<string, object> Parse, Func<string, (bool Ok, object? Result)> TryParse)> EntriesOf(Type type)
    {
        if (Implements(type, typeof(ISpanParsable<>)))
            yield return ("ISpanParsable", text => ParseSpan(type, text), text => TryParseSpan(type, text));

        if (Implements(type, typeof(IUtf8SpanParsable<>)))
            yield return ("IUtf8SpanParsable", text => ParseUtf8(type, text), text => TryParseUtf8(type, text));
    }

    private static object Call(string helper, Type definition, Type type, string text)
    {
        Implements(type, definition).ShouldBeTrue($"{type.Name} does not implement {definition.Name}");

        try
        {
            return typeof(SpanParsing).GetMethod(helper, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [text])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);
            throw;
        }
    }

    private static object ParseText<T>(string text) where T : IParsable<T> => T.Parse(text, null);

    private static object ParseChars<T>(string text) where T : ISpanParsable<T> => T.Parse(text.AsSpan(), null);

    private static (bool, object?) TryParseChars<T>(string text) where T : ISpanParsable<T> => (T.TryParse(text.AsSpan(), null, out var result), result);

    private static object ParseBytes<T>(string text) where T : IUtf8SpanParsable<T> => T.Parse(Encoding.UTF8.GetBytes(text), null);

    private static (bool, object?) TryParseBytes<T>(string text) where T : IUtf8SpanParsable<T> => (T.TryParse(Encoding.UTF8.GetBytes(text), null, out var result), result);
}
