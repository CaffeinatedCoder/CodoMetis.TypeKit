using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// Input the wrapped type cannot read is refused without ever being quoted: input can be a secret,
/// and a refusal ends up in logs and in answers to the client.
/// </summary>
/// <remarks>
/// <para>
/// The generated code called the wrapped type's own parsing, whose messages quote the input: "The input
/// string 'SECRET' was not in a correct format." (every number), "The string 'SECRET' was not recognized
/// as a valid DateTime.", NodaTime's "Value being parsed: '^SECRET'", through <c>Parse</c> and the type
/// converter alike, and a NodaTime JSON value carried NodaTime's exception, with the input, as its inner
/// exception. For a dictionary key the serializer's own message contains the key, as its path.
/// </para>
/// <para>
/// Now every generated <c>Parse</c>, the type converter and every JSON read throw their usual exception
/// type with a message naming the value object and the wrapped type, and no inner exception. The
/// serializer still sets <see cref="JsonException.Path"/>, which for a key contains the key: that is
/// the serializer's, and out of the generated code's reach.
/// </para>
/// </remarks>
public sealed class UnreadableInputTests
{
    private const string Secret = "http://[SECRET-7f3a";

    private sealed record Attempt(Type Exception, string Message, Func<object?> Action);

    private static Attempt Parsing<T>(string valueObject, string wrapped, Func<T> action) =>
        new(typeof(FormatException), $"{valueObject} could not read the input as {wrapped}.", () => action());

    private static Attempt Json<T>(string valueObject, string wrapped, Func<T> action) =>
        new(typeof(JsonException), $"{valueObject} could not read the JSON value as {wrapped}.", () => action());

    private static object? ConvertFrom(Type type) => TypeDescriptor.GetConverter(type).ConvertFromInvariantString(Secret);

    private static readonly byte[] Utf8 = Encoding.UTF8.GetBytes(Secret);

    private static readonly string JsonValue = JsonSerializer.Serialize(Secret);

    private static readonly string JsonKey = $"{{{JsonSerializer.Serialize(Secret)}:1}}";

    private static readonly Dictionary<string, Attempt> Attempts = new()
    {
        // ISpanParsable and IUtf8SpanParsable: a number.
        ["int, Parse(string)"] = Parsing("ProbeCount", "Int32", () => ProbeCount.Parse(Secret, null)),
        ["int, Parse(span)"] = Parsing("ProbeCount", "Int32", () => ProbeCount.Parse(Secret.AsSpan(), null)),
        ["int, Parse(UTF-8)"] = Parsing("ProbeCount", "Int32", () => ProbeCount.Parse(Utf8, null)),
        ["int, type converter"] = Parsing("ProbeCount", "Int32", () => ConvertFrom(typeof(ProbeCount))),
        ["int, JSON value"] = Json("ProbeCount", "Int32", () => JsonSerializer.Deserialize<ProbeCount>(JsonValue)),
        ["int, JSON key"] = Json("ProbeCount", "Int32", () => JsonSerializer.Deserialize<Dictionary<ProbeCount, int>>(JsonKey)),

        // A validated value object: the wrapped type refuses the text before Create sees it.
        ["validated int, Parse(string)"] = Parsing("ProbePercentage", "Int32", () => ProbePercentage.Parse(Secret, null)),
        ["validated int, Parse(span)"] = Parsing("ProbePercentage", "Int32", () => ProbePercentage.Parse(Secret.AsSpan(), null)),
        ["validated int, Parse(UTF-8)"] = Parsing("ProbePercentage", "Int32", () => ProbePercentage.Parse(Utf8, null)),
        ["validated int, type converter"] = Parsing("ProbePercentage", "Int32", () => ConvertFrom(typeof(ProbePercentage))),
        ["validated int, JSON key"] = Json("ProbePercentage", "Int32", () => JsonSerializer.Deserialize<Dictionary<ProbePercentage, int>>(JsonKey)),

        ["DateTime, Parse(string)"] = Parsing("ProbeTimestamp", "DateTime", () => ProbeTimestamp.Parse(Secret, null)),
        ["DateTime, Parse(span)"] = Parsing("ProbeTimestamp", "DateTime", () => ProbeTimestamp.Parse(Secret.AsSpan(), null)),
        ["DateTime, type converter"] = Parsing("ProbeTimestamp", "DateTime", () => ConvertFrom(typeof(ProbeTimestamp))),
        ["DateTime, JSON value"] = Json("ProbeTimestamp", "DateTime", () => JsonSerializer.Deserialize<ProbeTimestamp>(JsonValue)),
        ["DateTime, JSON key"] = Json("ProbeTimestamp", "DateTime", () => JsonSerializer.Deserialize<Dictionary<ProbeTimestamp, int>>(JsonKey)),

        ["Guid, Parse(UTF-8)"] = Parsing("ProbeId", "Guid", () => ProbeId.Parse(Utf8, null)),
        ["Guid, JSON key"] = Json("ProbeId", "Guid", () => JsonSerializer.Deserialize<Dictionary<ProbeId, int>>(JsonKey)),

        ["enum, Parse(string)"] = Parsing("ProbeWeekday", "DayOfWeek", () => ProbeWeekday.Parse(Secret, null)),
        ["enum, type converter"] = Parsing("ProbeWeekday", "DayOfWeek", () => ConvertFrom(typeof(ProbeWeekday))),
        ["enum, JSON value"] = Json("ProbeWeekday", "DayOfWeek", () => JsonSerializer.Deserialize<ProbeWeekday>(JsonValue)),
        ["enum, JSON key"] = Json("ProbeWeekday", "DayOfWeek", () => JsonSerializer.Deserialize<Dictionary<ProbeWeekday, int>>(JsonKey)),

        ["Uri, Parse(string)"] = Parsing("ProbeUri", "Uri", () => ProbeUri.Parse(Secret, null)),
        ["Uri, type converter"] = Parsing("ProbeUri", "Uri", () => ConvertFrom(typeof(ProbeUri))),
        ["Uri, JSON value"] = Json("ProbeUri", "Uri", () => JsonSerializer.Deserialize<ProbeUri>(JsonValue)),
        ["Uri, JSON key"] = Json("ProbeUri", "Uri", () => JsonSerializer.Deserialize<Dictionary<ProbeUri, int>>(JsonKey)),

        // A static Parse(string) and a string constructor, which quote the input themselves.
        ["static Parse, Parse(string)"] = Parsing("ProbeSku", "ProbeSkuText", () => ProbeSku.Parse(Secret, null)),
        ["static Parse, type converter"] = Parsing("ProbeSku", "ProbeSkuText", () => ConvertFrom(typeof(ProbeSku))),
        ["string constructor, Parse(string)"] = Parsing("ProbeIsbn", "ProbeIsbnText", () => ProbeIsbn.Parse(Secret, null)),
        ["string constructor, type converter"] = Parsing("ProbeIsbn", "ProbeIsbnText", () => ConvertFrom(typeof(ProbeIsbn))),

        // NodaTime: its type converter for parsing, its JSON converters for JSON.
        ["NodaTime, Parse(string)"] = Parsing("ProbeInstant", "Instant", () => ProbeInstant.Parse(Secret, null)),
        ["NodaTime, type converter"] = Parsing("ProbeInstant", "Instant", () => ConvertFrom(typeof(ProbeInstant))),
        ["NodaTime, JSON value"] = Json("ProbeInstant", "Instant", () => JsonSerializer.Deserialize<ProbeInstant>(JsonValue)),
        ["NodaTime, JSON key"] = Json("ProbeInstant", "Instant", () => JsonSerializer.Deserialize<Dictionary<ProbeInstant, int>>(JsonKey)),
        ["NodaTime LocalDate, JSON value"] = Json("ProbeLocalDate", "LocalDate", () => JsonSerializer.Deserialize<ProbeLocalDate>(JsonValue)),
    };

    public static TheoryData<string> AttemptNames => [.. Attempts.Keys];

    [Theory]
    [MemberData(nameof(AttemptNames))]
    public void Unreadable_input_is_refused_without_being_quoted(string attempt)
    {
        var (type, message, action) = Attempts[attempt];

        var exception = Should.Throw(() => action(), type);

        exception.GetType().ShouldBe(type, attempt);
        exception.Message.ShouldBe(message, attempt);
        exception.InnerException.ShouldBeNull(attempt);

        for (Exception? current = exception; current is not null; current = current.InnerException)
            current.Message.ShouldNotContain("SECRET", Case.Insensitive, attempt);
    }

    /// <summary>Where the wrapped type has a <c>TryParse</c>, <c>TryParse</c> throws nothing.</summary>
    [Fact]
    public void TryParse_answers_false_and_throws_nothing()
    {
        ProbeCount.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeCount.TryParse(Secret.AsSpan(), null, out _).ShouldBeFalse();
        ProbeCount.TryParse(Utf8, null, out _).ShouldBeFalse();
        ProbePercentage.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeTimestamp.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeWeekday.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeUri.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeSku.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeIsbn.TryParse(Secret, null, out _).ShouldBeFalse();
        ProbeInstant.TryParse(Secret, null, out _).ShouldBeFalse();
    }

    /// <summary>The strategies without a <c>TryParse</c> still parse what their type reads.</summary>
    [Fact]
    public void A_static_Parse_and_a_string_constructor_still_parse()
    {
        ProbeSku.Parse("ABC-123", null).Value.Text.ShouldBe("ABC-123");
        ProbeIsbn.Parse("9783161484100", null).Value.Text.ShouldBe("9783161484100");
        ProbeIsbn.TryParse("9783161484100", null, out var isbn).ShouldBeTrue();
        isbn.Value.Text.ShouldBe("9783161484100");
    }
}
