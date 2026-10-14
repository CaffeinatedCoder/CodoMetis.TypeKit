# JSON

A value object's JSON is its wrapped type's under whatever options the application uses, as a value
and as a dictionary key; the one addition is a `DateTime`, written as UTC (below). The generated converter reads and writes through the
serializer's own contract for the wrapped type, so replacing a `Guid` property by an `OrderId`
changes no document, and only what the value object refuses changes. `Option` and `Result` are not
wire types and refuse System.Text.Json in both directions. The
[Generators README](../src/CodoMetis.TypeKit.Generators/README.md#json) and the
[base README](../src/CodoMetis.TypeKit/README.md#not-wire-types) are the reference; this page is the
set of situations.

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
```

```csharp
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
```

## The wrapped type's JSON

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
var shipment = new Shipment(Quantity.From(3), Weekday.From(DayOfWeek.Friday), new() { [Weekday.From(DayOfWeek.Friday)] = Quantity.From(3) });
var primitives = new Primitives(3, DayOfWeek.Friday, new() { [DayOfWeek.Friday] = 3 });

string json = JsonSerializer.Serialize(shipment, options);                      // {"boxes":3,"day":"Friday","plan":{"Friday":3}}
bool parity = json == JsonSerializer.Serialize(primitives, options);            // true, the same bytes as the primitives
Shipment quoted = JsonSerializer.Deserialize<Shipment>("""{"boxes":"3","day":"Friday","plan":{}}""", options)!;   // "3" read as 3: the web defaults allow a quoted number
```

Whatever the options say about the wrapped type applies: a converter registered for it, number
handling, an enum converter, a dictionary key policy. A test in this repository compares the two
for every wrapped-type family under several sets of options. One addition of the converter's own: a
`DateTime` is written as UTC, a `Local` one converted and an `Unspecified` one taken as UTC with its
digits kept, so a stored instant reads back as the instant it was.

One exception: number handling given by an attribute rather than by the options. A
`[JsonNumberHandling]` on a record or class applies to the number properties the serializer writes
itself, and a value object's converter cannot see it. Under
`[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]`,
`record Counted(int Count)` writes `{"Count":"5"}` and reads `"5"`, while `record Counted(Quantity Count)`
writes `{"Count":5}` and refuses `"5"` with a `JsonException`, which ASP.NET Core answers with 400. On
a value-object property the serializer refuses the attribute outright (`InvalidOperationException`:
it is valid only on a number). Set the number handling on the options, or on a source-generated
context's `[JsonSourceGenerationOptions(NumberHandling = ...)]`: both reach the value object.

## Refusals

Every generated way in applies `Create`, and a refusal names the value object and the rule, or the
wrapped type it could not read, never the text, which can be a secret:

```csharp
JsonSerializer.Deserialize<Email>("\"nobody\"");       // JsonException naming Email and NoAt, never the text
JsonSerializer.Deserialize<Quantity>("\"many\"");      // JsonException naming Quantity and Int32, with no inner exception that quotes it
JsonSerializer.Deserialize<Quantity>("null");          // JsonException naming Quantity and null, refused for a struct
```

The `JsonException` still carries its path, so a host knows which property. ASP.NET Core answers it
with 400; what the body of that response says is in [ASP.NET Core](aspnet-core.md#what-a-refused-value-answers).

## Null and missing

A JSON `null` is refused for a struct value object; a `sealed partial record` reads it as `null`, as
any class does, since the serializer calls no converter for it. A property the document lacks is not
refused either: the serializer calls no converter for an absent property and leaves it `default`,
which for a value object is an instance that passed no factory.

```csharp
public sealed record Parcel(Quantity Boxes, Email? Contact);
```

```csharp
var parcel = JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""")!;
int boxes = parcel.Boxes.Value;                                        // 0, a missing property calls no converter and stays default

var strict = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""", strict);    // JsonException naming Parcel and Boxes
```

So a shape says which value objects it needs: `required` on a property, or
`RespectRequiredConstructorParameters` on the options for a record's constructor parameters, as
above. `RespectNullableAnnotations` makes a `null` refused for a class value object whose property
is not declared nullable. CMTK0006 points at a property of a value object's type that nothing sets.

## Stored documents

The generated converter applies today's `Create`, which is right for input and wrong for a document
the application stored before a rule existed. `StoredJsonConverterFactory`, registered on that
store's options and nowhere near input, reads a value object without its rules, as EF Core reads a
column; `Revalidate()` then says which stored values today's rules refuse.

```csharp
public enum CodeFault { NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value) =>
        value == value.ToUpperInvariant() ? new ProductCode(value) : Result.Error(CodeFault.NotUpperCase);   // the rule added after "abc" was stored
}
```

```csharp
var store = new JsonSerializerOptions { Converters = { new StoredJsonConverterFactory() } };

ProductCode stored = JsonSerializer.Deserialize<ProductCode>("\"abc\"", store);                                   // read without the rules
string state = stored.Revalidate().Match(_ => "still valid", fault => $"now refused: {fault}");                  // "now refused: NotUpperCase"
JsonSerializer.Deserialize<ProductCode>("\"abc\"");                                                               // JsonException naming ProductCode and NotUpperCase, the same document as input
```

## Decimals in one spelling

A decimal carries its scale: `7.8m` and `7.80m` are equal, and the serializer writes `7.8` and `7.80`,
so one value reaches a document in as many spellings as it arrived with, and a digest, a diff or a
replay sees differences that are not there. `DecimalNumberJsonConverter` writes the smallest scale as a
number, and `DecimalStringJsonConverter` as a string, for a client whose number is a double, which
rounds what a decimal holds past 15 digits. Both read a number or a string holding one, and keep the
scale they read. Registered once, either reaches every decimal, a nullable one and a dictionary key, and
every value object over a decimal, whose JSON is the wrapped type's:

```csharp
public readonly partial record struct Price : IValue<decimal>;
```

```csharp
var wire = new JsonSerializerOptions { Converters = { new DecimalStringJsonConverter() } };
string price = JsonSerializer.Serialize(Price.From(7.80m), wire);                                // "7.8"
string keyed = JsonSerializer.Serialize(new Dictionary<decimal, int> { [7.80m] = 1 }, wire);     // {"7.8":1}
Price read = JsonSerializer.Deserialize<Price>("7.80", wire);                                    // 7.80, a number read too, its scale kept
```

A store whose documents already hold numbers keeps them numbers with `DecimalNumberJsonConverter`, and
a document written as strings reads there too. `JsonNumberHandling.WriteAsString` is no alternative: it
writes every number as a string, an integer's included, and keeps every decimal's scale. A scale per
type, four places for a price, would widen `7.8` to `7.8000` where this narrows `7.8000` to `7.8`.

## Describing what a converter writes

A converter of one's own hides its type from a schema: System.Text.Json's exporter writes it as `true`,
ASP.NET Core as `{}` or a bare `format`, and both drop the `items` of a list of it and the
`additionalProperties` of a dictionary. System.Text.Json has no way for a converter to say what it writes
(dotnet/runtime#105769), so CodoMetis.TypeKit has one. A converter that implements
`IDescribedJsonConverter` says it, as both decimal converters do; a converter someone else wrote, or the
serializer's own enum converter, is described in the options by `JsonSchemaDescriptions`, which converts
nothing and carries the descriptions wherever the options go:

```csharp
public readonly record struct Coordinates(double Latitude, double Longitude);

public sealed class CoordinatesConverter : JsonConverter<Coordinates>, IDescribedJsonConverter
{
    public override Coordinates Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString()!.Split(',') is [var latitude, var longitude]
            ? new(double.Parse(latitude, CultureInfo.InvariantCulture), double.Parse(longitude, CultureInfo.InvariantCulture))
            : throw new JsonException();

    public override void Write(Utf8JsonWriter writer, Coordinates value, JsonSerializerOptions options) =>
        writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"{value.Latitude},{value.Longitude}"));

    public JsonSchemaDescription? DescribeJson(Type typeToConvert, JsonSerializerOptions options) =>
        JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["pattern"] = @"^-?\d+(\.\d+)?,-?\d+(\.\d+)?$" });
}

public sealed record Stop(Coordinates At, List<Coordinates> Route, DayOfWeek Day, Price Fare);
```

```csharp
var options = new JsonSerializerOptions(JsonSerializerOptions.Default)
{
    Converters =
    {
        new CoordinatesConverter(),
        new DecimalStringJsonConverter(),
        new JsonStringEnumConverter(),
        new JsonSchemaDescriptions().Describe(type => type.IsEnum ? JsonSchemaDescription.Of(new JsonObject { ["type"] = "string" }) : null),
    },
};
JsonNode stop = options.GetJsonSchemaAsNode(typeof(Stop), DescribedJsonSchema.ExporterOptions());
string route = stop["properties"]!["Route"]!["items"]!["type"]!.ToString();   // string, where the exporter alone wrote no items
string day = stop["properties"]!["Day"]!["type"]!.ToString();                 // string
string fare = stop["properties"]!["Fare"]!["format"]!.ToString();             // decimal, a value object as its wrapped type
```

What describes a type, the first that answers: the options' `JsonSchemaDescriptions`, the application's
word, so a value object can say more than its wrapped type (`Describe<Percentage>(…)` with a `minimum`
and a `maximum`); then the converter that writes it, as the serializer picks one: the property's own, a
nullable property's included, then the first of the options' converters that converts the type, a
`Nullable<T>` as itself before `T`, then the type's own, a value object's as its wrapped type under the
same options and a closed set's as a string of its members' names. A factory is asked for the converter
it makes, and a converter that says nothing says nothing. So a converter that replaces a value object's
generated one describes it, or leaves it undescribed.
A description says what one non-null value looks like, and the null goes in where a plain type would get
one in the same position: beside the type, or, where the schema combines subschemas (`oneOf`, `not`, an
`if` and its branches), in a branch beside the whole. It is a JSON Schema complete in itself: a `$ref`, a `$defs` or an `$id` is
refused where it is made, wherever a schema is, and a property or a value may still be called `$id`.
`JsonSchemaDescription.SameAs(type)` describes a converter that writes one type as another; the
references of a recursive type's schema are made to point from wherever it is put.

In CodoMetis.TypeKit.AspNetCore, `AddTypeKit()` writes the same descriptions into the OpenAPI document,
in every position, a list's elements and a dictionary's values included. A route, query or header
parameter binds through `TryParse` and not through JSON, so it keeps the description ASP.NET gives the
type without converters of one's own ([ASP.NET Core](aspnet-core.md)). The document is built from the
minimal-API JSON options and controllers write with MVC's, so a host with controllers registers the same
converters and descriptions on both, in the same order; building the document fails otherwise, naming the
type and what each set of options says of it. A schema the document would write otherwise than described,
a keyword OpenAPI 3.1 does not model such as `prefixItems`, fails it too, as does a described converter one
property names for a type the document writes once as a component, such as a value object. A description
in the options also outranks the converter's own: it can refine one, and it can contradict one. A type
nothing describes is left as the host writes it.

## Option and Result at the wire

Neither serializes. System.Text.Json refuses both, in both directions, with a `NotSupportedException`
that names the type and the alternative; without the refusal a `Some` would be written as `{}` and
read back as `None`, with nothing raised. A shape says absent with `T?`, and the option lives between
the shapes ([Option](option.md#at-the-boundary)); a result is matched to a response or a document
([ASP.NET Core](aspnet-core.md#a-result-as-a-response)).

```csharp
public sealed record Profile(Option<string> Nickname);
```

```csharp
JsonSerializer.Serialize(new Profile(Option.Some("Annie")));   // NotSupportedException naming Option and nullable
```

A converter registered on the options takes precedence over the refusal, so an application that
wants `Option<T>` on the wire writes one, closed over each `T` it needs, and registers it on those
options only:

```csharp
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
```

```csharp
var wire = new JsonSerializerOptions { Converters = { new OptionAsNullableConverter<string>() } };

string written = JsonSerializer.Serialize(new Profile(Option.Some("Annie")), wire);   // {"Nickname":"Annie"}
string absent = JsonSerializer.Serialize(new Profile(Option.None()), wire);          // {"Nickname":null}
Profile back = JsonSerializer.Deserialize<Profile>("""{"Nickname":null}""", wire)!;   // None
```

A converter per closed type rather than a factory: a factory would build the converter for each `T`
by `MakeGenericType`, which Native AOT cannot always honour. A property absent from the document
still reaches no converter and stays `default`, a `None`, so `required` is what says the shape must
mention it. A source-generated context cannot describe a shape with an `Option<T>?` property at all
and fails when it builds the contract, so declare `T?` there.

## Source-generated contexts

With a `JsonSerializerContext`, which Native AOT requires, list the value objects or the types that
hold them, not what they wrap: the generated converter builds the wrapped type's contract itself,
from a converter on the options, the type's own `[JsonConverter]` or the serializer's built-in one.
An enum-backed value object is the one exception, since a context's `UseStringEnumConverter` does not
reach an enum the context does not list:

```csharp
[JsonSerializable(typeof(Shipment))]
internal partial class ShipmentJson : JsonSerializerContext;

[JsonSerializable(typeof(Shipment))]
[JsonSerializable(typeof(DayOfWeek))]   // listed, so the string enum converter reaches what Weekday wraps
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal partial class NamedShipmentJson : JsonSerializerContext;
```

```csharp
string numbered = JsonSerializer.Serialize(shipment, ShipmentJson.Default.Shipment);        // {"Boxes":3,"Day":5,"Plan":{"Friday":3}}
string named = JsonSerializer.Serialize(shipment, NamedShipmentJson.Default.Shipment);      // {"Boxes":3,"Day":"Friday","Plan":{"Friday":3}}
```

What a context must list for the OpenAPI document is a different question, answered in
[Native AOT](native-aot.md#json-contexts).
