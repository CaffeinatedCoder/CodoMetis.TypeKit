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

    /// <summary>
    /// A <see cref="Uri"/> parses relative or absolute, as the serializer reads it: through its
    /// constructor, "/orders/7" became file:///orders/7 on macOS and Linux, "orders/7" threw, and
    /// <c>Parse(x.ToString(), null)</c> did not round-trip what JSON had read.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/a?b=c")]
    [InlineData("/orders/7")]
    [InlineData("orders/7")]
    public void A_uri_round_trips_through_its_text_relative_or_absolute(string text)
    {
        var fromJson = System.Text.Json.JsonSerializer.Deserialize<ProbeUri>(System.Text.Json.JsonSerializer.Serialize(text));

        ProbeUri.Parse(text, null).ShouldBe(fromJson);
        ProbeUri.Parse(fromJson.ToString(), null).ShouldBe(fromJson);
        ProbeUri.Parse(text, null).Value.OriginalString.ShouldBe(text);
        ProbeUri.TryParse(text, null, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(fromJson);
        System.ComponentModel.TypeDescriptor.GetConverter(typeof(ProbeUri)).ConvertFromInvariantString(text).ShouldBe(fromJson);
    }

    [Fact]
    public void A_uri_neither_relative_nor_absolute_is_refused_as_JSON_refuses_it()
    {
        Should.Throw<FormatException>(() => ProbeUri.Parse("http://[::1", null));
        ProbeUri.TryParse("http://[::1", null, out _).ShouldBeFalse();
        Should.Throw<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<ProbeUri>("\"http://[::1\""));
    }

    /// <summary>NodaTime's types carry a <c>[TypeConverter]</c>, which is the strategy they get.</summary>
    [Fact]
    public void A_type_with_a_type_converter_parses_through_it()
    {
        ProbeInstant.Parse("2026-09-27T12:00:00Z", CultureInfo.InvariantCulture).Value.ShouldBe(Instant.FromUtc(2026, 9, 27, 12, 0));
        ProbeInstant.TryParse("not an instant", CultureInfo.InvariantCulture, out _).ShouldBeFalse();
    }

    /// <summary>
    /// An enum is <c>IConvertible</c> but <c>Convert.ChangeType</c> cannot make one from a string, so a
    /// parse that went that way threw <c>InvalidCastException</c> for every input and <c>TryParse</c>
    /// answered <see langword="false"/> for every input, while the type still advertised <c>IParsable</c>.
    /// </summary>
    [Fact]
    public void An_enum_parses_by_name()
    {
        ProbeWeekday.Parse("Monday", null).Value.ShouldBe(DayOfWeek.Monday);
        ProbeWeekday.TryParse("Friday", null, out var parsed).ShouldBeTrue();
        parsed.Value.ShouldBe(DayOfWeek.Friday);
        ProbeWeekday.Parse(ProbeWeekday.From(DayOfWeek.Sunday).ToString(), null).Value.ShouldBe(DayOfWeek.Sunday);
    }

    [Fact]
    public void An_unknown_enum_name_is_a_FormatException_and_a_false()
    {
        Should.Throw<FormatException>(() => ProbeWeekday.Parse("Funday", null)).Message.ShouldNotContain("Funday");
        ProbeWeekday.TryParse("Funday", null, out _).ShouldBeFalse();
        ProbeWeekday.TryParse(null, null, out _).ShouldBeFalse();
    }

    /// <summary>The type converter delegates to <c>Parse</c>, so it parses an enum the same way.</summary>
    [Fact]
    public void An_enum_converts_from_its_name_through_the_type_converter() =>
        System.ComponentModel.TypeDescriptor.GetConverter(typeof(ProbeWeekday)).ConvertFromInvariantString("Tuesday").ShouldBe(ProbeWeekday.From(DayOfWeek.Tuesday));

    /// <summary>
    /// <c>ToString()</c> is invariant, so <c>Parse(x.ToString(), null)</c> must be too: with a null
    /// provider meaning the current culture, "1.5" parsed as 15 in de-DE.
    /// </summary>
    [Fact]
    public void A_null_provider_means_the_invariant_culture_so_ToString_round_trips()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var amount = ProbeAmount.From(1.5m);

            ProbeAmount.Parse(amount.ToString(), null).ShouldBe(amount);
            ProbeAmount.Parse(amount.ToString().AsSpan(), null).ShouldBe(amount);
            ProbeAmount.Parse("1.5"u8, null).ShouldBe(amount);

            ProbeAmount.TryParse("1.5", null, out var parsed).ShouldBeTrue();
            parsed.ShouldBe(amount);
            ProbeAmount.TryParse("1.5".AsSpan(), null, out parsed).ShouldBeTrue();
            parsed.ShouldBe(amount);
            ProbeAmount.TryParse("1.5"u8, null, out parsed).ShouldBeTrue();
            parsed.ShouldBe(amount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>The counterpart, so the fix is not "always invariant": a provider that is given is used.</summary>
    [Fact]
    public void A_given_provider_is_still_honoured() =>
        ProbeAmount.Parse("1,5", CultureInfo.GetCultureInfo("de-DE")).ShouldBe(ProbeAmount.From(1.5m));
}
