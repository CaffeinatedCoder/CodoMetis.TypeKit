# CodoMetis.TypeKit.Generators

Compile-time generation of strongly-typed value objects for
[CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit), built on
[Metalama](https://www.postsharp.net/metalama). One line becomes a complete value object, with
its JSON converter, parsing, formatting, comparison, type converter and query companions.

```bash
dotnet add package CodoMetis.TypeKit.Generators
```

Reference it in the project that declares value objects. Projects that reference that one, directly
or further down, inherit the generators without referencing the package themselves.
`Metalama.Framework` flows with it, because the fabric that applies the aspects runs in every one of
those projects. No Metalama license key is needed, there or here: the generators build on Metalama's
Open Source edition.

## Declaring a value object

```csharp
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Quantity : IValue<int>;

public enum EmailFault { Blank, NoAt, TooLong }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);

        var trimmed = value.Trim();
        if (!trimmed.Contains('@')) return Result.Error(EmailFault.NoAt);
        if (trimmed.Length > 254) return Result.Error(EmailFault.TooLong);

        return new Email(trimmed);   // the private constructor is generated
    }
}
```

A value object is a `readonly partial record struct`, or a `sealed partial record` class when it
has to be a reference type. `IValue<T>` wraps any `T`; `IValidatedValue<TSelf, T, TFault>` adds
`Create`, the one method you write, and every generated way in applies it.

A validated value object has no `From`. Its ways in are `Create`, `TryFrom` and `FromKnownGood`, so
nothing that looks harmless throws on input: the one factory that throws says in its name that the
caller vouches for the value, and its exception names the call site, never the value. The fault
`Create` returns is what every refusal reports, in that exception, in the JSON converter's
`JsonException` and in `Parse`'s `FormatException`.

A value object may be declared inside another type. `Order.Id` is generated like any other, and its
companion class is `OrderIdExtensions`, named after the whole nesting chain so that `Order.Id` and
`Customer.Id` do not collide.

```csharp
var id = OrderId.New();                          // a version 7 Guid
var quantity = Quantity.From(3);

Result<Email, EmailFault> created = Email.Create(input);       // says what to fix
Option<Email> maybe = Email.TryFrom(input);                    // is it valid?
Email known = Email.FromKnownGood("ops@example.com");          // yours to guarantee; throws otherwise

quantity.Value;                                  // 3
quantity == Quantity.From(3);                    // true, value equality
quantity.ToString();                             // "3"
```

## What can be wrapped

Any class, struct or enum, with the exceptions CMTK1005 lists. The generated members follow what the
wrapped type offers. Numbers, `bool`, `char`, `string`, `Guid`, `decimal`, `DateTime`,
`DateTimeOffset`, `DateOnly`, `TimeOnly`, enums, `Uri` and the NodaTime types get JSON, parsing,
formatting and comparison in full; an enum parses by name and is written as the host's JSON options
write the enum itself. Anything else is written and read through the serializer's own converter for
it, and parsed when it implements `IParsable`.

## What is generated

| Member | For | Notes |
|---|---|---|
| `Value`, a private constructor, value equality | every value object | The record's equality, over the wrapped value. Declare no constructor of your own, not even a parameter list (CMTK1009). |
| `From(T)` | `IValue<T>` | Refuses null for a reference type. |
| `TryFrom(T)`, `FromKnownGood(T)` | `IValidatedValue` | Both derived from your `Create`. `FromKnownGood` throws with the caller's expression in the message, never the value. Declare either yourself and it is not generated. |
| `[JsonConverter]` with a nested converter | every value object | Reads and writes the wrapped value, also as a dictionary key. A JSON `null` is refused. |
| `IParsable`, `ISpanParsable`, `IUtf8SpanParsable` | when the wrapped type has them | Enums parse by name. A `Uri` parses through its constructor, a NodaTime type through its type converter. |
| `IFormattable`, `ISpanFormattable`, `IUtf8SpanFormattable`, `ToString()` | when the wrapped type has them | `ToString()` is invariant. |
| `IEqualityOperators<TSelf, TSelf, bool>` | every value object | The generic-math form of `==`/`!=`, so a value object satisfies that constraint. |
| `IComparable<TSelf>`, `IComparable`, `IComparisonOperators<TSelf, TSelf, bool>`, `<` `>` `<=` `>=` | when the wrapped type is comparable | Strings compare ordinally, so ordering agrees with equality. |
| `MinValue`, `MaxValue`, `IMinMaxValue<TSelf>` | `IValue<T>` over a type that has them | Never for a validated value object. |
| `[TypeConverter]` with a nested converter | when parsing is generated | Model binding and configuration binding work. |
| `IConvertible` | when the wrapped type is convertible | Explicit, so `Convert.ToInt64(quantity)` works. |
| `{TSelf}Extensions.GetValue()` and `ValueOrNull()` | every value object | For EF Core queries, where they translate to the column. |
| `IValueObjectMaterializer<TSelf, T>` | every value object | Explicit, invisible on the type. Rebuilds an instance without validation, for the EF Core satellite. |

Every generated way into a validated value object goes through `Create`. The only exception is the
explicit materializer, for values the application wrote itself. Inside the type, your own members can
reach the private constructor, which applies no rules, because `Create` needs it: call it from
`Create` only. Another way in is a static method that calls `From` or `Create`.

## JSON

The wire format is the wrapped value's, so an `OrderId` is a string and a `Quantity` a number:

```csharp
JsonSerializer.Serialize(new Order(OrderId.New(), Quantity.From(3)));
// {"Id":"0199a3f4-1c00-7000-8000-000000000001","Quantity":3}

JsonSerializer.Deserialize<Email>("\"not an address\"");   // JsonException naming Email and NoAt, not the text
JsonSerializer.Deserialize<Dictionary<OrderId, int>>(json); // value objects work as dictionary keys
```

A `DateTime` is always written as UTC. Numbers and dates are read as strictly as the serializer
reads the wrapped type itself, and malformed text is a `JsonException`, which ASP.NET Core answers
with 400. NodaTime types use NodaTime's converters when `NodaTime.Serialization.SystemTextJson` is
referenced. For JSON the application stored itself, see `StoredJsonConverterFactory` in the base package.

With a source-generated `JsonSerializerContext` (and so under Native AOT), list the value objects,
not the types they wrap. The context sees a value object's converter and never what it wraps, so the
converter builds that contract itself, as the serializer would: a converter on your options first,
then the wrapped type's `[JsonConverter]`, then the serializer's built-in converter. Your options'
number handling applies either way. Everything generated is trim- and AOT-safe, and the consumer
smoke test publishes it with Native AOT.

## Parsing and formatting

```csharp
Quantity.Parse("3", null);                     // Quantity 3
Quantity.TryParse("x", null, out _);           // false
Email.Parse("nobody", null);                   // FormatException naming Email and NoAt

Amount.From(1.5m).ToString();                  // "1.5", whatever the current culture
Amount.From(1.5m).ToString("N2", german);      // "1,50"
$"{Amount.From(1.5m)}";                        // "1.5"
```

A null `IFormatProvider` means the invariant culture in every generated `Parse`, `TryParse`,
`ToString` and `TryFormat`, so `Parse(x.ToString(), null)` round-trips wherever the process runs.
A provider that is given is used as given.

## Comparison

`CompareTo` and the operators follow the wrapped type's ordering, ordinal for a string. To order
differently, declare `CompareTo(TSelf)` yourself: it is kept, and the object overload, the operators
and the interfaces are derived from it. Any other hand-written comparison member is an error
(CMTK1008), so the ordering can never disagree with itself.

## Build errors

A declaration that cannot be generated is an error, so no type is left half-generated:

| Id | Meaning |
|---|---|
| CMTK1000 | Not declared `partial`. |
| CMTK1001 | Not a record. |
| CMTK1002 | A struct not declared `readonly`. |
| CMTK1003 | More than one marker, such as `IValue<int>, IValue<string>`. |
| CMTK1004 | An `IValidatedValue<TSelf, …>` whose `TSelf` is another type. |
| CMTK1005 | Generic or nested in a generic type (nesting in a non-generic type is fine), derived from another value object, or wrapping an array, a pointer, a nullable type or a value object (itself included). Wrap what the other value object wraps instead. |
| CMTK1006 | A record class not declared `sealed`. |
| CMTK1007 | The `{TSelf}Extensions` companion's name is taken by a declared type or by another value object's companion. |
| CMTK1008 | A hand-written comparison operator or object `CompareTo` beside the generated ones. |
| CMTK1009 | A hand-written instance constructor, including a positional record's parameter list such as `OrderId(Guid Value)`. The constructor and `Value` are generated; a static constructor is fine. |

The analyzer that comes with the base package adds CMTK0001, no `default` of a value object, and
CMTK0002, a value object in a project without this package.

## Reading the generated code

Set `<MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>` in the project
and read `obj/Debug/net10.0/metalama/`: every generated member is there as ordinary C#, marked
`[CompilerGenerated]`.
