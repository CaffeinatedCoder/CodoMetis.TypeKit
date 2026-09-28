using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using NodaTime.Text;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// A value object writes exactly the bytes the serializer writes for the value it wraps, under the same
/// options, as a value, as a property and as a dictionary key, and reads what the serializer reads.
/// </summary>
/// <remarks>
/// <para>
/// Before, the generated converter wrote some families itself: a <see cref="TimeOnly"/> as
/// <c>03:04:05.0000000</c> where the serializer writes <c>03:04:05</c>, <see cref="DateTime"/> and
/// <see cref="DateTimeOffset"/> keys with seven fractional digits (and <c>+</c> escaped), string keys
/// without the <see cref="JsonSerializerOptions.DictionaryKeyPolicy"/>, and a converter the host
/// registered for <see cref="Guid"/>, <see cref="DateTime"/> or a number key was ignored.
/// </para>
/// <para>
/// Every probe family, under default options, <see cref="JsonSerializerOptions.Web"/>, a converter the
/// host registers for the wrapped type (with and without its own key methods, which the serializer
/// then takes from the built-in converter) and a camel-case key policy. A NodaTime type has no JSON
/// form of its own until the options are configured for NodaTime (the serializer writes <c>{}</c>, and
/// refuses it as a key), so its cells configure them; unconfigured, the value object writes NodaTime's
/// own format (<see cref="JsonTests"/>). A <see cref="DateTime"/> is a UTC one here: the value object
/// writes every <see cref="DateTime"/> as UTC by design (<see cref="DateTimeJsonTests"/>).
/// </para>
/// </remarks>
public sealed class JsonParityTests
{
    private static readonly Dictionary<string, IParityCase> Families = new()
    {
        ["string"] = Case(ProbeName.From("Ab c"), "Ab c", text => text, text => text),
        ["Guid"] = Case(ProbeId.From(Guid.Parse("0199a3f4-1c00-7000-8000-000000000001")), Guid.Parse("0199a3f4-1c00-7000-8000-000000000001"), id => id.ToString("N"), text => Guid.ParseExact(text, "N")),
        ["int"] = Case(ProbeCount.From(42), 42, number => number.ToString(CultureInfo.InvariantCulture), text => int.Parse(text, CultureInfo.InvariantCulture)),
        ["decimal"] = Case(ProbeAmount.From(1.50m), 1.50m, number => number.ToString(CultureInfo.InvariantCulture), text => decimal.Parse(text, CultureInfo.InvariantCulture)),
        ["double"] = Case(ProbeRatio.From(0.25), 0.25, number => number.ToString(CultureInfo.InvariantCulture), text => double.Parse(text, CultureInfo.InvariantCulture)),
        ["bool"] = Case(ProbeFlag.From(true), true, flag => flag ? "yes" : "no", text => text == "yes"),
        ["DateTime"] = Case(ProbeTimestamp.From(new DateTime(2026, 9, 27, 3, 4, 5, DateTimeKind.Utc)), new DateTime(2026, 9, 27, 3, 4, 5, DateTimeKind.Utc), moment => moment.ToString("O", CultureInfo.InvariantCulture), text => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
        ["DateOnly"] = Case(ProbeDate.From(new DateOnly(2026, 9, 27)), new DateOnly(2026, 9, 27), date => date.ToString("O", CultureInfo.InvariantCulture), text => DateOnly.Parse(text, CultureInfo.InvariantCulture)),
        ["DateTimeOffset"] = Case(ProbeMoment.From(new DateTimeOffset(2026, 9, 27, 3, 4, 5, TimeSpan.FromHours(2))), new DateTimeOffset(2026, 9, 27, 3, 4, 5, TimeSpan.FromHours(2)), moment => moment.ToString("O", CultureInfo.InvariantCulture), text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)),
        ["TimeOnly"] = Case(ProbeTime.From(new TimeOnly(3, 4, 5)), new TimeOnly(3, 4, 5), time => time.ToString("O", CultureInfo.InvariantCulture), text => TimeOnly.Parse(text, CultureInfo.InvariantCulture)),
        ["enum"] = Case(ProbeWeekday.From(DayOfWeek.Monday), DayOfWeek.Monday, day => day.ToString(), Enum.Parse<DayOfWeek>),
        ["Uri"] = Case(ProbeUri.From(new Uri("https://example.com/A")), new Uri("https://example.com/A"), uri => uri.ToString(), text => new Uri(text, UriKind.RelativeOrAbsolute)),
        ["NodaTime Instant"] = Case(ProbeInstant.From(Instant.FromUtc(2026, 9, 27, 3, 4, 5)), Instant.FromUtc(2026, 9, 27, 3, 4, 5), InstantPattern.General.Format, text => InstantPattern.General.Parse(text).Value, nodaTime: true),
        ["NodaTime LocalDate"] = Case(ProbeLocalDate.From(new LocalDate(2026, 9, 27)), new LocalDate(2026, 9, 27), LocalDatePattern.Iso.Format, text => LocalDatePattern.Iso.Parse(text).Value, nodaTime: true),
    };

    private static readonly string[] OptionSets = ["default", "Web", "host converter", "host converter without key methods", "camel-case keys"];

    public static TheoryData<string, string> Cells =>
    [
        .. from family in Families.Keys
           from optionSet in OptionSets
           select (family, optionSet)
    ];

    [Fact]
    public void Every_family_and_every_option_set_is_a_cell() => Cells.Count.ShouldBe(14 * 5);

    [Theory]
    [MemberData(nameof(Cells))]
    public void A_value_object_writes_and_reads_what_the_serializer_does_for_its_wrapped_value(string family, string optionSet)
    {
        var @case = Families[family];

        @case.AssertParity(@case.Options(optionSet), $"{family} under {optionSet}");
    }

    /// <summary>
    /// The host's converter is really in use in the host cells, so the parity there is not two built-in
    /// writes agreeing by default.
    /// </summary>
    [Fact]
    public void A_converter_the_host_registers_is_the_one_that_writes()
    {
        var options = Families["Guid"].Options("host converter");

        JsonSerializer.Serialize(ProbeId.From(Guid.Parse("0199a3f4-1c00-7000-8000-000000000001")), options).ShouldBe("\"host:0199a3f41c0070008000000000000001\"");
        JsonSerializer.Serialize(new Dictionary<ProbeCount, bool> { [ProbeCount.From(42)] = true }, Families["int"].Options("host converter")).ShouldBe("{\"key:42\":true}");
        JsonSerializer.Serialize(new Dictionary<ProbeName, int> { [ProbeName.From("Ab c")] = 1 }, Families["string"].Options("camel-case keys")).ShouldBe("{\"ab c\":1}");
    }

    private static ParityCase<TValueObject, T> Case<TValueObject, T>(TValueObject valueObject, T raw, Func<T, string> format, Func<string, T> parse, bool nodaTime = false)
        where TValueObject : IValueObject<TValueObject, T>
        where T : notnull =>
        new(valueObject, raw, format, parse, nodaTime);

    public interface IParityCase
    {
        JsonSerializerOptions Options(string optionSet);

        void AssertParity(JsonSerializerOptions options, string cell);
    }

    private sealed class ParityCase<TValueObject, T>(TValueObject valueObject, T raw, Func<T, string> format, Func<string, T> parse, bool nodaTime) : IParityCase
        where TValueObject : IValueObject<TValueObject, T>
        where T : notnull
    {
        public JsonSerializerOptions Options(string optionSet)
        {
            var options = optionSet switch
            {
                "default" => new JsonSerializerOptions(),
                "Web" => new JsonSerializerOptions(JsonSerializerOptions.Web),
                "host converter" => new JsonSerializerOptions { Converters = { new HostConverter<T>(format, parse, withKeyMethods: true) } },
                "host converter without key methods" => new JsonSerializerOptions { Converters = { new HostConverter<T>(format, parse, withKeyMethods: false) } },
                "camel-case keys" => new JsonSerializerOptions { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase },
                _ => throw new ArgumentOutOfRangeException(nameof(optionSet), optionSet, null)
            };

            return nodaTime && !optionSet.StartsWith("host", StringComparison.Ordinal) ? options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb) : options;
        }

        public void AssertParity(JsonSerializerOptions options, string cell)
        {
            var value = Outcome(() => JsonSerializer.Serialize(raw, options));
            Outcome(() => JsonSerializer.Serialize(valueObject, options)).ShouldBe(value, $"{cell}: written as a value");
            Outcome(() => JsonSerializer.Deserialize<TValueObject>(value, options)!.Value).ShouldBe(Outcome(() => JsonSerializer.Deserialize<T>(value, options)), $"{cell}: read as a value");

            var property = Outcome(() => JsonSerializer.Serialize(new Holder<T>(raw), options));
            Outcome(() => JsonSerializer.Serialize(new Holder<TValueObject>(valueObject), options)).ShouldBe(property, $"{cell}: written as a property");
            Outcome(() => JsonSerializer.Deserialize<Holder<TValueObject>>(property, options)!.Item.Value).ShouldBe(Outcome(() => JsonSerializer.Deserialize<Holder<T>>(property, options)!.Item), $"{cell}: read as a property");

            var key = Outcome(() => JsonSerializer.Serialize(new Dictionary<T, int> { [raw] = 1 }, options));
            Outcome(() => JsonSerializer.Serialize(new Dictionary<TValueObject, int> { [valueObject] = 1 }, options)).ShouldBe(key, $"{cell}: written as a dictionary key");
            Outcome(() => JsonSerializer.Deserialize<Dictionary<TValueObject, int>>(key, options)!.Keys.Single().Value).ShouldBe(Outcome(() => JsonSerializer.Deserialize<Dictionary<T, int>>(key, options)!.Keys.Single()), $"{cell}: read as a dictionary key");

            // Not two refusals agreeing: in every cell the raw type has a JSON form, as a value and as a
            // key, but for an enum or NodaTime key under a host converter without key methods, which the
            // serializer refuses, having no built-in key converter to fall back to for those types (and
            // so does the value object, asserted above).
            value.ShouldNotStartWith("!", customMessage: cell);

            if (cell.EndsWith("without key methods", StringComparison.Ordinal) && (nodaTime || typeof(T).IsEnum)) key.ShouldBe("!NotSupportedException", cell);
            else key.ShouldNotStartWith("!", customMessage: cell);
        }

        private static string Outcome<TResult>(Func<TResult> action)
        {
            try
            {
                return string.Create(CultureInfo.InvariantCulture, $"{action()}");
            }
            catch (Exception exception)
            {
                return $"!{exception.GetType().Name}";
            }
        }
    }

    public sealed record Holder<T>(T Item);

    /// <summary>
    /// A converter the host registers for a wrapped type, writing a form no built-in converter writes,
    /// so a cell passes only where both sides really went through it.
    /// </summary>
    private sealed class HostConverter<T>(Func<T, string> format, Func<string, T> parse, bool withKeyMethods) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => parse(reader.GetString()!["host:".Length..]);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue($"host:{format(value)}");

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            withKeyMethods ? parse(reader.GetString()!["key:".Length..]) : base.ReadAsPropertyName(ref reader, typeToConvert, options);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (withKeyMethods) writer.WritePropertyName($"key:{format(value)}");
            else base.WriteAsPropertyName(writer, value!, options);
        }
    }
}
