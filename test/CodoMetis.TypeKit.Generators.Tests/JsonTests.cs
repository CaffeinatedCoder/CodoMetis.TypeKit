using System.Globalization;
using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;
using NodaTime;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// The generated JSON converter: the wire format per wrapped type, as a value and as a dictionary
/// key, and the round trip. Asserting the JSON text matters: a converter that silently fell back to
/// serializing the record would round-trip too, as <c>{"Value":…}</c>.
/// </summary>
public sealed class JsonTests
{
    internal static readonly Dictionary<string, (object Value, string Json)> Cases = new()
    {
        ["string"]         = (ProbeName.From("a b"), "\"a b\""),
        ["Guid"]           = (ProbeId.From(Guid.Parse("0199a3f4-1c00-7000-8000-000000000001")), "\"0199a3f4-1c00-7000-8000-000000000001\""),
        ["int"]            = (ProbeCount.From(42), "42"),
        ["decimal"]        = (ProbeAmount.From(1.50m), "1.50"),
        ["bool"]           = (ProbeFlag.From(true), "true"),
        ["DateTime"]       = (ProbeTimestamp.From(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc)), "\"2026-09-27T12:00:00Z\""),
        ["DateOnly"]       = (ProbeDate.From(new DateOnly(2026, 9, 27)), "\"2026-09-27\""),
        ["DateTimeOffset"] = (ProbeMoment.From(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(2))), "\"2026-09-27T12:00:00+02:00\""),
        ["TimeOnly"]       = (ProbeTime.From(new TimeOnly(13, 45, 30)), "\"13:45:30\""),
        ["NodaTime Instant"]   = (ProbeInstant.From(Instant.FromUtc(2026, 9, 27, 12, 0)), "\"2026-09-27T12:00:00Z\""),
        ["NodaTime LocalDate"] = (ProbeLocalDate.From(new LocalDate(2026, 9, 27)), "\"2026-09-27\""),
        ["fallback (Uri)"] = (ProbeUri.From(new Uri("https://example.com/a")), "\"https://example.com/a\""),
        ["fallback (enum)"] = (ProbeWeekday.From(DayOfWeek.Monday), "1"),
        ["record class"]   = (ProbeLabel.From("label"), "\"label\""),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    /// <summary>
    /// Every case, the fallback included: as a key, the fallback goes through the wrapped type's own
    /// converter. Before, it wrote the serialized value as the property name, which for a Uri was a
    /// quoted string inside the quotes and for an enum its number, and read neither back.
    /// </summary>
    public static TheoryData<string> KeyCaseNames => CaseNames;

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void A_value_object_is_written_as_its_value(string @case)
    {
        var (value, json) = Cases[@case];

        JsonSerializer.Serialize(value, value.GetType()).ShouldBe(json);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void A_value_object_is_read_back_from_its_value(string @case)
    {
        var (value, json) = Cases[@case];

        JsonSerializer.Deserialize(json, value.GetType()).ShouldBe(value);
    }

    [Theory]
    [MemberData(nameof(KeyCaseNames))]
    public void A_value_object_round_trips_as_a_dictionary_key(string @case)
    {
        var (value, _) = Cases[@case];
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(value.GetType(), typeof(int));
        var dictionary = (System.Collections.IDictionary)Activator.CreateInstance(dictionaryType)!;
        dictionary.Add(value, 1);

        var json = JsonSerializer.Serialize(dictionary, dictionaryType);
        var read = (System.Collections.IDictionary)JsonSerializer.Deserialize(json, dictionaryType)!;

        read.Keys.Cast<object>().ShouldHaveSingleItem().ShouldBe(value);
        json.ShouldNotContain("\"Value\"");
    }

    [Fact]
    public void A_date_time_is_written_in_UTC()
    {
        var local = new DateTimeOffset(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2)).LocalDateTime;

        JsonSerializer.Serialize(ProbeTimestamp.From(local)).ShouldBe("\"2026-09-27T12:00:00Z\"");
    }

    /// <summary>A JSON null is not a value object: without the check, a string-backed one would wrap null.</summary>
    [Theory]
    [InlineData(typeof(ProbeName))]
    [InlineData(typeof(ProbeId))]
    [InlineData(typeof(ProbeCount))]
    [InlineData(typeof(ProbeCode))]
    public void A_JSON_null_is_refused(Type type)
    {
        var message = Should.Throw<JsonException>(() => JsonSerializer.Deserialize("null", type)).Message;

        message.ShouldStartWith($"{type.Name} cannot be read from a JSON null.");
        message.ShouldEndWith("https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#a-null-given-to-a-value-object");
    }

    [Fact]
    public void A_nullable_value_object_reads_a_JSON_null_as_null() => JsonSerializer.Deserialize<ProbeId?>("null").ShouldBeNull();

    /// <summary>
    /// Malformed input is a <c>JsonException</c>, as it is for the wrapped type itself: the
    /// serializer adds the path, and ASP.NET Core answers it with 400. A parse inside the converter
    /// let a <c>FormatException</c> escape instead, and a minimal API answered 500.
    /// </summary>
    [Theory]
    [InlineData(typeof(ProbeDate), "\"2026-02-30\"")]
    [InlineData(typeof(ProbeTime), "\"25:00\"")]
    [InlineData(typeof(ProbeInstant), "\"nope\"")]
    [InlineData(typeof(ProbeLocalDate), "\"nope\"")]
    public void A_malformed_value_is_a_JsonException(Type type, string json) =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, type));

    /// <summary>
    /// A key is parsed by the wrapped type's own built-in converter, as strictly as the value path
    /// parses a value, and malformed text is a <c>JsonException</c>. Number keys were parsed with
    /// <c>NumberStyles.Any</c>, which read "1,000" as 1000 and "(5)" as -5.
    /// </summary>
    [Theory]
    [InlineData(typeof(ProbeId), "nope")]
    [InlineData(typeof(ProbeTimestamp), "nope")]
    [InlineData(typeof(ProbeMoment), "nope")]
    [InlineData(typeof(ProbeDate), "2026-02-30")]
    [InlineData(typeof(ProbeTime), "25:00")]
    [InlineData(typeof(ProbeFlag), "maybe")]
    [InlineData(typeof(ProbeCount), "x")]
    [InlineData(typeof(ProbeCount), "99999999999")]
    [InlineData(typeof(ProbeCount), "1,000")]
    [InlineData(typeof(ProbeAmount), "(5)")]
    [InlineData(typeof(ProbeInstant), "nope")]
    public void A_malformed_dictionary_key_is_a_JsonException(Type type, string key) =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize($"{{\"{key}\":1}}", typeof(Dictionary<,>).MakeGenericType(type, typeof(int))));

    /// <summary>
    /// A date-time key accepts what the value accepts. The key was parsed with the exact "O" pattern,
    /// which refused "2026-09-27T12:00:00Z" for lacking seven fractional digits.
    /// </summary>
    [Fact]
    public void A_date_time_key_accepts_what_the_value_accepts()
    {
        var value = JsonSerializer.Deserialize<ProbeTimestamp>("\"2026-09-27T12:00:00Z\"");

        JsonSerializer.Deserialize<Dictionary<ProbeTimestamp, int>>("{\"2026-09-27T12:00:00Z\":1}")!.Keys.ShouldHaveSingleItem().ShouldBe(value);
    }

    /// <summary>A fallback key is written in the wrapped type's own key format: an enum by name, a Uri as its text.</summary>
    [Fact]
    public void A_fallback_key_is_the_wrapped_type_s_own_key_format()
    {
        JsonSerializer.Serialize(new Dictionary<ProbeWeekday, int> { [ProbeWeekday.From(DayOfWeek.Monday)] = 1 }).ShouldBe("{\"Monday\":1}");
        JsonSerializer.Serialize(new Dictionary<ProbeUri, int> { [ProbeUri.From(new Uri("https://example.com/a"))] = 1 }).ShouldBe("{\"https://example.com/a\":1}");
    }

    [Fact]
    public void Numbers_are_written_with_the_invariant_culture_whatever_the_current_one()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            JsonSerializer.Serialize(new Dictionary<ProbeAmount, int> { [ProbeAmount.From(1.5m)] = 1 }).ShouldBe("{\"1.5\":1}");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
