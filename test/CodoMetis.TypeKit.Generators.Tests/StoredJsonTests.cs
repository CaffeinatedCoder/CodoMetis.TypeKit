using System.Text.Json;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using NodaTime;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// <see cref="StoredJsonConverterFactory"/>: JSON the application stored itself is read back without
/// the rules, in exactly the format the generated converter writes, and only where it is registered.
/// </summary>
public sealed class StoredJsonTests
{
    private static readonly JsonSerializerOptions Store = new() { Converters = { new StoredJsonConverterFactory() } };

    /// <summary>One value per JSON strategy, as in <see cref="JsonTests"/>.</summary>
    public static TheoryData<string> Formats => ["string", "Guid", "int", "decimal", "bool", "DateTime", "DateOnly", "DateTimeOffset", "TimeOnly", "Instant", "LocalDate", "Uri"];

    private static object Sample(string format) => format switch
    {
        "string"         => ProbeName.From("a b"),
        "Guid"           => ProbeId.From(Guid.Parse("0199a3f4-1c00-7000-8000-000000000001")),
        "int"            => ProbeCount.From(42),
        "decimal"        => ProbeAmount.From(1.50m),
        "bool"           => ProbeFlag.From(true),
        "DateTime"       => ProbeTimestamp.From(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc)),
        "DateOnly"       => ProbeDate.From(new DateOnly(2026, 9, 27)),
        "DateTimeOffset" => ProbeMoment.From(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.FromHours(2))),
        "TimeOnly"       => ProbeTime.From(new TimeOnly(13, 45, 30)),
        "Instant"        => ProbeInstant.From(Instant.FromUtc(2026, 9, 27, 12, 0)),
        "LocalDate"      => ProbeLocalDate.From(new LocalDate(2026, 9, 27)),
        "Uri"            => ProbeUri.From(new Uri("https://example.com/a")),
        _                => throw new ArgumentOutOfRangeException(nameof(format))
    };

    /// <summary>A value today's rules refuse, stored when they did not exist: the case the factory is for.</summary>
    [Fact]
    public void A_stored_value_the_rules_now_refuse_is_read_back()
    {
        JsonSerializer.Deserialize<ProbePercentage>("101", Store).Value.ShouldBe(101);
        JsonSerializer.Deserialize<ProbeCode>("\"lower\"", Store).Value.ShouldBe("lower");
    }

    [Fact]
    public void A_stored_dictionary_key_the_rules_now_refuse_is_read_back() =>
        JsonSerializer.Deserialize<Dictionary<ProbeCode, int>>("{\"ab\":1}", Store)!.Keys.ShouldHaveSingleItem().Value.ShouldBe("ab");

    /// <summary>The control: the same JSON through the default options is input, and is refused.</summary>
    [Fact]
    public void Without_the_factory_the_same_JSON_is_refused() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProbePercentage>("101"));

    /// <summary>
    /// The factory hands out the generated converter, so what a store writes is what an ordinary
    /// write produces, for every format.
    /// </summary>
    [Theory]
    [MemberData(nameof(Formats))]
    public void A_store_writes_what_the_generated_converter_writes(string format)
    {
        var value = Sample(format);

        JsonSerializer.Serialize(value, value.GetType(), Store).ShouldBe(JsonSerializer.Serialize(value, value.GetType()));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void A_store_reads_back_what_it_wrote(string format)
    {
        var value = Sample(format);

        JsonSerializer.Deserialize(JsonSerializer.Serialize(value, value.GetType(), Store), value.GetType(), Store).ShouldBe(value);
    }

    [Fact]
    public void A_JSON_null_is_still_not_a_value_object() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProbeCode>("null", Store));

    [Fact]
    public void The_factory_claims_value_objects_only()
    {
        var factory = new StoredJsonConverterFactory();

        factory.CanConvert(typeof(ProbeCode)).ShouldBeTrue();
        factory.CanConvert(typeof(ProbeId)).ShouldBeTrue();
        factory.CanConvert(typeof(string)).ShouldBeFalse();
        factory.CanConvert(typeof(Option<int>)).ShouldBeFalse();
    }
}
