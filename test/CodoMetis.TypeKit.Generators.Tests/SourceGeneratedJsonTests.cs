using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using NodaTime;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// The generated JSON converter under a source-generated <see cref="JsonSerializerContext"/>, the
/// only resolver under Native AOT. A context sees a value object's <c>[JsonConverter]</c> and never
/// the type it wraps, so the converter must not need that type's contract from the context: a value
/// object wrapping a <see cref="decimal"/>, a <see cref="Uri"/> or an enum was refused outright.
/// </summary>
public sealed class SourceGeneratedJsonTests
{
    /// <summary>The context as the only resolver, as under Native AOT.</summary>
    private static readonly JsonSerializerOptions ContextOnly = new() { TypeInfoResolver = ValueObjectsOnly.Default };

    /// <summary>What the tests below rely on: the context has no contract for the wrapped types.</summary>
    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(Uri))]
    [InlineData(typeof(DayOfWeek))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(Instant))]
    public void The_context_has_no_contract_for_a_wrapped_type(Type wrapped) =>
        ValueObjectsOnly.Default.GetTypeInfo(wrapped).ShouldBeNull();

    [Theory]
    [MemberData(nameof(JsonTests.CaseNames), MemberType = typeof(JsonTests))]
    public void A_value_object_is_written_and_read_as_it_is_with_reflection(string @case)
    {
        var (value, json) = JsonTests.Cases[@case];

        JsonSerializer.Serialize(value, value.GetType(), ContextOnly).ShouldBe(json);
        JsonSerializer.Deserialize(json, value.GetType(), ContextOnly).ShouldBe(value);
    }

    [Fact]
    public void A_dictionary_key_round_trips_in_the_wrapped_type_s_key_format()
    {
        var byDay    = new Dictionary<ProbeWeekday, int> { [ProbeWeekday.From(DayOfWeek.Monday)] = 1 };
        var byLink   = new Dictionary<ProbeUri, int> { [ProbeUri.From(new Uri("https://example.com/a"))] = 1 };
        var byAmount = new Dictionary<ProbeAmount, int> { [ProbeAmount.From(1.5m)] = 1 };

        JsonSerializer.Serialize(byDay, ContextOnly).ShouldBe("{\"Monday\":1}");
        JsonSerializer.Serialize(byLink, ContextOnly).ShouldBe("{\"https://example.com/a\":1}");
        JsonSerializer.Serialize(byAmount, ContextOnly).ShouldBe("{\"1.5\":1}");
        JsonSerializer.Deserialize<Dictionary<ProbeWeekday, int>>("{\"Monday\":1}", ContextOnly).ShouldBe(byDay);
        JsonSerializer.Deserialize<Dictionary<ProbeUri, int>>("{\"https://example.com/a\":1}", ContextOnly).ShouldBe(byLink);
    }

    /// <summary>The contract made for a wrapped number follows the options, as the serializer's own does.</summary>
    [Fact]
    public void The_options_number_handling_applies_to_a_wrapped_number_the_context_never_saw()
    {
        const JsonNumberHandling handling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString;
        var options = new JsonSerializerOptions(ContextOnly) { NumberHandling = handling };

        JsonSerializer.Serialize(ProbeAmount.From(1.5m), options).ShouldBe(JsonSerializer.Serialize(ProbeAmount.From(1.5m), new JsonSerializerOptions { NumberHandling = handling }));
        JsonSerializer.Serialize(ProbeAmount.From(1.5m), options).ShouldBe("\"1.5\"");
        JsonSerializer.Deserialize<ProbeAmount>("\"2.5\"", options).Value.ShouldBe(2.5m);
    }

    [Fact]
    public void A_converter_on_the_options_applies_to_a_wrapped_type_the_context_never_saw()
    {
        var options = new JsonSerializerOptions(ContextOnly) { Converters = { new JsonStringEnumConverter<DayOfWeek>() } };

        JsonSerializer.Serialize(ProbeWeekday.From(DayOfWeek.Monday), options).ShouldBe("\"Monday\"");
        JsonSerializer.Deserialize<ProbeWeekday>("\"Friday\"", options).Value.ShouldBe(DayOfWeek.Friday);
    }

    /// <summary>Create still applies, and a refusal is still a <see cref="JsonException"/>.</summary>
    [Fact]
    public void A_validated_value_object_is_still_refused() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProbePercentage>("101", ContextOnly));

    [Fact]
    public void Stored_JSON_is_read_without_the_rules() =>
        JsonSerializer.Deserialize<ProbePercentage>("101", new JsonSerializerOptions(ContextOnly) { Converters = { new StoredJsonConverterFactory() } })
                      .Value.ShouldBe(101);
}

[JsonSerializable(typeof(ProbeName))]
[JsonSerializable(typeof(ProbeId))]
[JsonSerializable(typeof(ProbeCount))]
[JsonSerializable(typeof(ProbeAmount))]
[JsonSerializable(typeof(ProbeFlag))]
[JsonSerializable(typeof(ProbeTimestamp))]
[JsonSerializable(typeof(ProbeDate))]
[JsonSerializable(typeof(ProbeMoment))]
[JsonSerializable(typeof(ProbeTime))]
[JsonSerializable(typeof(ProbeInstant))]
[JsonSerializable(typeof(ProbeLocalDate))]
[JsonSerializable(typeof(ProbeUri))]
[JsonSerializable(typeof(ProbeWeekday))]
[JsonSerializable(typeof(ProbeLabel))]
[JsonSerializable(typeof(ProbePercentage))]
[JsonSerializable(typeof(Dictionary<ProbeWeekday, int>))]
[JsonSerializable(typeof(Dictionary<ProbeUri, int>))]
[JsonSerializable(typeof(Dictionary<ProbeAmount, int>))]
internal sealed partial class ValueObjectsOnly : JsonSerializerContext;
