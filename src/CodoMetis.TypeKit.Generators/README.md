# CodoMetis.TypeKit.Generators

Compile-time generation of value objects and closed sets (smart enums) for
[CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit), built on
[Metalama](https://www.postsharp.net/metalama). One line becomes a complete value object, with
its JSON converter, parsing, formatting, comparison, type converter and query companions, each
applying the one `Create` you wrote.

```bash
dotnet add package CodoMetis.TypeKit.Generators
```

Reference it in the project that declares value objects. Projects that reference that one, directly
or further down, inherit the generators without referencing the package themselves.
`Metalama.Framework` flows with it, because the fabric that applies the aspects runs in every one of
those projects. No Metalama license key is needed, there or here: the generators build on Metalama's
Open Source edition.

How this compares with Vogen, Thinktecture.Runtime.Extensions and StronglyTypedId, and when one of
them is the better choice: [How it compares](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/comparison.md).

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
do NodaTime's `OffsetDateTime`, `ZonedDateTime`, `Period` and `Interval`, and `Interval` also has no
type converter to parse through. An enum parses as `Enum.TryParse` does, case-sensitively: a name, a
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

Every generated way into a validated value object goes through `Create`. Two paths skip it, both for
values the application wrote itself: the explicit materializer, which the EF Core satellite reads a
column through, and `StoredJsonConverterFactory`, registered only on the options that read the
application's own JSON. Inside the type, your own members can reach the private constructor, which
applies no rules, because `Create` needs it: call it from `Create` only. For another way in, write a
static method that calls `From` or `Create`.

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
never as an explicit interface implementation. The generated code cannot call an explicit one; and
beside a public one, generic code calling `T.Create` would reach the explicit one's rules, not those
every generated way in applies.

To change what a value object prints, declare `ToString()`: a record's `PrintMembers(StringBuilder)` is
CMTK1011 without it, since the generated `ToString()` prints the wrapped value and never calls it.

What a base record declares counts as the value object's own. Its `sealed` `ToString()` is the seam,
since C# keeps it in every derived record; one that is not sealed is CMTK1011, because the generated
`ToString()` would replace it: declare `ToString()` in the value object, or seal the base's. Its
`PrintMembers`, its explicit implementations of the interfaces the generators implement, and an
`IConvertible` on it are refused as they are on the value object.

Equality is the wrapped value's, as the ordering, the JSON and the column are, so a hand-written
`Equals(TSelf)`, `GetHashCode()`, `IEquatable<TSelf>.Equals`, or `operator ==` or `!=` between two of
the type, is CMTK1011: it would make values equal that sort apart. To make values that differ only in form equal, such as "abc" and "ABC", normalise them
in `Create` (a plain value object becomes an `IValidatedValue` for that), so that equal values hold
the same wrapped value.

An `[Obsolete]` on a value object, or on a type it is nested in, is copied to its `{TSelf}Extensions`
class, which names the value object outside it: with `error: true` that use failed the build.

A value object holds its wrapped value and nothing else. Its JSON, parsing, type converter and column
carry that value alone, while a record's equality compares every field, so any other instance state
would be lost on a round trip or make equal values unequal. An instance field, an auto-property, a
`required` member or a field-like event, declared or inherited from a base record, is CMTK1012. A
property computed from `Value` without a backing field, and anything `static` or `const`, is fine.

### A value that must not be printed

A value object over a secret declares `ToString()`. No formatting interface is generated then, so
interpolation, `string.Format`, `Convert.ToString`, a record that holds it and every
Microsoft.Extensions.Logging path print what it says, `[LogProperties]` included:

```csharp
public sealed partial record ApiKey : IValue<string>
{
    public override string ToString() => "ApiKey(***)";
}
```

- **Still carrying the value**, since they are conversions and not display: its JSON, the type
  converter and the database column; and the debugger, which shows `ToString()` but expands `Value`.
- **Serilog** reads the public `Value` where it destructures (`{@Key}`, or a record holding one), past
  `ToString()`. [CodoMetis.TypeKit.Serilog](https://www.nuget.org/packages/CodoMetis.TypeKit.Serilog)
  logs every value object as what it prints instead.
- **Microsoft.Extensions.Compliance**: classify the logging method's parameter. A
  `DataClassificationAttribute` on the value object's type makes the logging generator report
  LOGGEN035 and generate nothing for the method.

## Closed sets

An `enum` names its members and nothing else: a `switch` over one is never proven complete (C#
only warns about a missing discard), a member carries no data, and the names are integers on the
wire unless a converter says otherwise. A closed set is a sealed class whose every instance is a
named member, declared as a `public static readonly` field, with constant data through a private
constructor where the members carry some:

```csharp
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}

public sealed partial class Tier : IClosedSet<Tier>
{
    public static readonly Tier Basic = new(1, null);
    public static readonly Tier Premium = new(2, "Priority support.");

    private Tier(int rank, string? perk) { Rank = rank; Perk = perk; }

    public int Rank { get; }
    public string? Perk { get; }
}
```

The generators add `Name` from the field's name, `QualifiedName` (`OrderStatus.Open`), `All` in
declaration order and read-only, `Match` with one required delegate per member named after it, `TryParse` and
`Parse` by name, each ordinal or ignoring case when asked, `ToString()`, a JSON converter that
writes the name and reads the member itself back, and a private parameterless constructor where the
type declares none:

```csharp
var label = status.Match(open: () => "Open", shipped: () => "On its way", cancelled: () => "Cancelled");   // every member, or it does not compile
var names = OrderStatus.All.Select(s => s.Name);                  // Open Shipped Cancelled
var same = OrderStatus.Parse("Shipped") == OrderStatus.Shipped;   // True, members are singletons
var json = JsonSerializer.Serialize(Tier.Premium);                // "Premium"
```

Members are singletons, so equality is reference equality, and a member added later fails every
`Match` until it is handled. Every parameter of `Match` has the same type, so an argument by
position would follow the declaration order and swap branches when two members are reordered: the
analyzer requires every argument named (CMTK0012), and its code fix names them. `Match` checks every
delegate for null before it picks one. `Parse` and the JSON converter refuse an unknown name with an
exception naming the set, never the input, and a set is a dictionary key in JSON by its name too.
`Parse(name, ignoreCase: true)` and `TryParse(name, ignoreCase: true, out member)` accept any casing.
JSON, the type converter, `IParsable` and a bound route or query parameter stay ordinal, since what
is stored or sent is the name as it is: a header or a query string a client typed in any casing is
bound as a `string` and parsed with `ignoreCase: true`.
`IParsable<TSelf>` is implemented explicitly, for generic code that parses through `T.Parse`, and a
type converter reads and writes the name, so the configuration binder, MVC and anything else that
reads a string through `TypeDescriptor` bind a set. The binder's source generator reads no type
converter ([dotnet/runtime#83599](https://github.com/dotnet/runtime/issues/83599)): under it, as under
Native AOT, bind the name as a `string` and `Parse` it.
The EF Core satellite maps a closed set to a text column of the member's name, and the ASP.NET Core
satellite describes it as a string enum of the names, with nothing registered per set. A set whose
members are faults declares `IFaultSet<TSelf>`, a `FaultKind` and an optional description per member,
and the ASP.NET Core satellite answers it by kind.

Each member is initialized with `new` where it is declared (CMTK1019), and the constructors are
private (CMTK1016); a record is refused (CMTK1020). A member may not take the name of a generated
member, nor differ from another's only in case (`Open` and `open`), since
`TryParse(name, ignoreCase: true)` could not tell them apart (CMTK1021). A `ToString()` written by
hand is kept, and the name is still what JSON, parsing and the column use. An overload of a generated
method with another signature, such as `Parse(int rank)`, is kept too. Any other generated member
written by hand is CMTK1011. An instance the set's own code creates beside its members has no name,
so reading its name throws rather than writing a null. A JSON `null` reads as `null`, as for any class, unless the serializer's
`RespectNullableAnnotations` refuses it.

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

One exception: number handling given by an attribute rather than by the options. A
`[JsonNumberHandling]` on the containing type reaches the number properties the serializer writes
itself, and a value object's converter never sees it: under
`[JsonNumberHandling(WriteAsString | AllowReadingFromString)]`, `record Dto(int Count)` writes
`{"Count":"5"}` and reads `"5"`, while `record Dto(Quantity Count)` writes `{"Count":5}` and refuses
`"5"` with a `JsonException`. On the property itself the serializer refuses the attribute for any type
but a number (`InvalidOperationException`). Set the number handling on the options, or on a
source-generated context's `[JsonSourceGenerationOptions]`, which do reach the value object.

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
`ToString` and `TryFormat`, so the text does not depend on the culture the process runs in:
`ToString()` is the wrapped type's own default text under the invariant culture, and the type
converter and `$"{x}"` write the same. It reads back as the same value wherever the wrapped type's
own does: numbers (`float`, `double` and `Half` included), `BigInteger`, `Guid`, `DateOnly`,
`TimeSpan`, `Version`, `Uri`, enums and strings. It does not for `TimeOnly`, `DateTime` and
`DateTimeOffset`, whose default text drops what the invariant culture's short patterns drop:
`TimeOnly(3, 4, 5, 123)` prints `03:04`, a `DateTime` loses its fraction of a second and its `Kind`,
a `DateTimeOffset` its fraction. Format those with `"O"` (`x.ToString("O", null)`), which a `TimeOnly`
and a `DateTimeOffset` read back exactly, or keep them in their JSON form, which a `DateTime` reads
back as the UTC instant it wrote. A provider that is given is used as given where the wrapped type's
parsing and formatting take one; a `Uri`, a NodaTime type and a type parsed through a constructor or a
provider-less `Parse` ignore it.

Text the wrapped type cannot read is a `FormatException` naming the value object and the wrapped
type ("Quantity could not read the input as Int32."), an overflow included, with no inner exception:
the wrapped type's own message quotes the input. A refusal by `Create` names the fault instead. The
one place the input still appears is System.Text.Json's own: a refused dictionary key ends the
`JsonException`'s `Path`.
`TryParse` answers `false` for both, and lets anything else through as `Parse` does: an exception
`Create` throws is a bug to see, not text to refuse.

A default instance, which CMTK0001 keeps out of your own code but EF Core, MVC and serializers can
make, formats as `""` over a reference type, as it always did over a string.

## Comparison

`CompareTo` and the operators follow the wrapped type's ordering, ordinal for a string. To order
differently, declare `CompareTo(TSelf)` yourself: it is kept, and the object overload, the operators
and the interfaces are derived from it. Any other hand-written comparison member, an explicit
implementation of `IComparable<TSelf>`, `IComparable` or `IComparisonOperators` included, is an error
(CMTK1008), so in memory every comparison agrees with your `CompareTo`.

A database cannot run it: it orders the column by the wrapped value. The type then carries
`[HandWrittenOrdering]`, and an EF Core query that compares, orders, or takes the `Min` or `Max` of
it is refused, naming the value object, rather than answered in the column's order
([CodoMetis.TypeKit.EntityFrameworkCore](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries)).
Compare or order by `.Value` where the wrapped value's order is meant.

## Build errors

A declaration that cannot be generated is an error, so no type is left half-generated:

| Id | Meaning |
|---|---|
| CMTK1000 | Not declared `partial`. |
| CMTK1001 | Not a record. |
| CMTK1002 | A struct not declared `readonly`. |
| CMTK1003 | More than one marker, such as `IValue<int>, IValue<string>`. |
| CMTK1004 | An `IValidatedValue<TSelf, …>` whose `TSelf` is another type. |
| CMTK1005 | Generic or nested in a generic type (nesting in a non-generic type is fine), `file`-local or nested in a `file`-local type, named `Value` or as another member the generators introduce (`From`, `TryFrom`, `FromKnownGood`, `Revalidate`, `Parse`, `TryParse`, `TryFormat`, `MinValue`, `MaxValue`, `CompareTo`), derived from another value object, or wrapping `object`, `System.Enum`, an array, a pointer, a nullable type, a generic type (a tuple included) or a value object (itself included). Wrap what the other value object wraps instead. |
| CMTK1006 | A record class not declared `sealed`. |
| CMTK1007 | The `{TSelf}Extensions` companion's name is taken by a declared type or by another value object's companion. |
| CMTK1008 | A hand-written comparison operator, object `CompareTo` or explicit implementation of a comparison interface beside the generated ones. |
| CMTK1009 | A hand-written instance constructor, including a positional record's parameter list such as `OrderId(Guid Value)`. The constructor and `Value` are generated; a static constructor is fine. |
| CMTK1010 | Declared in more than one `partial` part: Metalama 2026.1 writes the generated `ToString()` into every part, which does not compile. A part a source generator adds, such as a `[GeneratedRegex]` method's, does not count. |
| CMTK1011 | A hand-written member or attribute the generators introduce (see "Your own members", and for a closed set every generated member but `ToString()`, by signature, `IParsable<TSelf>`, `[JsonConverter]` and `[TypeConverter]`), an explicit implementation of an interface they implement, a hand-written equality (an `operator ==` or `!=` between two of the type included, which C# also reports as CS0111), a `PrintMembers` without `ToString()`, or `Create` implemented explicitly. |
| CMTK1012 | Instance state besides the wrapped value: a field, an auto-property, a `required` member or a field-like event, declared or inherited. Compute it from `Value`, make it static, or wrap a type that holds all of it. |
| CMTK1013 | A closed set not declared `partial`. |
| CMTK1014 | A closed set not declared `sealed`: a set with subtypes is not closed. |
| CMTK1015 | A closed set with no member: a member is a `public static readonly` field of the set's own type. |
| CMTK1016 | A closed set with a constructor that is not private, so an instance beside its members could be made. |
| CMTK1017 | A closed set with a static member of its own type that is not a public readonly field: a property, a field that is not `readonly`, or a field that is not public, which is an instance that is no member. |
| CMTK1018 | A closed set that is generic or nested in a generic type. |
| CMTK1019 | A closed set's member not initialized with `new` where it is declared, and with nothing else: `= Open` would give one instance two names, and a member set later, by a call, or by an expression that only starts with `new`, may be another member or null. |
| CMTK1020 | A closed set declared as a record: `with` copies a member into an instance that is none, and a record compares by value where members are singletons. |
| CMTK1021 | A closed set's member named as a member the generators introduce (`Name`, `QualifiedName`, `All`, `Match`, `Parse`, `TryParse`, `ToString`, the JSON or type converter), a set itself named as one of them (a member cannot have the name of its type), or a member whose name differs from another member's only in case: they collide in `TryParse(name, ignoreCase: true)`, and in `Match`'s parameters where the first letter is the difference; or two members whose `Match` parameters would have one name, which lower-casing the first letter gives a member starting with the Kelvin sign (U+212A) and one starting with `K`. |
| CMTK1022 | An abstract type with a value-object marker: it would get no member, and a value held as it, of a type derived from it, would have no `Value` and be written as `{}`. Declare the marker on the sealed type. An abstract closed set is CMTK1014. |

The analyzers that come with the base package add CMTK0001 to CMTK0012; see
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers).

## Reading the generated code

Set `<MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>` in the project
and read `obj/Debug/net10.0/metalama/`: every generated member is there as ordinary C#.
