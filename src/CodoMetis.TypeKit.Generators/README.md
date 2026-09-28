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

How this compares with Vogen, Thinktecture.Runtime.Extensions and StronglyTypedId, and when one of
them is the better choice: [How it compares](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit#how-it-compares).

## Built with Metalama

This package exists in this form because of [Metalama](https://www.postsharp.net/metalama). A
transitive fabric finds every type that implements a TypeKit contract, in the project that references
this package and in every project that references that one, so a value object is declared by its
interface and nothing else. Templates write the generated members as ordinary C#, which you can read
(see "Reading the generated code"). A declaration that cannot be generated fails the build with its
name on it (see "Build errors") instead of compiling to less than it appears to. Thanks to the
Metalama team.

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

int three = quantity.Value;                      // 3
bool same = quantity == Quantity.From(3);        // true, value equality
string text = quantity.ToString();               // "3"
```

## What can be wrapped

Any non-generic class, struct or enum, with the exceptions CMTK1005 lists. The generated members
follow what the wrapped type offers: parsing where it can be parsed, formatting where it formats, and
comparison where it is comparable. Numbers, `bool`, `char`, `string`, `Guid`, `DateTime`,
`DateTimeOffset`, `DateOnly`, `TimeOnly` and enums have all of it; a `Uri` has no ordering, and neither
do NodaTime's `OffsetDateTime`, `ZonedDateTime`, `Period` and `Interval`, whose `Interval` has no type
converter to parse through either. An enum parses as `Enum.TryParse` does, case-sensitively: a name, a
comma-separated list of names, or a number, which need not name a member.

A generic wrapped type, a tuple included, is refused (CMTK1005): a `List<T>` or an `ImmutableArray<T>`
compares by reference, so the value object would not have value equality. Wrap a type of your own that
does.

## What is generated

| Member | For | Notes |
|---|---|---|
| `Value`, a private constructor, value equality | every value object | The record's equality, over the wrapped value. Declare no constructor of your own, not even a parameter list (CMTK1009). |
| `From(T)` | `IValue<T>` | Refuses null for a reference type. |
| `TryFrom(T)`, `FromKnownGood(T)` | `IValidatedValue` | Both derived from your `Create`. `FromKnownGood` throws with the caller's expression in the message, never the value. Declare either yourself and it is not generated (see "Your own members" below). |
| `Revalidate()` | `IValidatedValue` | `Create` applied to the value an instance holds: which values read back without validation (a column, a stored document) would today's rules refuse? Returns what `Create` returns, normalisation included. |
| `[JsonConverter]` with a nested converter | every value object | Reads and writes the wrapped value exactly as the serializer does, also as a dictionary key. A JSON `null` is refused for a struct value object (see JSON below). |
| `IParsable`, `ISpanParsable`, `IUtf8SpanParsable` | when the wrapped type can be parsed | A `Uri` parses relative or absolute, as JSON reads it; a NodaTime type parses through its type converter. |
| `IFormattable`, `ISpanFormattable`, `IUtf8SpanFormattable`, `ToString()` | every value object, unless you write `ToString()` | Invariant. A hand-written `ToString()` is kept, and then none of the formatting interfaces is generated, so interpolation and `string.Format` reach yours too. |
| `IEqualityOperators<TSelf, TSelf, bool>` | every value object | The generic-math form of `==`/`!=`, so a value object satisfies that constraint. |
| `IComparable<TSelf>`, `IComparable`, `IComparisonOperators<TSelf, TSelf, bool>`, `<` `>` `<=` `>=` | when the wrapped type is comparable | Strings compare ordinally, so ordering agrees with equality. |
| `MinValue`, `MaxValue`, `IMinMaxValue<TSelf>` | `IValue<T>` over a type whose own static `MinValue`/`MaxValue` are of that type | Never for a validated value object. |
| `[TypeConverter]` with a nested converter | when parsing is generated | MVC model binding and the reflection-based configuration binder use it. The configuration binding source generator, which `PublishAot` and `PublishTrimmed` turn on, does not bind value objects and leaves them `default`: bind the wrapped type there. |
| `IConvertible` | when the wrapped type is convertible | Explicit, so `Convert.ToInt64(quantity)` and `Convert.ChangeType(quantity, typeof(Quantity))` work. |
| `{TSelf}Extensions.GetValue()` and `ValueOrNull()` | every value object an extension class can reach (not a `private` or `protected` nested one) | For EF Core queries, where they translate to the column. |
| `IValueObjectMaterializer<TSelf, T>` | every value object | Explicit, invisible on the type. Rebuilds an instance without validation, for the EF Core satellite. |

Every generated way into a validated value object goes through `Create`. The only exception is the
explicit materializer, for values the application wrote itself. Inside the type, your own members can
reach the private constructor, which applies no rules, because `Create` needs it: call it from
`Create` only. Another way in is a static method that calls `From` or `Create`.

`default(Email)` is not one of those ways, and the analyzer refuses it (CMTK0001), but an array slot or
a JSON document without the property can still produce it (CMTK0005, and "A missing property" below).
Its `Value` is the wrapped type's `default`: `Guid.Empty` for an `OrderId`, and `null` for an `Email`
despite its `string` type.

### Your own members

A value object may declare its own `TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)` and
`ToString()`; each is kept, and what depends on it is derived from it. Any other member or attribute
the generators introduce, written by hand, is CMTK1011, which names it: a plain value object's `From`,
`Value`, `Parse` and `TryParse`, `MinValue`/`MaxValue`, `[JsonConverter]` or `[TypeConverter]`, the
interfaces the generators implement, and `IConvertible` where they generate it. So is an explicit
implementation of a member of an interface they implement, such as `IParsable<TSelf>.Parse` or
`IFormattable.ToString`: every caller through the interface would reach it rather than the generated
member, and a generic `T.Parse` would skip `Create`. `Create` is declared once, `public static`, and
never as an explicit interface implementation: alone, the generated code cannot call it, and beside a
public one, generic code calling `T.Create` would reach its rules instead of the ones every generated
way in applies.

To change what a value object prints, declare `ToString()`: a record's `PrintMembers(StringBuilder)` is
CMTK1011 without it, since the generated `ToString()` prints the wrapped value and never calls it.

What a base record declares counts as the value object's own. Its `sealed` `ToString()` is the seam,
since C# keeps it in every derived record; one that is not sealed is CMTK1011, because the generated
`ToString()` would replace it: declare `ToString()` in the value object, or seal the base's. Its
`PrintMembers`, its explicit implementations of the interfaces the generators implement, and an
`IConvertible` on it are refused as they are on the value object.

Equality is the wrapped value's, as the ordering, the JSON and the column are, so a hand-written
`Equals(TSelf)`, `GetHashCode()` or `IEquatable<TSelf>.Equals` is CMTK1011: it would make values equal
that sort apart. To make values that differ only in form equal, such as "abc" and "ABC", normalise them
in `Create` (a plain value object becomes an `IValidatedValue` for that), so that equal values hold
the same wrapped value.

A value object holds its wrapped value and nothing else. Its JSON, parsing, type converter and column
carry that value alone, while a record's equality compares every field, so any other instance state
would be lost on a round trip or make equal values unequal. An instance field, an auto-property, a
`required` member or a field-like event, declared or inherited from a base record, is CMTK1012. A
property computed from `Value` without a backing field, and anything `static` or `const`, is fine.

## JSON

The wire format is the wrapped value's, so an `OrderId` is a string and a `Quantity` a number:

```csharp
JsonSerializer.Serialize(new Order(OrderId.New(), Quantity.From(3)));
// {"Id":"0199a3f4-1c00-7000-8000-000000000001","Quantity":3}

JsonSerializer.Deserialize<Email>("\"not an address\"");   // JsonException naming Email and NoAt, not the text
JsonSerializer.Deserialize<Dictionary<OrderId, int>>(json); // value objects work as dictionary keys
```

The bytes are the serializer's own for the wrapped type under the same options, as a value and as a
dictionary key: a converter your options register for the wrapped type, number handling, an enum
converter and `DictionaryKeyPolicy` all apply as they do to the wrapped type. Replacing a `Guid`
property by an `OrderId` therefore changes no JSON, and a test compares the two for every wrapped-type
family under several sets of options. One addition: a `DateTime` is written as UTC, a `Local` one
converted and an `Unspecified` one taken as UTC with its digits kept.

Numbers and dates are read as strictly as the serializer reads the wrapped type. What it cannot read
is a `JsonException` naming the value object and the wrapped type ("Quantity could not read the JSON
value as Int32."), with no inner exception that could carry the text; ASP.NET Core answers it with 400.
NodaTime types use NodaTime's converters when `NodaTime.Serialization.SystemTextJson` is referenced and
the options have none of their own. For JSON the application stored itself, see
`StoredJsonConverterFactory` in the base package.

**A JSON `null`** is refused for a struct value object. A `sealed partial record` reads it as `null`,
as any class does, since the serializer calls no converter for it; `RespectNullableAnnotations = true`
on the options makes it refuse `null` for a property that is not declared nullable.

**A missing property** is not refused either: the serializer calls no converter for a property the
document lacks, and leaves it `default`. Mark the property `required`, or set
`RespectRequiredConstructorParameters = true` for a record's constructor parameters.

With a source-generated `JsonSerializerContext` (and so under Native AOT), list the value objects,
not the types they wrap. The context sees a value object's converter and never what it wraps, so the
converter builds that contract itself, as the serializer would: a converter on your options first,
then the wrapped type's `[JsonConverter]`, then the serializer's built-in converter. An enum is the
exception: a context's `UseStringEnumConverter` does not reach an enum the context does not list, so
an enum-backed value object is written as a number unless you list the enum
(`[JsonSerializable(typeof(DayOfWeek))]`) or name its converter
(`[JsonSourceGenerationOptions(Converters = [typeof(JsonStringEnumConverter<DayOfWeek>)])]`).
Everything generated is trim- and AOT-safe, and the consumer smoke test publishes it with Native AOT.

## Parsing and formatting

```csharp
Quantity.Parse("3", null);                     // Quantity 3
Quantity.TryParse("x", null, out _);           // false
Email.Parse("nobody", null);                   // FormatException naming Email and NoAt

var de     = CultureInfo.GetCultureInfo("de-DE");
var text   = Amount.From(1.5m).ToString();                 // "1.5", whatever the current culture
var german = Amount.From(1.5m).ToString("N2", de);         // "1,50"
var shown  = $"{Amount.From(1.5m)}";                       // "1.5"
```

A null `IFormatProvider` means the invariant culture in every generated `Parse`, `TryParse`,
`ToString` and `TryFormat`, so `Parse(x.ToString(), null)` round-trips wherever the process runs. A
provider that is given is used as given where the wrapped type's parsing and formatting take one; a
`Uri`, a NodaTime type and a type parsed through a constructor or a provider-less `Parse` ignore it.

Text the wrapped type cannot read is a `FormatException` naming the value object and the wrapped
type ("Quantity could not read the input as Int32."), an overflow included, with no inner exception:
the wrapped type's own message quotes the input. A refusal by `Create` names the fault instead.

## Comparison

`CompareTo` and the operators follow the wrapped type's ordering, ordinal for a string. To order
differently, declare `CompareTo(TSelf)` yourself: it is kept, and the object overload, the operators
and the interfaces are derived from it. Any other hand-written comparison member, an explicit
implementation of `IComparable<TSelf>`, `IComparable` or `IComparisonOperators` included, is an error
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
| CMTK1005 | Generic or nested in a generic type (nesting in a non-generic type is fine), `file`-local or nested in a `file`-local type, named `Value`, derived from another value object, or wrapping an array, a pointer, a nullable type, a generic type (a tuple included) or a value object (itself included). Wrap what the other value object wraps instead. |
| CMTK1006 | A record class not declared `sealed`. |
| CMTK1007 | The `{TSelf}Extensions` companion's name is taken by a declared type or by another value object's companion. |
| CMTK1008 | A hand-written comparison operator, object `CompareTo` or explicit implementation of a comparison interface beside the generated ones. |
| CMTK1009 | A hand-written instance constructor, including a positional record's parameter list such as `OrderId(Guid Value)`. The constructor and `Value` are generated; a static constructor is fine. |
| CMTK1010 | Declared in more than one `partial` part: Metalama 2026.1 writes the generated `ToString()` into every part, which does not compile. A part a source generator adds, such as a `[GeneratedRegex]` method's, does not count. |
| CMTK1011 | A hand-written member or attribute the generators introduce (see "Your own members"), an explicit implementation of an interface they implement, a hand-written equality, a `PrintMembers` without `ToString()`, or `Create` implemented explicitly. |
| CMTK1012 | Instance state besides the wrapped value: a field, an auto-property, a `required` member or a field-like event, declared or inherited. Compute it from `Value`, make it static, or wrap a type that holds all of it. |

The analyzers that come with the base package add CMTK0001 to CMTK0009; see
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers).

## Reading the generated code

Set `<MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>` in the project
and read `obj/Debug/net10.0/metalama/`: every generated member is there as ordinary C#.
