using System.Globalization;
using CodoMetis.TypeKit.Generators.Probes;
using NodaTime;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// The generated <c>IParsable</c>/<c>ISpanParsable</c>/<c>IUtf8SpanParsable</c>, one probe per
/// strategy. The validated side is in <see cref="EntryPointValidationTests"/>.
/// </summary>
public sealed class ParsingTests
{
    [Fact]
    public void A_string_value_object_parses_to_the_string_itself()
    {
        ProbeName.Parse("text", null).Value.ShouldBe("text");
        ProbeName.TryParse("text", null, out var parsed).ShouldBeTrue();
        parsed.Value.ShouldBe("text");
    }

    /// <summary><c>IParsable.Parse</c> takes a non-null string; wrapping null would make an invalid instance.</summary>
    [Fact]
    public void Parsing_null_throws_and_trying_to_returns_false()
    {
        Should.Throw<ArgumentNullException>(() => ProbeName.Parse(null!, null));
        ProbeName.TryParse(null, null, out _).ShouldBeFalse();
        ProbeCount.TryParse((string?)null, null, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_number_parses_as_text_span_and_UTF_8()
    {
        ProbeCount.Parse("42", CultureInfo.InvariantCulture).Value.ShouldBe(42);
        ProbeCount.Parse("42".AsSpan(), CultureInfo.InvariantCulture).Value.ShouldBe(42);
        ProbeCount.Parse("42"u8, CultureInfo.InvariantCulture).Value.ShouldBe(42);

        ProbeCount.TryParse("x", null, out _).ShouldBeFalse();
        ProbeCount.TryParse("x".AsSpan(), null, out _).ShouldBeFalse();
        ProbeCount.TryParse("x"u8, null, out _).ShouldBeFalse();
    }

    /// <summary><c>bool</c> implements <c>IParsable</c> explicitly, so <c>bool.Parse(s, provider)</c> does not exist.</summary>
    [Fact]
    public void A_type_that_implements_IParsable_explicitly_parses()
    {
        ProbeFlag.Parse("true", null).Value.ShouldBeTrue();
        ProbeFlag.TryParse("False", null, out var parsed).ShouldBeTrue();
        parsed.Value.ShouldBeFalse();
    }

    [Fact]
    public void A_guid_and_a_date_parse()
    {
        var id = Guid.CreateVersion7();

        ProbeId.Parse(id.ToString(), null).Value.ShouldBe(id);
        ProbeDate.Parse("2026-09-27", CultureInfo.InvariantCulture).Value.ShouldBe(new DateOnly(2026, 9, 27));
    }

    /// <summary>A type without IParsable that has a string constructor: the constructor's exception means "no" to TryParse.</summary>
    [Fact]
    public void A_type_with_a_string_constructor_parses_through_it()
    {
        ProbeUri.Parse("https://example.com/", null).Value.ShouldBe(new Uri("https://example.com/"));
        ProbeUri.TryParse("::not a uri", null, out _).ShouldBeFalse();
    }

    /// <summary>NodaTime's types carry a <c>[TypeConverter]</c>, which is the strategy they get.</summary>
    [Fact]
    public void A_type_with_a_type_converter_parses_through_it()
    {
        ProbeInstant.Parse("2026-09-27T12:00:00Z", CultureInfo.InvariantCulture).Value.ShouldBe(Instant.FromUtc(2026, 9, 27, 12, 0));
        ProbeInstant.TryParse("not an instant", CultureInfo.InvariantCulture, out _).ShouldBeFalse();
    }
}
