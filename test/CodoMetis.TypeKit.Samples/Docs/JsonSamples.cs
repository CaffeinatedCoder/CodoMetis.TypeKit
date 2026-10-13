// sample: json/usings
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
// end sample
using System.Text.Json.Serialization.Metadata;

namespace ReadmeSamples.Docs.Json;

// sample: json/types
public readonly partial record struct Quantity : IValue<int>;

public readonly partial record struct Weekday : IValue<DayOfWeek>;

public enum EmailFault { NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value) : Result.Error(EmailFault.NoAt);
}

public sealed record Shipment(Quantity Boxes, Weekday Day, Dictionary<Weekday, Quantity> Plan);

public sealed record Primitives(int Boxes, DayOfWeek Day, Dictionary<DayOfWeek, int> Plan);
// end sample

// sample: json/parcel
public sealed record Parcel(Quantity Boxes, Email? Contact);
// end sample

// sample: json/product-code
public enum CodeFault { NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value) =>
        value == value.ToUpperInvariant() ? new ProductCode(value) : Result.Error(CodeFault.NotUpperCase);   // the rule added after "abc" was stored
}
// end sample

// sample: json/profile
public sealed record Profile(Option<string> Nickname);
// end sample

// sample: json/option-converter
public sealed class OptionAsNullableConverter<T> : JsonConverter<Option<T>> where T : notnull
{
    public override Option<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? Option.None()
            : Option.Some(JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))!);   // the typed overload: no reflection, so Native AOT keeps it

    public override void Write(Utf8JsonWriter writer, Option<T> value, JsonSerializerOptions options)
    {
        if (value.TryGetValue(out var content)) JsonSerializer.Serialize(writer, content, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
        else writer.WriteNullValue();
    }
}
// end sample

// sample: json/contexts
[JsonSerializable(typeof(Shipment))]
internal partial class ShipmentJson : JsonSerializerContext;

[JsonSerializable(typeof(Shipment))]
[JsonSerializable(typeof(DayOfWeek))]   // listed, so the string enum converter reaches what Weekday wraps
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal partial class NamedShipmentJson : JsonSerializerContext;
// end sample

public static class Usage
{
    public static (string Json, bool Parity, Shipment Quoted) Parity()
    {
        // sample: json/parity
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var shipment = new Shipment(Quantity.From(3), Weekday.From(DayOfWeek.Friday), new() { [Weekday.From(DayOfWeek.Friday)] = Quantity.From(3) });
        var primitives = new Primitives(3, DayOfWeek.Friday, new() { [DayOfWeek.Friday] = 3 });

        string json = JsonSerializer.Serialize(shipment, options);                      // {"boxes":3,"day":"Friday","plan":{"Friday":3}}
        bool parity = json == JsonSerializer.Serialize(primitives, options);            // true, the same bytes as the primitives
        Shipment quoted = JsonSerializer.Deserialize<Shipment>("""{"boxes":"3","day":"Friday","plan":{}}""", options)!;   // "3" read as 3: the web defaults allow a quoted number
        // end sample

        return (json, parity, quoted);
    }

    public static void Refusals()
    {
        // sample: json/refusals
        JsonSerializer.Deserialize<Email>("\"nobody\"");       // JsonException naming Email and NoAt, never the text
        JsonSerializer.Deserialize<Quantity>("\"many\"");      // JsonException naming Quantity and Int32, with no inner exception that quotes it
        JsonSerializer.Deserialize<Quantity>("null");          // JsonException naming Quantity and null, refused for a struct
        // end sample
    }

    public static int NullAndMissing()
    {
        // sample: json/null-and-missing
        var parcel = JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""")!;
        int boxes = parcel.Boxes.Value;                                        // 0, a missing property calls no converter and stays default

        var strict = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
        JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""", strict);    // JsonException naming Parcel and Boxes
        // end sample

        return boxes;
    }

    public static string Stored()
    {
        // sample: json/stored
        var store = new JsonSerializerOptions { Converters = { new StoredJsonConverterFactory() } };

        ProductCode stored = JsonSerializer.Deserialize<ProductCode>("\"abc\"", store);                                   // read without the rules
        string state = stored.Revalidate().Match(_ => "still valid", fault => $"now refused: {fault}");                  // "now refused: NotUpperCase"
        JsonSerializer.Deserialize<ProductCode>("\"abc\"");                                                               // JsonException naming ProductCode and NotUpperCase, the same document as input
        // end sample

        return state;
    }

    public static void Refused()
    {
        // sample: json/option-refused
        JsonSerializer.Serialize(new Profile(Option.Some("Annie")));   // NotSupportedException naming Option and nullable, the alternative
        // end sample
    }

    public static (string Written, string Absent, Profile Back) Wire()
    {
        // sample: json/option-wire
        var wire = new JsonSerializerOptions { Converters = { new OptionAsNullableConverter<string>() } };

        string written = JsonSerializer.Serialize(new Profile(Option.Some("Annie")), wire);   // {"Nickname":"Annie"}
        string absent = JsonSerializer.Serialize(new Profile(Option.None()), wire);          // {"Nickname":null}
        Profile back = JsonSerializer.Deserialize<Profile>("""{"Nickname":null}""", wire)!;   // None
        // end sample

        return (written, absent, back);
    }

    public static (string Numbered, string Named) Contexts(Shipment shipment)
    {
        // sample: json/contexts-use
        string numbered = JsonSerializer.Serialize(shipment, ShipmentJson.Default.Shipment);        // {"Boxes":3,"Day":5,"Plan":{"Friday":3}}
        string named = JsonSerializer.Serialize(shipment, NamedShipmentJson.Default.Shipment);      // {"Boxes":3,"Day":"Friday","Plan":{"Friday":3}}
        // end sample

        return (numbered, named);
    }
}

public sealed class JsonTests
{
    private const string Readme = "docs/json.md";

    private static string Json(string json) => json;

    [Fact]
    public void The_JSON_is_the_primitives_JSON_as_the_comments_say()
    {
        var (json, parity, quoted) = Usage.Parity();

        json.ShouldBeShownIn(Readme, Json, "JsonSerializer.Serialize(shipment, options)");
        parity.ShouldBeShownIn(Readme, p => p ? "true" : "false", "json == JsonSerializer.Serialize(primitives, options)");
        quoted.Boxes.ShouldBe(Quantity.From(3));
        quoted.Day.ShouldBe(Weekday.From(DayOfWeek.Friday));
    }

    [Fact]
    public void Each_refusal_is_the_exception_the_comment_says()
    {
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Email>("\"nobody\""), Readme, input: "nobody");
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Quantity>("\"many\""), Readme, input: "many");
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Quantity>("null"), Readme, input: "\"null\"");
        Should.Throw<JsonException>(Usage.Refusals);
    }

    [Fact]
    public void A_missing_property_stays_default_until_the_options_say_otherwise()
    {
        var parcel = JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""")!;
        var strict = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };

        parcel.Boxes.Value.ShouldBeShownIn(Readme, boxes => boxes.ToString());
        parcel.Contact.ShouldBeNull();
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""", strict), Readme, input: "Annie");
        Should.Throw<JsonException>(() => Usage.NullAndMissing());
    }

    [Fact]
    public void A_stored_document_reads_without_the_rules_as_the_comments_say()
    {
        var store = new JsonSerializerOptions { Converters = { new StoredJsonConverterFactory() } };
        var stored = JsonSerializer.Deserialize<ProductCode>("\"abc\"", store);

        stored.Value.ShouldBe("abc");
        stored.Revalidate().Match(_ => "still valid", fault => $"now refused: {fault}").ShouldBeShownIn(Readme, state => $"\"{state}\"");
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<ProductCode>("\"abc\""), Readme, input: "abc");
        Should.Throw<JsonException>(() => Usage.Stored());
    }

    [Fact]
    public void An_Option_is_refused_unless_a_converter_is_registered()
    {
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Serialize(new Profile(Option.Some("Annie"))), Readme, input: "Annie");
        Should.Throw<NotSupportedException>(Usage.Refused);

        var (written, absent, back) = Usage.Wire();
        var wire = new JsonSerializerOptions { Converters = { new OptionAsNullableConverter<string>() } };

        JsonSerializer.Serialize(new Profile(Option.Some("Annie")), wire).ShouldBeShownIn(Readme, Json);
        JsonSerializer.Serialize(new Profile(Option.None()), wire).ShouldBeShownIn(Readme, Json);
        JsonSerializer.Deserialize<Profile>("""{"Nickname":null}""", wire)!.ShouldBeShownIn(Readme, profile => profile.Nickname.IsNone() ? "None" : "Some", "JsonSerializer.Deserialize<Profile>(\"\"\"{\"Nickname\":null}\"\"\", wire)!");
        (written, absent, back.Nickname.IsNone()).ShouldBe(("""{"Nickname":"Annie"}""", """{"Nickname":null}""", true));
        JsonSerializer.Deserialize<Profile>("""{"Nickname":"Bo"}""", wire)!.Nickname.ShouldBe(Option.Some("Bo"));
    }

    [Fact]
    public void A_context_lists_the_value_objects_and_the_enum_decides_the_names()
    {
        var shipment = new Shipment(Quantity.From(3), Weekday.From(DayOfWeek.Friday), new() { [Weekday.From(DayOfWeek.Friday)] = Quantity.From(3) });

        var (numbered, named) = Usage.Contexts(shipment);

        numbered.ShouldBeShownIn(Readme, Json, "JsonSerializer.Serialize(shipment, ShipmentJson.Default.Shipment)");
        named.ShouldBeShownIn(Readme, Json, "JsonSerializer.Serialize(shipment, NamedShipmentJson.Default.Shipment)");
        var back = JsonSerializer.Deserialize(named, NamedShipmentJson.Default.Shipment)!;
        (back.Boxes, back.Day).ShouldBe((shipment.Boxes, shipment.Day));
        back.Plan.ShouldBe(shipment.Plan);
    }
}
