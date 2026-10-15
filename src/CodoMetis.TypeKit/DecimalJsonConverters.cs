using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>
/// A <see cref="decimal"/> as a JSON number in one spelling per value: <c>7.8</c> for <c>7.8m</c>,
/// <c>7.80m</c> and <c>7.8000m</c> alike. Reads a number, or a string holding one.
/// </summary>
/// <remarks>
/// <para>
/// A decimal carries its scale in its bits, so two equal values print differently, and System.Text.Json
/// writes whichever scale the value arrived with: a request keeps the caller's, a <c>numeric(6,4)</c>
/// column comes back with four decimals. This converter writes the minimal scale, trailing fractional
/// zeros dropped and a zero of any scale or sign written <c>0</c>, as a value and as a dictionary key, so a digest, a
/// diff or a replay of what was written sees one spelling. Reading keeps the scale it is given.
/// </para>
/// <para>
/// Registered on the options, <c>options.Converters.Add(new DecimalNumberJsonConverter())</c>, it reaches
/// every <see cref="decimal"/> and <see cref="Nullable{T}"/> of one, and every value object over one,
/// whose JSON is the wrapped type's. A store whose earlier documents hold numbers keeps reading them, and
/// a string written by <see cref="DecimalStringJsonConverter"/> reads too, so one converter serves both.
/// A value no decimal holds, or a token that is neither a number nor a string, is refused as the
/// serializer refuses it for a <see cref="decimal"/>.
/// </para>
/// </remarks>
public sealed class DecimalNumberJsonConverter : JsonConverter<decimal>, IDescribedJsonConverter
{
    /// <summary>What it writes: <c>{"type":"number","format":"decimal"}</c>.</summary>
    /// <param name="typeToConvert">The type, <see cref="decimal"/>.</param>
    /// <param name="options">The options, unused.</param>
    /// <returns>The description, or <see langword="null"/> for another type.</returns>
    public JsonSchemaDescription? DescribeJson(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(decimal) ? DecimalJson.NumberSchema : null;

    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecimalJson.Read(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(DecimalJson.Minimal(value));
    }

    /// <inheritdoc />
    public override decimal ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecimalJson.ReadText(ref reader);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(DecimalJson.Text(value));
    }
}

/// <summary>
/// A <see cref="decimal"/> as a JSON string in one spelling per value: <c>"7.8"</c> for <c>7.8m</c>,
/// <c>7.80m</c> and <c>7.8000m</c> alike, for a client whose number is a double. Reads a string holding a
/// number, or a number.
/// </summary>
/// <remarks>
/// <para>
/// A double holds 15 to 17 significant digits and a decimal 28 to 29, so a client that parses a JSON
/// number into a double rounds what a decimal says exactly; a string reaches it as written. The string is
/// the minimal-scale spelling <see cref="DecimalNumberJsonConverter"/> writes as a number: digits, an
/// optional minus and an optional fraction without trailing zeros, never an exponent.
/// </para>
/// <para>
/// Registered on the host's JSON options, CodoMetis.TypeKit.AspNetCore's <c>AddTypeKit()</c> describes
/// every decimal the document shows as that string, a value object's over one, a list's elements and a
/// dictionary's values included. Reading takes a number too, so a request written before the switch
/// still binds. Unlike <see cref="System.Text.Json.Serialization.JsonNumberHandling.WriteAsString"/>, it
/// changes decimals alone, and normalises them.
/// </para>
/// </remarks>
public sealed class DecimalStringJsonConverter : JsonConverter<decimal>, IDescribedJsonConverter
{
    /// <summary>What it writes: <c>{"type":"string","format":"decimal","pattern":"^-?(?:0|[1-9]\d*)(?:\.\d*[1-9])?$"}</c>, the minimal spelling.</summary>
    /// <param name="typeToConvert">The type, <see cref="decimal"/>.</param>
    /// <param name="options">The options, unused.</param>
    /// <returns>The description, or <see langword="null"/> for another type.</returns>
    public JsonSchemaDescription? DescribeJson(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(decimal) ? DecimalJson.StringSchema : null;

    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecimalJson.Read(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(DecimalJson.Text(value));
    }

    /// <inheritdoc />
    public override decimal ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecimalJson.ReadText(ref reader);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(DecimalJson.Text(value));
    }
}

/// <summary>What the two decimal converters share: the minimal-scale spelling, and reading either token.</summary>
internal static class DecimalJson
{
    /// <summary>One, at the largest scale a decimal has.</summary>
    /// <remarks>
    /// Division chooses the smallest scale that holds the quotient exactly, so dividing by this one keeps
    /// the value and drops precisely its trailing fractional zeros, for every decimal, <c>MaxValue</c> and
    /// a scale-28 value included. <c>ToString("G29")</c> drops them too, and writes <c>1E-07</c>.
    /// </remarks>
    private const decimal OneAtLargestScale = 1.0000000000000000000000000000m;

    /// <summary>A decimal written as a number.</summary>
    public static readonly JsonSchemaDescription NumberSchema = JsonSchemaDescription.Of(new JsonObject { ["type"] = "number", ["format"] = "decimal" });

    /// <summary>
    /// A decimal written as a string in its minimal spelling: no leading zero but the one before a point,
    /// a fraction ending in a digit that is not zero, never an exponent.
    /// </summary>
    public static readonly JsonSchemaDescription StringSchema = JsonSchemaDescription.Of(new JsonObject
    {
        ["type"] = "string",
        ["format"] = "decimal",
        ["pattern"] = @"^-?(?:0|[1-9]\d*)(?:\.\d*[1-9])?$",
    });

    /// <summary>The value at its smallest scale; a zero of any scale or sign is <c>0</c>.</summary>
    public static decimal Minimal(decimal value) => value == 0m ? 0m : value / OneAtLargestScale;

    /// <summary>The minimal-scale spelling, invariant, never with an exponent.</summary>
    public static string Text(decimal value) => Minimal(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>A number, or a string holding one.</summary>
    public static decimal Read(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.TryGetDecimal(out var value) ? value : throw new JsonException(),
        JsonTokenType.String => ReadText(ref reader),
        _ => throw new JsonException(),
    };

    /// <summary>
    /// A string or a property name holding a JSON number, read as the serializer reads a quoted number:
    /// the number's grammar, nothing before or after it.
    /// </summary>
    /// <remarks>
    /// The parameterless <see cref="JsonException"/> lets the serializer write its own message, the one it
    /// writes for a <see cref="decimal"/> it cannot read, with the path and without the value.
    /// </remarks>
    public static decimal ReadText(ref Utf8JsonReader reader)
    {
        if (reader.ValueIsEscaped || reader.HasValueSequence)
        {
            var text = reader.GetString();
            return text is not null && TryParse(Encoding.UTF8.GetBytes(text), out var unescaped) ? unescaped : throw new JsonException();
        }

        return TryParse(reader.ValueSpan, out var value) ? value : throw new JsonException();
    }

    private static bool TryParse(ReadOnlySpan<byte> utf8, out decimal value) =>
        Utf8Parser.TryParse(utf8, out value, out var consumed) && consumed == utf8.Length && utf8.Length > 0;
}
