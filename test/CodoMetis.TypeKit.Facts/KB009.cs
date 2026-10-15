using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-009: a resolver modifier that sets <see cref="JsonPropertyInfo.CustomConverter"/> puts
/// its converter in charge of the property, over the converter the property's <c>[JsonConverter]</c> names.
/// </summary>
public sealed class KB009
{
    public sealed record Attributed([property: JsonConverter(typeof(DecimalStringConverter))] decimal? Value);

    /// <summary>A converter of the nullable type that writes whether there is a value.</summary>
    public sealed class Presence : JsonConverter<decimal?>
    {
        public override bool HandleNull => true;

        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetBoolean() ? 0m : null;

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options) => writer.WriteBooleanValue(value is not null);
    }

    [Fact]
    public void The_attribute_s_converter_writes_a_nullable_property_on_its_own() =>
        JsonSerializer.Serialize(new Attributed(1.2m), JsonSerializerOptions.Web).ShouldBe("""{"value":"1.2"}""");

    [Fact]
    public void A_modifier_s_converter_writes_over_the_attribute_s()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(info =>
            {
                if (info.Type != typeof(Attributed)) return;
                foreach (var property in info.Properties) property.CustomConverter = new Presence();
            }),
        };

        JsonSerializer.Serialize(new Attributed(1.2m), options).ShouldBe("""{"value":true}""");
    }
}
