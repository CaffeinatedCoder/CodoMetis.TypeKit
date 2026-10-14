using System.ComponentModel;
using System.Globalization;
using System.Net;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>A value object over a reference type that is span and UTF-8 formattable.</summary>
public readonly partial record struct DefaultedVersion : IValue<Version>;

/// <summary>See <see cref="DefaultedVersion"/>.</summary>
public readonly partial record struct DefaultedAddress : IValue<IPAddress>;

/// <summary>
/// A default instance is not supposed to exist (CMTK0001), but EF Core, MVC and serializers make one,
/// and over a reference type it holds null. Its text is <c>""</c> wherever it is formatted, as its
/// <c>ToString()</c> over a string always was: <c>ToString()</c>, interpolation, <c>IFormattable</c>,
/// both <c>TryFormat</c>s, <c>Convert.ToString</c>, <c>IConvertible</c> and the type converter threw
/// <see cref="NullReferenceException"/> over a <c>Uri</c>, a <c>Version</c> or an <c>IPAddress</c>, and
/// <c>Convert.ToString</c> and <c>IConvertible</c> over a string too.
/// </summary>
public sealed class DefaultInstanceFormattingTests
{
#pragma warning disable CMTK0001 // The default instance is the subject.
    public static TheoryData<string> Wrapped => ["string", "Uri", "Version", "IPAddress"];

    private static readonly Dictionary<string, object> Defaults = new()
    {
        ["string"] = default(ProbeName),
        ["Uri"] = default(ProbeUri),
        ["Version"] = default(DefaultedVersion),
        ["IPAddress"] = default(DefaultedAddress),
    };

    [Theory]
    [MemberData(nameof(Wrapped))]
    public void A_default_instance_over_a_reference_type_formats_as_an_empty_string(string wrapped)
    {
        var instance = Defaults[wrapped];

        instance.ToString().ShouldBe("");
        $"{instance}".ShouldBe("");
        string.Format(CultureInfo.InvariantCulture, "{0}", instance).ShouldBe("");
        ((IFormattable)instance).ToString(null, null).ShouldBe("");
        Convert.ToString(instance, CultureInfo.InvariantCulture).ShouldBe("");
        TypeDescriptor.GetConverter(instance.GetType()).ConvertToInvariantString(instance).ShouldBe("");

        if (instance is ISpanFormattable span)
        {
            span.TryFormat(new char[8], out var charsWritten, default, null).ShouldBeTrue();
            charsWritten.ShouldBe(0);
        }

        if (instance is IUtf8SpanFormattable utf8)
        {
            utf8.TryFormat(new byte[8], out var bytesWritten, default, null).ShouldBeTrue();
            bytesWritten.ShouldBe(0);
        }
    }

    /// <summary>The cases above that a span or UTF-8 format would answer do run, so they cannot pass by being skipped.</summary>
    [Fact]
    public void The_span_formats_are_among_the_cases()
    {
        Defaults.Values.Count(instance => instance is ISpanFormattable).ShouldBe(3);
        Defaults.Values.Count(instance => instance is IUtf8SpanFormattable).ShouldBe(2);
    }

    /// <summary>
    /// Over a string, <c>IConvertible</c> answers for the null a default instance holds as
    /// <see cref="Convert"/> answers for a null object: <c>""</c> as text, the target type's default as
    /// a number, <see cref="TypeCode.Empty"/> as its type code.
    /// </summary>
    [Fact]
    public void A_default_instance_over_a_string_converts_as_Convert_converts_a_null_object()
    {
        var convertible = (IConvertible)default(ProbeName);

        convertible.ToString(null).ShouldBe("");
        convertible.ToType(typeof(string), null).ShouldBe("");
        convertible.ToInt32(null).ShouldBe(Convert.ToInt32((object?)null, CultureInfo.InvariantCulture));
        convertible.ToBoolean(null).ShouldBe(Convert.ToBoolean((object?)null, CultureInfo.InvariantCulture));
        convertible.GetTypeCode().ShouldBe(Convert.GetTypeCode(null));
    }

    /// <summary>A value is converted as before: by its own <c>IConvertible</c>.</summary>
    [Fact]
    public void A_value_over_a_string_converts_as_the_string_does()
    {
        var convertible = (IConvertible)ProbeName.From("42");

        convertible.ToInt32(null).ShouldBe(42);
        convertible.ToString(null).ShouldBe("42");
        convertible.GetTypeCode().ShouldBe(TypeCode.String);
        convertible.ToType(typeof(string), null).ShouldBe("42");
        convertible.ToType(typeof(long), null).ShouldBe(42L);
        Should.Throw<FormatException>(() => ((IConvertible)ProbeName.From("x")).ToInt32(null));
    }
#pragma warning restore CMTK0001
}
