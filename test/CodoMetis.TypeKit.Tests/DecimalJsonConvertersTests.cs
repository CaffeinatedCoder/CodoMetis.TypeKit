using System.Text.Json;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <see cref="DecimalNumberJsonConverter"/> and <see cref="DecimalStringJsonConverter"/>: one spelling per
/// value, as a value and as a dictionary key, and either token read, refused as the serializer refuses a
/// decimal it cannot read.
/// </summary>
public sealed class DecimalJsonConvertersTests
{
    private static readonly JsonSerializerOptions Numbers = new() { Converters = { new DecimalNumberJsonConverter() } };
    private static readonly JsonSerializerOptions Strings = new() { Converters = { new DecimalStringJsonConverter() } };

    /// <summary>A negative zero, which the literal <c>-0.0m</c> does not make: the compiler folds it to zero.</summary>
    private static readonly decimal NegativeZero = new(0, 0, 0, isNegative: true, scale: 2);

    public static TheoryData<decimal, string> Spellings => new()
    {
        { 7.8000m, "7.8" },
        { -7.80m, "-7.8" },
        { 100m, "100" },
        { 100.00m, "100" },
        { 0.000m, "0" },
        { 0.0000000000000000000000000001m, "0.0000000000000000000000000001" },
        { 1.0000000000000000000000000000m, "1" },
        { decimal.MaxValue, "79228162514264337593543950335" },
        { decimal.MinValue, "-79228162514264337593543950335" },
        { 1m / 3m, "0.3333333333333333333333333333" },
    };

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_value_is_written_at_its_smallest_scale(decimal value, string spelled)
    {
        JsonSerializer.Serialize(value, Numbers).ShouldBe(spelled);
        JsonSerializer.Serialize(value, Strings).ShouldBe($"\"{spelled}\"");
    }

    /// <summary>A zero keeps its scale, and its sign in its bits; the serializer writes the scale.</summary>
    [Fact]
    public void A_zero_of_any_scale_or_sign_is_written_as_zero()
    {
        decimal.IsNegative(NegativeZero).ShouldBeTrue("the control: the bits keep the sign");
        JsonSerializer.Serialize(NegativeZero).ShouldBe("0.00", "the control: the serializer writes the scale");
        JsonSerializer.Serialize(NegativeZero, Numbers).ShouldBe("0");
        JsonSerializer.Serialize(NegativeZero, Strings).ShouldBe("\"0\"");
    }

    /// <summary>The serializer writes a dictionary key with its own converter unless a converter of one's own writes it; this one does.</summary>
    [Fact]
    public void A_dictionary_key_is_written_at_its_smallest_scale_and_read_back()
    {
        var keyed = new Dictionary<decimal, int> { [7.80m] = 1, [NegativeZero] = 2 };

        JsonSerializer.Serialize(keyed, Numbers).ShouldBe("""{"7.8":1,"0":2}""");
        JsonSerializer.Serialize(keyed, Strings).ShouldBe("""{"7.8":1,"0":2}""");
        JsonSerializer.Deserialize<Dictionary<decimal, int>>("""{"7.80":1}""", Numbers)!.Keys.ShouldHaveSingleItem().ShouldBe(7.80m);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Dictionary<decimal, int>>("""{"seven":1}""", Numbers));
    }

    [Fact]
    public void A_nullable_and_a_list_are_written_through_the_converter()
    {
        JsonSerializer.Serialize<decimal?>(7.80m, Strings).ShouldBe("\"7.8\"");
        JsonSerializer.Serialize<decimal?>(null, Strings).ShouldBe("null");
        JsonSerializer.Serialize(new List<decimal> { 1.10m, 2.000m }, Numbers).ShouldBe("[1.1,2]");
    }

    /// <summary>
    /// Both read both tokens, so one converter serves a store whose old documents hold numbers and a
    /// client that sends strings; reading keeps the scale it is given.
    /// </summary>
    [Theory]
    [InlineData("7.80")]
    [InlineData("\"7.80\"")]
    [InlineData("\"\\u0037.80\"")]
    public void Either_token_is_read_and_keeps_its_scale(string json)
    {
        foreach (var options in new[] { Numbers, Strings })
        {
            var read = JsonSerializer.Deserialize<decimal>(json, options);
            read.ShouldBe(7.8m);
            read.Scale.ShouldBe((byte)2);
        }
    }

    /// <summary>A string is read as the serializer reads a quoted number: the number's grammar and nothing around it.</summary>
    [Theory]
    [InlineData("\"seven\"")]
    [InlineData("\" 7.8\"")]
    [InlineData("\"7.8 \"")]
    [InlineData("\"\"")]
    [InlineData("\"1,5\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("1e40")]
    public void What_is_no_decimal_is_refused_as_the_serializer_refuses_it(string json)
    {
        var builtIn = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<decimal>(json.StartsWith('"') ? "\"x\"" : json));

        foreach (var options in new[] { Numbers, Strings })
        {
            var refusal = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<decimal>(json, options));
            refusal.Message.ShouldStartWith("The JSON value could not be converted to System.Decimal.", Case.Sensitive, $"{json}: {refusal.Message}");
            refusal.Path.ShouldBe("$");
            if (json.Length > 6) refusal.Message.ShouldNotContain(json.Trim('"'), Case.Sensitive);
        }

        builtIn.Message.ShouldStartWith("The JSON value could not be converted to System.Decimal.", Case.Sensitive, "the control: the serializer's own message");
    }

    [Fact]
    public void A_refusal_in_a_property_names_its_path()
    {
        var refusal = Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Priced>("""{"Price":"seven"}""", Strings));

        refusal.Path.ShouldBe("$.Price");
    }

    [Fact]
    public void Each_describes_what_it_writes_and_nothing_else()
    {
        new DecimalNumberJsonConverter().DescribeJson(typeof(decimal), Numbers)!.Schema!.ToJsonString().ShouldBe("""{"type":"number","format":"decimal"}""");
        new DecimalStringJsonConverter().DescribeJson(typeof(decimal), Strings)!.Schema!.ToJsonString().ShouldBe("""{"type":"string","format":"decimal","pattern":"^-?(?:0|[1-9]\\d*)(?:\\.\\d*[1-9])?$"}""");
        new DecimalStringJsonConverter().DescribeJson(typeof(double), Strings).ShouldBeNull();
    }

    /// <summary>The string pattern admits exactly what the converter writes, the negative zero aside, which it never writes.</summary>
    [Theory]
    [MemberData(nameof(Spellings))]
    public void The_pattern_admits_every_spelling_written(decimal value, string spelled)
    {
        var pattern = new System.Text.RegularExpressions.Regex(new DecimalStringJsonConverter().DescribeJson(typeof(decimal), Strings)!.Schema!["pattern"]!.GetValue<string>());

        pattern.IsMatch(spelled).ShouldBeTrue(spelled);
        pattern.IsMatch(JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(value, Strings))!).ShouldBeTrue();
        pattern.IsMatch("7.80").ShouldBeFalse("a trailing zero is never written");
        pattern.IsMatch("07.8").ShouldBeFalse("nor a leading one");
        pattern.IsMatch("1E-07").ShouldBeFalse("nor an exponent");
    }

    public sealed record Priced(decimal Price);
}
