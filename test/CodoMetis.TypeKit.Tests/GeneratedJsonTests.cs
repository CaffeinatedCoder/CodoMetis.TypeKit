using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.TypeKit.CompilerServices;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="GeneratedJson.TypeInfo{T}"/>, the wrapped type's contract the generated JSON converter
/// reads and writes through, tested without a generator.
/// </summary>
public sealed class GeneratedJsonTests
{
    /// <summary>A resolver that knows nothing, as a source-generated context knows nothing of a wrapped type.</summary>
    private static JsonSerializerOptions Empty() => new() { TypeInfoResolver = JsonTypeInfoResolver.Combine() };

    [Fact]
    public void The_resolver_s_own_contract_is_used_where_it_has_one()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.MakeReadOnly();

        GeneratedJson.TypeInfo(options, JsonMetadataServices.DecimalConverter).ShouldBeSameAs(options.GetTypeInfo(typeof(decimal)));
    }

    [Fact]
    public void Without_one_the_built_in_converter_is_used_once_per_options()
    {
        var options = Empty();

        var contract = GeneratedJson.TypeInfo(options, JsonMetadataServices.DecimalConverter);

        contract.Converter.ShouldBeSameAs(JsonMetadataServices.DecimalConverter);
        GeneratedJson.TypeInfo(options, JsonMetadataServices.DecimalConverter).ShouldBeSameAs(contract);
        JsonSerializer.Serialize(1.5m, contract).ShouldBe("1.5");
    }

    [Fact]
    public void A_converter_on_the_options_comes_before_the_built_in_one()
    {
        var options = Empty();
        options.Converters.Add(new JsonStringEnumConverter<DayOfWeek>());

        JsonSerializer.Serialize(DayOfWeek.Monday, GeneratedJson.TypeInfo(options, JsonMetadataServices.GetEnumConverter<DayOfWeek>(options))).ShouldBe("\"Monday\"");
    }

    [Fact]
    public void The_type_s_own_JsonConverter_attribute_comes_before_the_built_in_one() =>
        JsonSerializer.Serialize(new Tagged("x"), GeneratedJson.TypeInfo<Tagged>(Empty(), builtIn: null)).ShouldBe("\"tagged:x\"");

    /// <summary>A type nothing can write is refused with the serializer's own message, which names the type and the resolver.</summary>
    [Fact]
    public void A_type_nothing_can_write_is_refused_by_name() =>
        Should.Throw<NotSupportedException>(() => GeneratedJson.TypeInfo<Untagged>(Empty(), builtIn: null)).Message.ShouldContain(nameof(Untagged));

    [JsonConverter(typeof(TaggedConverter))]
    public sealed record Tagged(string Text);

    public sealed record Untagged(string Text);

    public sealed class TaggedConverter : JsonConverter<Tagged>
    {
        public override Tagged Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetString()![7..]);

        public override void Write(Utf8JsonWriter writer, Tagged value, JsonSerializerOptions options) => writer.WriteStringValue($"tagged:{value.Text}");
    }
}
