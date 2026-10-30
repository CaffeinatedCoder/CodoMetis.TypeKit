# CodoMetis.TypeKit.Generators

Declare a value object in one line, and get a complete type at compile time. This package generates
value objects and closed sets (smart enums) for
[CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit). It is built on
[Metalama](https://postsharp.net/metalama), a framework that generates C# code at compile time from
templates written in C#. Each value object comes with its JSON converter, parsing,
formatting, comparison, type converter and query helpers, and every one of them runs the one
`Create` you wrote.

```bash
dotnet add package CodoMetis.TypeKit.Generators
```

Reference it in the project that declares your value objects. Projects that reference that one,
directly or further down, get the generators too, without referencing the package themselves.
`Metalama.Framework` comes along with it, because the fabric that applies the generators runs in
every one of those projects. You need no Metalama license key, there or here: the generators build on
Metalama's Open Source edition.

This page is the reference for the package. For a guide to designing value objects, read
[Value objects](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/value-objects.md).
To see how TypeKit compares with Vogen, Thinktecture.Runtime.Extensions and StronglyTypedId, and when
one of them is the better choice, read
[How it compares](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/comparison.md).

## Built with Metalama

This package exists in this form because of [Metalama](https://postsharp.net/metalama). A
transitive fabric finds every type that implements a TypeKit interface, in the project that
references this package and in every project that references that one. So you declare a value object
by its interface and nothing else.

Templates write the generated members as ordinary C#, which you can read (see "Reading the generated
code"). A declaration that can't be generated fails the build with its name on it (see "Build
errors"), instead of compiling to less than it appears to. Thanks to the Metalama team.

## Declare a value object

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

A value object is a `readonly partial record struct`, or a `sealed partial record` class when it has
to be a reference type. There are two interfaces:

- **`IValue<T>`** wraps any `T`.
- **`IValidatedValue<TSelf, T, TFault>`** adds `Create`, the one method you write. Every way a value
  gets created runs it.

You don't have to type `Create` out. Declare the interface first. The build then fails because
`Create` is missing (CS0535), and your editor offers a fix for that error: **Add Create**. It writes
a `Create` that accepts every value, ready for your rules:

```csharp
public enum NameFault { Blank, TooLong }

public readonly partial record struct Name : IValidatedValue<Name, string, NameFault>
{
    public static Result<Name, NameFault> Create(string value)
    {
        // Add your rules: if (...) return Result.Error(NameFault.Blank);
        return new Name(value);
    }
}
```

A validated value object has no `From`. You create one with `Create`, `TryFrom` or `FromKnownGood`,
so nothing harmless-looking throws on input. The one factory that throws says in its name that you
vouch for the value, and its exception names the call site, never the value.

The error `Create` returns (TypeKit calls it a fault) is what every rejection reports: in that
exception, in the JSON converter's `JsonException`, and in `Parse`'s `FormatException`.

<details>
<summary>What the generators write for <code>Email</code>, trimmed</summary>

Every factory below calls the `Create` you wrote:

<!-- not compiled: generated code for Generators/DeclaringSamples.cs, trimmed (GeneratedCodeBlockTests) -->
```csharp
public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>, IValueObject<Email, string>
{
    private readonly string _value;

    private Email(string value)
    {
        _value = value;
    }

    public string Value
    {
        get { return _value; }
    }

    public static Option<Email> TryFrom(string value)
    {
        return Result.ToOption(Email.Create(value));
    }

    public static Email FromKnownGood(string value, [CallerArgumentExpression("value")] string? source = null)
    {
        return GeneratedFactories.OrInvalidOperationException(Email.Create(value), source);
    }

    public Result<Email, EmailFault> Revalidate()
    {
        return Email.Create(Value);
    }

    // Also: Parse and TryParse, formatting, comparison, a JSON converter and a type converter.
}
```

</details>

You can declare a value object inside another type. `Order.Id` is generated like any other. Its
companion class is `OrderIdExtensions`, named after the whole nesting chain, so `Order.Id` and
`Customer.Id` don't collide.

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

## What you can wrap

Any non-generic class, struct or enum, with the exceptions listed under CMTK1005. The generated
members follow what the underlying type can do:

- **Parsing** where it can be parsed.
- **Formatting** where it formats.
- **Comparison** where it's comparable.

Numbers, `bool`, `char`, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and
enums have all of it. A `Uri` has no ordering. Neither do NodaTime's `OffsetDateTime`,
`ZonedDateTime`, `Period` and `Interval`, and `Interval` also has no type converter to parse through.

An enum parses as `Enum.TryParse` does, case-sensitively: a name, a comma-separated list of names, or
a number, which doesn't have to name a member.

A generic underlying type, a tuple included, fails the build (CMTK1005). A `List<T>` or an
`ImmutableArray<T>` compares by reference, so the value object wouldn't have value equality. Wrap a
type of your own that does.

## What is generated

| Member | For | Notes |
|---|---|---|
| `Value`, a private constructor, value equality | every value object | The record's equality, over the underlying value. Declare no constructor of your own, not even a parameter list (CMTK1009). |
| `From(T)` | `IValue<T>` | Rejects null for a reference type. |
| `TryFrom(T)`, `FromKnownGood(T)` | `IValidatedValue` | Both built from your `Create`. `FromKnownGood` throws with the caller's expression in the message, never the value. Declare either one yourself and it isn't generated (see "Write your own members" below). |
| `Revalidate()` | `IValidatedValue` | Runs `Create` on the value an instance holds. It answers: which values read back without validation (a column, a stored document) would today's rules reject? Returns what `Create` returns, normalisation included. |
| `[JsonConverter]` with a nested converter | every value object | Reads and writes the underlying value exactly as the serializer does, also as a dictionary key. A struct value object rejects a JSON `null` (see JSON below). |
| `IParsable` | when the underlying type can be parsed | A `Uri` parses relative or absolute, as JSON reads it. A NodaTime type parses through its type converter. |
| `ISpanParsable`, `IUtf8SpanParsable` | where the underlying type has them | Each one reads and rejects the same text as `Parse`. A `string` has `ISpanParsable` only. A `Uri`, an enum and a NodaTime type have neither. |
| `IFormattable`, `ISpanFormattable`, `IUtf8SpanFormattable`, `ToString()` | every value object, unless you write `ToString()` | Invariant. A `ToString()` you write is kept, and then none of the formatting interfaces is generated, so interpolation and `string.Format` use yours too. |
| `IEqualityOperators<TSelf, TSelf, bool>` | every value object | The generic-math form of `==`/`!=`, so a value object satisfies that constraint. |
| `IComparable<TSelf>`, `IComparable`, `IComparisonOperators<TSelf, TSelf, bool>`, `<` `>` `<=` `>=` | when the underlying type is comparable | Strings compare ordinally, so ordering agrees with equality. |
| `MinValue`, `MaxValue`, `IMinMaxValue<TSelf>` | `IValue<T>` over a type whose own static `MinValue`/`MaxValue` are of that type | Never for a validated value object. |
| `[TypeConverter]` with a nested converter | when parsing is generated | MVC model binding and the reflection-based configuration binder use it. The configuration binding source generator, which `PublishAot` and `PublishTrimmed` turn on, doesn't bind value objects and leaves them `default`. Bind the underlying type there. |
| `IConvertible` | when the underlying type is convertible | Explicit, so `Convert.ToInt64(quantity)` and `Convert.ChangeType(quantity, typeof(Quantity))` work. |
| `{TSelf}Extensions.GetValue()` and `ValueOrNull()` | every value object an extension class can reach (not a `private` or `protected` nested one) | For EF Core queries, where they translate to the column. |
| `IValueObjectMaterializer<TSelf, T>` | every value object | Explicit, so it's invisible on the type. Rebuilds an instance without validation, for the EF Core package. |

Every generated way of creating a validated value object goes through `Create`. Two paths skip it,
both for values your application wrote itself:

- **The explicit materializer**, which the EF Core package reads a column through.
- **`StoredJsonConverterFactory`**, registered only on the options that read your application's own
  JSON.

Inside the type, your own members can reach the private constructor, which applies no rules, because
`Create` needs it. Call it from `Create` only. For another way of creating a value, write a static
method that calls `From` or `Create`.

**Good to know**

- **`default(Email)` isn't a way to create one.** The analyzer rejects it (CMTK0001). But an array
  slot, or a JSON document without the property, can still produce it (CMTK0005, and "A missing
  property" below). Its `Value` is the underlying type's `default`: `Guid.Empty` for an `OrderId`,
  and `null` for an `Email` despite its `string` type.

### Write your own members

A value object may declare its own `TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)` and
`ToString()`. Each one is kept, and whatever depends on it is built from it.

Writing any other generated member or attribute by hand fails the build, naming it (CMTK1011). That
covers:

- **A plain value object's members**: `From`, `Value`, `Parse` and `TryParse`, `MinValue`/`MaxValue`.
- **The attributes**: `[JsonConverter]` and `[TypeConverter]`.
- **The interfaces the generators implement**, and `IConvertible` where they generate it.
- **An explicit implementation of a member of an interface they implement**, such as
  `IParsable<TSelf>.Parse` or `IFormattable.ToString`. Every caller through the interface would reach
  it rather than the generated member, and a generic `T.Parse` would skip `Create`.

Declare `Create` once, `public static`, and never as an explicit interface implementation. The
generated code can't call an explicit one. And beside a public one, generic code calling `T.Create`
would reach the explicit one's rules, not the ones every generated way of creating a value runs.

To change what a value object prints, declare `ToString()`. A record's
`PrintMembers(StringBuilder)` without it fails the build (CMTK1011), since the generated `ToString()`
prints the underlying value and never calls it.

#### Base records

What a base record declares counts as the value object's own:

- **A `sealed` `ToString()`** on the base is kept, since C# keeps it in every derived record.
- **A `ToString()` that isn't sealed** fails the build (CMTK1011), because the generated `ToString()`
  would replace it. Declare `ToString()` in the value object, or seal the base's.
- **Its `PrintMembers`, its explicit implementations of the interfaces the generators implement, and
  an `IConvertible` on it** are rejected as they are on the value object.

#### Equality

Equality is the underlying value's, like the ordering, the JSON and the column. So a hand-written
`Equals(TSelf)`, `GetHashCode()`, `IEquatable<TSelf>.Equals`, or `operator ==` or `!=` between two of
the type, fails the build (CMTK1011). It would make values equal that sort apart. So does an `Equals`
you write for one of its base records' types, such as `Equals(DeepBase?)`: a `HashSet<DeepBase>` would
compare by it while `==` compares the underlying value.

To make values equal that differ only in form, such as "abc" and "ABC", normalise them in `Create`.
Equal values then hold the same underlying value. A plain value object becomes an `IValidatedValue`
for that.

Over a base record, the generated `Equals(TSelf)` and `GetHashCode()` compare the underlying value
alone, and never call the base's. So a hand-written `Equals(TBase)`, `GetHashCode()` or
`IEquatable<TBase>.Equals` on a base record isn't used, and a warning names it (CMTK1023). The value
object implements each base's `IEquatable<TBase>` again (`IReimplementedEquatable<TBase>`), so a
collection of the base compares it the way the value object does.

#### Obsolete value objects

An `[Obsolete]` on a value object, or on a type it's nested in, is copied to its
`{TSelf}Extensions` class, which names the value object outside it. Without the copy, with
`error: true`, that use failed the build.

#### One value, nothing else

A value object holds its underlying value and nothing else. Its JSON, parsing, type converter and
column carry that value alone, while a record's equality compares every field. So any other instance
state would be lost on a round trip, or make equal values unequal.

An instance field, an auto-property, a `required` member or a field-like event fails the build
(CMTK1012), whether declared or inherited from a base record. A property computed from `Value`
without a backing field is fine, and so is anything `static` or `const`.

A base record from another assembly is seen without its private fields, and a writable property of
it counts as state. What a private field of it holds takes no part in the value object's equality,
and is lost on a round trip like anything else beside the value.

### A value that must not be printed

```csharp
public sealed partial record ApiKey : IValue<string>
{
    public override string ToString() => "ApiKey(***)";
}
```

For a value object over a secret, declare `ToString()`. Then no formatting interface is generated, so
these all print what your `ToString()` says: interpolation, `string.Format`, `Convert.ToString`, a
record that holds it, and every Microsoft.Extensions.Logging path, `[LogProperties]` included.

**Good to know**

- **Some things still carry the value**, because they're conversions and not display: its JSON, the
  type converter and the database column. The debugger too, which shows `ToString()` but expands
  `Value`.
- **Serilog** reads the public `Value` where it destructures (`{@Key}`, or a record holding one),
  past `ToString()`.
  [CodoMetis.TypeKit.Serilog](https://www.nuget.org/packages/CodoMetis.TypeKit.Serilog) logs every
  value object as what it prints instead.
- **Microsoft.Extensions.Compliance**: classify the logging method's parameter. A
  `DataClassificationAttribute` on the value object's type makes the logging generator report
  LOGGEN035 and generate nothing for the method.

## Closed sets

An `enum` names its members and nothing else:

- **A `switch` over one is never proven complete.** C# only warns about a missing discard.
- **A member carries no data.**
- **The names are integers on the wire**, unless a converter says otherwise.

A closed set fixes all three. It's a sealed class whose every instance is a named member, declared as
a `public static readonly` field. Where the members carry data, it's constant data passed through a
private constructor:

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

```csharp
var label = status.Match(open: () => "Open", shipped: () => "On its way", cancelled: () => "Cancelled");   // every member, or it does not compile
var names = OrderStatus.All.Select(s => s.Name);                  // Open Shipped Cancelled
var same = OrderStatus.Parse("Shipped") == OrderStatus.Shipped;   // True, members are singletons
var json = JsonSerializer.Serialize(Tier.Premium);                // "Premium"
```

The generators add:

- **`Name`**, from the field's name, and **`QualifiedName`** (`OrderStatus.Open`).
- **`All`**, in declaration order and read-only.
- **`Match`**, with one required delegate per member, named after it.
- **`TryParse` and `Parse`** by name, each ordinal, or ignoring case when you ask.
- **`ToString()`.**
- **A JSON converter** that writes the name and reads the member itself back.
- **A private parameterless constructor**, where the type declares none.

Members are singletons, so equality is reference equality. A member added later fails every `Match`
until it's handled.

<details>
<summary>What the generators write for <code>OrderStatus</code>, trimmed</summary>

<!-- not compiled: generated code for Generators/ClosedSetSamples.cs, trimmed (GeneratedCodeBlockTests) -->
```csharp
public sealed partial class OrderStatus : IClosedSet<OrderStatus>, IParsable<OrderStatus>
{
    private static readonly IReadOnlyList<OrderStatus> _all = null!;

    private string _name = null!;

    static OrderStatus()
    {
        OrderStatus.Open._name = "Open";
        OrderStatus.Shipped._name = "Shipped";
        OrderStatus.Cancelled._name = "Cancelled";
        OrderStatus._all = Array.AsReadOnly(new OrderStatus[] { OrderStatus.Open, OrderStatus.Shipped, OrderStatus.Cancelled });
    }

    private OrderStatus() { }

    public string Name
    {
        get { return _name ?? throw GeneratedClosedSets.NotAMember("OrderStatus"); }
    }

    public TResult Match<TResult>(Func<TResult> open, Func<TResult> shipped, Func<TResult> cancelled)
    {
        ArgumentNullException.ThrowIfNull(open, "open");
        ArgumentNullException.ThrowIfNull(shipped, "shipped");
        ArgumentNullException.ThrowIfNull(cancelled, "cancelled");
        if (object.ReferenceEquals(this, OrderStatus.Open))
            return open.Invoke();
        if (object.ReferenceEquals(this, OrderStatus.Shipped))
            return shipped.Invoke();
        if (object.ReferenceEquals(this, OrderStatus.Cancelled))
            return cancelled.Invoke();
        throw GeneratedClosedSets.NotAMember("OrderStatus");
    }

    // Also: All, QualifiedName, Parse and TryParse, ToString and a JSON converter.
}
```

</details>

### Matching, parsing and binding

- **`Match` arguments must be named (CMTK0012).** Every parameter of `Match` has the same type, so an
  argument by position would follow the declaration order, and swap branches when two members are
  reordered. The analyzer's code fix names them. `Match` checks every delegate for null before it
  picks one.
- **Unknown names are rejected.** `Parse` and the JSON converter throw for an unknown name, with an
  exception naming the set, never the input. A set is a dictionary key in JSON by its name too.
- **Casing.** `Parse(name, ignoreCase: true)` and `TryParse(name, ignoreCase: true, out member)`
  accept any casing. JSON, the type converter, `IParsable` and a bound route or query parameter stay
  ordinal, since what's stored or sent is the name as it is. For a header or a query string a client
  typed in any casing, bind it as a `string` and parse it with `ignoreCase: true`.
- **`IParsable<TSelf>` is implemented explicitly**, for generic code that parses through `T.Parse`.
- **A type converter reads and writes the name.** So the configuration binder, MVC, and anything else
  that reads a string through `TypeDescriptor` can bind a set. The binder's source generator reads no
  type converter ([dotnet/runtime#83599](https://github.com/dotnet/runtime/issues/83599)). Under it,
  as under Native AOT, bind the name as a `string` and `Parse` it.
- **Nothing to register per set.** The EF Core package maps a closed set to a text column of the
  member's name. The ASP.NET Core package describes it as a string enum of the names.
- **Error sets.** A set whose members are errors declares `IFaultSet<TSelf>`, a `FaultKind` and an
  optional description per member. The ASP.NET Core package answers it by kind.

### Rules for a closed set

- **Each member is initialized with `new` where it's declared (CMTK1019)**, and never written again
  (CMTK0013, the analyzer's). A member assigned in the static constructor would hold an instance named
  and listed as another.
- **The constructors are private (CMTK1016).**
- **A record fails the build (CMTK1020).**
- **Member names.** A member can't take the name of a generated member, or differ from another only
  in case (`Open` and `open`), since `TryParse(name, ignoreCase: true)` couldn't tell them apart
  (CMTK1021).
- **A `ToString()` you write is kept**, and the name is still what JSON, parsing and the column use.
- **An overload of a generated method with another signature**, such as `Parse(int rank)`, is kept
  too. Any other generated member written by hand fails the build (CMTK1011).
- **An instance beside the members has no name.** If the set's own code creates one, reading its name
  throws rather than writing a null.
- **A JSON `null` reads as `null`**, as for any class, unless the serializer's
  `RespectNullableAnnotations` rejects it.

## JSON

```csharp
JsonSerializer.Serialize(new Order(OrderId.New(), Quantity.From(3)));
// {"Id":"0199a3f4-1c00-7000-8000-000000000001","Quantity":3}

JsonSerializer.Deserialize<Email>("\"not an address\"");   // JsonException naming Email and NoAt, not the text
JsonSerializer.Deserialize<Dictionary<OrderId, int>>(json); // value objects work as dictionary keys
```

The JSON is the underlying value's, so an `OrderId` is a string and a `Quantity` a number.

The bytes are the serializer's own for the underlying type under the same options, both as a value
and as a dictionary key. So everything you set for the underlying type applies: a converter your
options register for it, number handling, an enum converter, and `DictionaryKeyPolicy`. Replacing a
`Guid` property with an `OrderId` changes no JSON. A test compares the two for every family of
underlying types, under several sets of options.

Numbers and dates are read as strictly as the serializer reads the underlying type. Input it can't
read throws a `JsonException` naming the value object and the underlying type ("Quantity could not
read the JSON value as Int32."), with no inner exception that could carry the text. ASP.NET Core
answers it with 400.

NodaTime types use NodaTime's converters when `NodaTime.Serialization.SystemTextJson` is referenced
and the options have none of their own. For JSON your application stored itself, see
`StoredJsonConverterFactory` in the base package.

**Good to know**

- **A `DateTime` is written as UTC.** This is the converter's one addition. A `Local` value is
  converted, and an `Unspecified` one is taken as UTC with its digits kept.
- **`[JsonNumberHandling]` on the containing type doesn't reach a value object.** The attribute
  reaches the number properties the serializer writes itself, and a value object's converter never
  sees it. Under `[JsonNumberHandling(WriteAsString | AllowReadingFromString)]`,
  `record Dto(int Count)` writes `{"Count":"5"}` and reads `"5"`. But `record Dto(Quantity Count)`
  writes `{"Count":5}` and rejects `"5"` with a `JsonException`. On the property itself, the
  serializer rejects the attribute for any type but a number (`InvalidOperationException`). Set the
  number handling on the options, or on a source-generated context's
  `[JsonSourceGenerationOptions]`. Both reach the value object.

**A JSON `null`** is rejected for a struct value object. A `sealed partial record` reads it as `null`,
as any class does, since the serializer calls no converter for it. `RespectNullableAnnotations = true`
on the options makes it reject `null` for a property that isn't declared nullable.

**Preserved references** (`ReferenceHandler.Preserve`) throw a `NotSupportedException` where the
serializer could write `$id` or `$ref` inside the underlying value. That's a class it walks as an
object, an `object` the serializer writes itself, or a struct, nullable or not, holding one in a
property that isn't ignored. The serializer numbers references per call, and the converter reaches
the underlying value through a call of its own, so its numbers wouldn't match the document's.
Register a `JsonConverter` for the underlying type, which writes no `$id` for it or for the value
object, or serialize without preserving references. These are unaffected: a value the serializer writes with a value converter (a number, a
string, a `Guid`, a `Uri`), anything a converter of your own writes, on the options or on one
property, a struct of such values, and `ReferenceHandler.IgnoreCycles`.

**A missing property** isn't rejected either. The serializer calls no converter for a property the
document lacks, and leaves it `default`. Mark the property `required`, or set
`RespectRequiredConstructorParameters = true` for a record's constructor parameters.

**With a source-generated `JsonSerializerContext`** (and so under Native AOT), list the value
objects, not the types they wrap. The context sees a value object's converter and never what it
wraps. So the converter builds that contract itself, as the serializer would: a converter on your
options first, then the underlying type's `[JsonConverter]`, then the serializer's built-in
converter.

An enum is the exception. A context's `UseStringEnumConverter` doesn't reach an enum the context
doesn't list. So a value object over an enum is written as a number, unless you list the enum
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

Text is the same on every machine. A null `IFormatProvider` means the invariant culture in every
generated `Parse`, `TryParse`, `ToString` and `TryFormat`, so the text doesn't depend on the culture
the process runs in. `ToString()` is the underlying type's own default text under the invariant
culture, and the type converter and `$"{x}"` write the same.

That text reads back as the same value wherever the underlying type's own does: numbers (`float`,
`double` and `Half` included), `BigInteger`, `Guid`, `DateOnly`, `TimeSpan`, `Version`, `Uri`, enums
and strings.

**Good to know**

- **`TimeOnly`, `DateTime` and `DateTimeOffset` lose detail as text.** Their default text drops what
  the invariant culture's short patterns drop. `TimeOnly(3, 4, 5, 123)` prints `03:04`, a `DateTime`
  loses its fraction of a second and its `Kind`, and a `DateTimeOffset` its fraction. Format those
  with `"O"` (`x.ToString("O", null)`), which a `TimeOnly` and a `DateTimeOffset` read back exactly.
  Or keep them in their JSON form, which a `DateTime` reads back as the UTC instant it wrote.
- **A provider you pass is used as given** where the underlying type's parsing and formatting take
  one. A `Uri`, a NodaTime type, and a type parsed through a constructor or a provider-less `Parse`
  ignore it.
- **A default instance formats as `""` over a reference type**, as it always did over a string.
  CMTK0001 keeps such an instance out of your own code, but EF Core, MVC and serializers can make one.

### When text is rejected

Text the underlying type can't read throws a `FormatException` naming the value object and the
underlying type ("Quantity could not read the input as Int32."), an overflow included. It has no inner
exception, because the underlying type's own message quotes the input. A rejection by `Create` names
the error instead.

The one place the input still appears is System.Text.Json's own: a rejected dictionary key ends the
`JsonException`'s `Path`.

`TryParse` answers `false` for both. It lets anything else through, as `Parse` does: an exception
`Create` throws is a bug you should see, not text to reject.

## Comparison

`CompareTo` and the operators follow the underlying type's ordering, ordinal for a string.

To order differently, declare `CompareTo(TSelf)` yourself. It's kept, and the object overload, the
operators and the interfaces are built from it. Any other hand-written comparison member fails the
build (CMTK1008), an explicit implementation of `IComparable<TSelf>`, `IComparable` or
`IComparisonOperators` included. So in memory, every comparison agrees with your `CompareTo`.

A database can't run your `CompareTo`, because it orders the column by the underlying value. So the
type carries `[HandWrittenOrdering]`, and an EF Core query that compares or orders it, or takes its
`Min` or `Max`, is rejected, naming the value object, rather than answered in the column's order
([CodoMetis.TypeKit.EntityFrameworkCore](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#queries)).
Compare or order by `.Value` where the underlying value's order is what you mean.

## Build errors

A declaration that can't be generated fails the build, so no type is left half-generated. Every
error ends with a link to this table. CMTK1023 is the one warning:

| Id | Meaning |
|---|---|
| CMTK1000 | Not declared `partial`. |
| CMTK1001 | Not a record. |
| CMTK1002 | A struct not declared `readonly`. |
| CMTK1003 | More than one marker, such as `IValue<int>, IValue<string>`. |
| CMTK1004 | An `IValidatedValue<TSelf, ...>` whose `TSelf` is another type. |
| CMTK1005 | Generic or nested in a generic type (nesting in a non-generic type is fine), `file`-local or nested in a `file`-local type, named `Value` or as another member the generators add (`From`, `TryFrom`, `FromKnownGood`, `Revalidate`, `Parse`, `TryParse`, `TryFormat`, `MinValue`, `MaxValue`, `CompareTo`), derived from another value object, or wrapping `object`, `System.Enum`, an array, a pointer, a nullable type, a generic type (a tuple included) or a value object (itself included). Wrap what the other value object wraps instead. |
| CMTK1006 | A record class not declared `sealed`. |
| CMTK1007 | The `{TSelf}Extensions` companion's name is taken by a declared type or by another value object's companion. |
| CMTK1008 | A hand-written comparison operator, object `CompareTo` or explicit implementation of a comparison interface beside the generated ones. |
| CMTK1009 | A hand-written instance constructor, including a positional record's parameter list such as `OrderId(Guid Value)`. The constructor and `Value` are generated. A static constructor is fine. |
| CMTK1010 | Declared in more than one `partial` part. Metalama 2026.1 writes the generated `ToString()` into every part, which doesn't compile. A part a source generator adds, such as a `[GeneratedRegex]` method's, doesn't count. |
| CMTK1011 | A hand-written member or attribute the generators add (see "Write your own members", and for a closed set every generated member but `ToString()`, by signature, `IParsable<TSelf>`, `[JsonConverter]` and `[TypeConverter]`), an explicit implementation of an interface they implement, a hand-written equality (an `operator ==` or `!=` between two of the type included, which C# also reports as CS0111), a `PrintMembers` without `ToString()`, or `Create` implemented explicitly. |
| CMTK1012 | Instance state besides the underlying value: a field, an auto-property, a `required` member or a field-like event, declared or inherited. Compute it from `Value`, make it static, or wrap a type that holds all of it. |
| CMTK1013 | A closed set not declared `partial`. |
| CMTK1014 | A closed set not declared `sealed`. A set with subtypes isn't closed. |
| CMTK1015 | A closed set with no member. A member is a `public static readonly` field of the set's own type. |
| CMTK1016 | A closed set with a constructor that isn't private, so an instance beside its members could be made. |
| CMTK1017 | A closed set with a static member of its own type that isn't a public readonly field: a property, a field that isn't `readonly`, or a field that isn't public. Each of those is an instance that is no member. |
| CMTK1018 | A closed set that is generic or nested in a generic type. |
| CMTK1019 | A closed set's member not initialized with `new` where it's declared, and with nothing else. `= Open` would give one instance two names. A member set later, by a call, or by an expression that only starts with `new`, may be another member or null. |
| CMTK1020 | A closed set declared as a record. `with` copies a member into an instance that is none, and a record compares by value where members are singletons. |
| CMTK1021 | A closed set's member named as a member the generators add (`Name`, `QualifiedName`, `All`, `Match`, `Parse`, `TryParse`, `ToString`, the JSON or type converter), a set itself named as one of them (a member can't have the name of its type), or a member whose name differs from another member's only in case. Those collide in `TryParse(name, ignoreCase: true)`, and in `Match`'s parameters where the first letter is the difference. Also two members whose `Match` parameters would have one name, which lower-casing the first letter gives a member starting with the Kelvin sign (U+212A) and one starting with `K`. |
| CMTK1022 | An abstract type with a value-object marker. It would get no member, and a value held as it, of a type derived from it, would have no `Value` and be written as `{}`. Declare the marker on the sealed type. An abstract closed set is CMTK1014. |
| CMTK1023 | A warning, and the value object is generated. A base record declares `Equals(TBase)`, `GetHashCode()` or `IEquatable<TBase>.Equals`, which the generated equality, the underlying value's, doesn't call. Remove them from the base, or derive from a record without them. |

The analyzers that come with the base package add CMTK0001 to CMTK0014. See
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers).

## Reading the generated code

Set `<MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>` in the project,
then open `obj/Debug/net10.0/metalama/`. Every generated member is there as ordinary C#.
