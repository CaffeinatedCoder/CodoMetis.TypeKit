using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.Facts;

/// <summary>Writes a decimal as a string: <c>"1.2"</c>.</summary>
public sealed class DecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Writes a decimal as a number: <c>1.2</c>.</summary>
public sealed class DecimalNumberConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>A resolver that answers for one type only, with a contract over the converter it is given.</summary>
public sealed class ResolverOf<T>(JsonConverter<T> converter) : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
        type == typeof(T) ? JsonMetadataServices.CreateValueInfo<T>(options, converter) : null;
}

public sealed record Holder(decimal? Maybe, decimal Plain);
