# JSON

A value object reads and writes the same JSON as the value inside it. Replace a `Guid` property with
an `OrderId` and no document changes, byte for byte. The only difference is that the value object's
rules now run on every value that comes in.

This page walks through the situations you'll meet: options, rejected input, null and missing
properties, stored documents, decimals, schemas, `Option` and `Result`, and source-generated
contexts. The [Generators README](../src/CodoMetis.TypeKit.Generators/README.md#json) and the
[base README](../src/CodoMetis.TypeKit/README.md#not-wire-types) are the reference.

The examples on this page use these imports and types:

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

The value object's JSON follows whatever options your application uses, both as a value and as a
dictionary key. The generated converter reads and writes through the serializer's own contract for
the underlying type. So every option you set for that type applies to the value object too:

- **A converter** registered for the underlying type.
- **Number handling**, like the web defaults' quoted numbers above.
- **An enum converter**, like `JsonStringEnumConverter` above.
- **A dictionary key policy.**

A test in this repository compares the two for every family of underlying types, under several sets
of options.

**Good to know**

- **A `DateTime` is written as UTC.** This is the converter's one addition to the underlying type's
  JSON. A `Local` value is converted to UTC. An `Unspecified` one is taken as UTC with its digits
  kept. So a stored instant reads back as the instant it was.
- **`[JsonNumberHandling]` on a record or class doesn't reach a value object.** The attribute applies
  to the number properties the serializer writes itself, and a value object's converter can't see it.
  Under
  `[JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]`,
  `record Counted(int Count)` writes `{"Count":"5"}` and reads `"5"`. But `record Counted(Quantity Count)`
  writes `{"Count":5}` and rejects `"5"` with a `JsonException`, which ASP.NET Core answers with 400.
  Put the attribute on a value-object property itself and the serializer throws
  `InvalidOperationException`, because the attribute is valid only on a number. Set the number handling
  on the options instead, or in a source-generated context's
  `[JsonSourceGenerationOptions(NumberHandling = ...)]`. Both reach the value object.

## When a value is rejected

Every generated way of reading a value object runs `Create`. When the input fails, the exception
names the value object and the rule, or the underlying type it couldn't read. It never quotes the
text, because the text could be a secret:

```csharp
JsonSerializer.Deserialize<Email>("\"nobody\"");       // JsonException naming Email and NoAt, never the text
JsonSerializer.Deserialize<Quantity>("\"many\"");      // JsonException naming Quantity and Int32, with no inner exception that quotes it
JsonSerializer.Deserialize<Quantity>("null");          // JsonException naming Quantity and null, refused for a struct
```

The `JsonException` still carries its path, so your host knows which property failed. ASP.NET Core
answers it with 400. [ASP.NET Core](aspnet-core.md#what-a-refused-value-answers) shows what the body
of that response says.

## Null and missing

```csharp
public sealed record Parcel(Quantity Boxes, Email? Contact);
```

```csharp
var parcel = JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""")!;
int boxes = parcel.Boxes.Value;                                        // 0, a missing property calls no converter and stays default

var strict = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
JsonSerializer.Deserialize<Parcel>("""{"Contact":null}""", strict);    // JsonException naming Parcel and Boxes
```

A struct value object rejects a JSON `null`. A `sealed partial record` reads it as `null`, like any
class, because the serializer calls no converter for a null class.

A missing property isn't rejected either. The serializer calls no converter for a property the
document lacks, so the property stays `default`. For a value object, that's an instance that never
went through a factory, like `Boxes` above.

So let your shape say which value objects it needs:

- **`required` on a property.**
- **`RespectRequiredConstructorParameters` on the options**, for a record's constructor parameters,
  as above.

**Good to know**

- **`RespectNullableAnnotations`** makes a class value object reject `null` when its property isn't
  declared nullable.
- **A property nothing sets gets a warning (CMTK0006)** when its type is a value object.

## Stored documents

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

The generated converter runs today's `Create`. That's right for input. It's wrong for a document your
application stored before a rule existed.

For that store, register `StoredJsonConverterFactory` on its options, and nowhere near input. It reads
a value object without validation, the way EF Core reads a column. Then call `Revalidate()` to find
the stored values that today's rules reject.

## Decimals in one spelling

```csharp
public readonly partial record struct Price : IValue<decimal>;
```

```csharp
var wire = new JsonSerializerOptions { Converters = { new DecimalStringJsonConverter() } };
string price = JsonSerializer.Serialize(Price.From(7.80m), wire);                                // "7.8"
string keyed = JsonSerializer.Serialize(new Dictionary<decimal, int> { [7.80m] = 1 }, wire);     // {"7.8":1}
Price read = JsonSerializer.Deserialize<Price>("7.80", wire);                                    // 7.80, a number read too, its scale kept
```

A decimal carries its scale. `7.8m` and `7.80m` are equal, yet the serializer writes `7.8` and `7.80`.
So one value ends up in your documents in as many spellings as it arrived with, and a digest, a diff
or a replay sees differences that aren't there.

Two converters write the smallest scale instead:

- **`DecimalNumberJsonConverter`** writes it as a number.
- **`DecimalStringJsonConverter`** writes it as a string. Use it for a client whose numbers are
  doubles, which round what a decimal holds past 15 digits.

Both read a number, or a string holding one, and keep the scale they read. Register either one once,
and it covers every decimal: nullable ones, dictionary keys, and every value object over a decimal,
since its JSON is the underlying type's.

**Good to know**

- **Documents that already hold numbers** stay numbers with `DecimalNumberJsonConverter`. A document
  written as strings reads there too.
- **`JsonNumberHandling.WriteAsString` is no alternative.** It writes every number as a string,
  integers included, and keeps every decimal's scale.
- **A scale per type wouldn't help either.** Four places for a price would widen `7.8` to `7.8000`,
  where these converters narrow `7.8000` to `7.8`.

## Describing what a converter writes

When you write your own converter, schemas lose track of the type. System.Text.Json's exporter writes
it as `true`, and ASP.NET Core as `{}` or a bare `format`. Both drop the `items` of a list of it and
the `additionalProperties` of a dictionary of it. System.Text.Json gives a converter no way to say
what it writes (dotnet/runtime#105769), so CodoMetis.TypeKit adds one:

- **Your own converter** implements `IDescribedJsonConverter` and describes itself, as both decimal
  converters do.
- **A converter someone else wrote**, or the serializer's own enum converter, is described in the
  options by `JsonSchemaDescriptions`. It converts nothing, and it carries the descriptions wherever
  the options go.

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

### Which description is used

The first of these that answers describes the type:

1. **The options' `JsonSchemaDescriptions`.** This is your application's word, so a value object can
   say more than its underlying type, for example `Describe<Percentage>(...)` with a `minimum` and a
   `maximum`.
2. **The converter that writes it, chosen the way the serializer chooses one.** First the property's
   own converter, a nullable property's included. Then the one the options' resolver puts in charge,
   which weighs the options' converters against the type's own, and a `Nullable<T>`'s before `T`'s.
   So your own resolver wins over a conflicting converter in the options, as it does on the wire.
3. **For a type the resolver doesn't know**, the first of the options' converters that converts it.

A value object's generated converter is described as its underlying type, under the same options. A
closed set's converter is described as a string of its members' names. A converter factory is asked
for the converter it makes. A converter that says nothing leaves the type undescribed. So a converter
that replaces a value object's generated one either describes it, or leaves it undescribed.

**Good to know**

- **A `Nullable<T>` is described as `T`.** System.Text.Json's own converter for a `Nullable<T>` is
  described by `T`'s contract, because no public API names the converter it was built over. So when
  a resolver builds a nullable contract by hand, with `JsonMetadataServices.GetNullableConverter` over
  another converter of `T`, it's described as `T`'s. Under `ReferenceHandler.Preserve`, a value object
  over it is judged by `T`'s. Give the nullable type a converter of its own instead.
- **Your own transform runs once on each node.** Pass your exporter options to
  `DescribedJsonSchema.ExporterOptions(yours)`, and your `TransformSchemaNode` runs after the
  description. Where a type is described as another's schema, such as a value object as its
  underlying type's, the nodes inside that schema get it too, but their `context.Path` starts at
  that schema, not at the document's root.
- **A description covers one non-null value.** The null is added where a plain type would get one in
  the same position: beside the type, or, where the schema combines subschemas (`oneOf`, `not`, an
  `if` and its branches), in a branch beside the whole.
- **A description is a complete JSON Schema.** It can't contain a `$ref`, a `$defs` or an `$id`, at
  any depth. Creating one that does throws an `ArgumentException`. A property or a value may still be
  called `$id`.
- **`JsonSchemaDescription.SameAs(type)`** describes a converter that writes one type as another.
- **Recursive types work.** The references in a recursive type's schema are made to point correctly
  from wherever the schema is put.

### In the OpenAPI document

With CodoMetis.TypeKit.AspNetCore, `AddTypeKit()` writes the same descriptions into the OpenAPI
document, in every position, a list's elements and a dictionary's values included. A route, query
or header parameter binds through `TryParse`, not JSON. So it keeps the description ASP.NET gives the
type without your converters ([ASP.NET Core](aspnet-core.md)).

Building the document fails, and names the type, in these cases:

- **Controllers and minimal APIs are set up differently.** The document is built from the minimal-API
  JSON options, while controllers write with MVC's. A host with controllers registers the same
  converters and descriptions on both, in the same order. Otherwise the error names the type and what
  each set of options says about it.
- **The document would write the schema differently from its description**, such as a keyword
  OpenAPI 3.1 doesn't model, like `prefixItems`.
- **One property names a described converter for a type the document writes once**, as a component,
  such as a value object.

A description in the options also outranks the converter's own. You can use it to refine one, and
you can also contradict one. A type nothing describes is left as the host writes it.

## Option and Result at the wire

```csharp
public sealed record Profile(Option<string> Nickname);
```

```csharp
JsonSerializer.Serialize(new Profile(Option.Some("Annie")));   // NotSupportedException naming Option and nullable
```

Neither type serializes. System.Text.Json rejects both, reading and writing, with a
`NotSupportedException` that names the type and what to use instead. Without that, a `Some` would be
written as `{}` and read back as `None`, and nothing would tell you.

In a shape, say "absent" with `T?`, and keep the option between shapes
([Option](option.md#at-the-boundary)). Match a result to a response or a document
([ASP.NET Core](aspnet-core.md#a-result-as-a-response)).

If you do want `Option<T>` on the wire, write a converter. A converter registered on the options
takes precedence over the rejection. Close it over each `T` you need, and register it only on those
options:

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

**Good to know**

- **One converter per closed type, not a factory.** A factory would build the converter for each `T`
  with `MakeGenericType`, which Native AOT can't always support.
- **A missing property is still `None`.** A property absent from the document reaches no converter and
  stays `default`, which is `None`. Use `required` when the shape must mention it.
- **Source-generated contexts can't hold `Option<T>?`.** A context can't describe a shape with an
  `Option<T>?` property, and fails when it builds the contract. Declare `T?` there.

## Source-generated contexts

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

Native AOT needs a `JsonSerializerContext`. In it, list the value objects, or the types that hold
them, and not the types they wrap. The generated converter builds the underlying type's contract
itself, from a converter on the options, the type's own `[JsonConverter]`, or the serializer's
built-in one.

The one exception is a value object over an enum. A context's `UseStringEnumConverter` doesn't reach
an enum the context doesn't list, so list the enum as well, as `NamedShipmentJson` does.

What a context must list for the OpenAPI document is a different question.
[Native AOT](native-aot.md#json-contexts) answers it.
