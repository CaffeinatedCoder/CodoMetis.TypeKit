# Value objects

A value object wraps exactly one value, is equal to another when the wrapped values are equal, and
is declared in one line. The contracts live in `CodoMetis.TypeKit`, the generation in
`CodoMetis.TypeKit.Generators`, which the project that declares value objects references. This page
is about designing them: which shape to pick, how to write `Create`, what a fault should carry, and
which factory to call where. What is generated, member by member, and every build error are in the
[Generators README](../src/CodoMetis.TypeKit.Generators/README.md); the contracts are in the
[base README](../src/CodoMetis.TypeKit/README.md#value-objects). The examples import `CodoMetis.TypeKit`
and `CodoMetis.TypeKit.ValueObjects`.

## Struct or class

```csharp
public readonly partial record struct Sku : IValue<string>;   // a struct: never null, and default is CMTK0001

public sealed partial record Note : IValue<string>;           // a class: for a string column in an EF Core compiled model
```

A struct is the default: it cannot be null, it allocates nothing, and the analyzer refuses a
`default` of it. Declare a `sealed partial record` class instead in two cases. A property of an EF
Core compiled model cannot be a struct that wraps a reference type, since EF cannot write its sentinel
([EF Core](ef-core.md#compiled-models)). And a class reads a JSON `null` as `null`, where a struct refuses
it, which matters only for a shape that cannot say `Note?` ([JSON](json.md#null-and-missing)). Equality,
JSON, parsing and the column are the same for both.

## Identifiers

```csharp
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct InvoiceNo : IValue<long>;
```

```csharp
var id = OrderId.New();                                                  // a version 7 Guid, which sorts by creation time
var imported = OrderId.New(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));   // its timestamp, for a migrated record
var no = InvoiceNo.From(42);

int version = id.Value.Version;                                          // 7
```

`New()` is an extension on every plain value object over a `Guid`, in the `CodoMetis.TypeKit`
namespace, so the file that calls it imports that. Identifiers created in different milliseconds sort
by creation time; two of one millisecond do not order against each other, and SQL Server's
`uniqueidentifier` compares other bytes first. `New(timestamp)` keeps only the milliseconds and throws
`ArgumentOutOfRangeException` for a time before the Unix epoch. An identifier over an integer is what the database
assigns: `From` builds one from a known number, and EF Core fills a key in on insert
([EF Core](ef-core.md#keys)). Two identifiers of different types never compare: `orderId == customerId`
does not compile, and comparing their `.Value`s is CMTK0008.

## Validated text

`Create` is the one method you write. Every generated way in applies it: JSON, parsing, the type
converter, `TryFrom` and `FromKnownGood`.

```csharp
public enum EmailFault { Blank, NoAt, TooLong }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);

        var normalised = value.Trim().ToLowerInvariant();   // equal addresses hold the same wrapped value
        if (!normalised.Contains('@')) return Result.Error(EmailFault.NoAt);
        if (normalised.Length > 254) return Result.Error(EmailFault.TooLong);

        return new Email(normalised);                       // the private constructor is generated
    }

    public string Domain => Value[(Value.IndexOf('@') + 1)..];   // computed from Value: fine. A second field is CMTK1012
}
```

```csharp
string lowered = Email.Create(" Ops@Example.com ").Match(email => email.Value, fault => fault.ToString());   // "ops@example.com"
bool same = Email.FromKnownGood("Ops@Example.com") == Email.FromKnownGood("ops@example.com");                 // true
string domain = Email.FromKnownGood("ops@example.com").Domain;                                                 // "example.com"
```

Normalise in `Create`, never in a hand-written `Equals`: equality, ordering, the JSON and the column
are all the wrapped value's, so two values that should be equal must hold the same string. A plain
value object that needs normalising becomes an `IValidatedValue` for that alone. The private
constructor applies no rules, so call it from `Create` only.

The order of the checks is the order the fault reports: `Blank` before `NoAt`, so a caller that
shows one message shows the first thing to fix.

## Faults

A fault names the rule, never the input. The text a value object refused can be a password or an
address, and the fault is what reaches an exception message and a log. An enum is enough for most
value objects, and `ToHttpResult()` turns an enum, a closed set or a `Fault<TFault>` into problem
details ([ASP.NET Core](aspnet-core.md#a-result-as-a-response)). A fault that must carry something
carries the rule's own data, and an endpoint maps it to a response itself:

```csharp
public abstract record QuantityFault
{
    public sealed record NotPositive : QuantityFault;

    public sealed record AboveLimit(int Limit) : QuantityFault;   // the limit, never the value given
}

public readonly partial record struct Quantity : IValidatedValue<Quantity, int, QuantityFault>
{
    public const int Limit = 1_000;

    public static Result<Quantity, QuantityFault> Create(int value)
    {
        if (value <= 0) return Result.Error<QuantityFault>(new QuantityFault.NotPositive());
        if (value > Limit) return Result.Error<QuantityFault>(new QuantityFault.AboveLimit(Limit));

        return new Quantity(value);
    }
}
```

```csharp
static string Describe(QuantityFault fault) => fault switch
{
    QuantityFault.NotPositive => "must be positive",
    QuantityFault.AboveLimit { Limit: var limit } => $"at most {limit}",
    _ => throw new UnreachableException(),
};

string explained = Quantity.Create(5_000).Match(quantity => quantity.ToString(), Describe);   // "at most 1000"
```

`Result.Error(new QuantityFault.AboveLimit(Limit))` alone would infer the derived type, so the base
is named as the type argument. Where the fault is an enum, `Result.Error(EmailFault.NoAt)` infers it.

## Numbers

```csharp
public readonly partial record struct Weight : IValue<decimal>;   // any decimal, with MinValue and MaxValue

public enum PercentageFault { BelowZero, AboveHundred }

public readonly partial record struct Percentage : IValidatedValue<Percentage, int, PercentageFault>
{
    public static Result<Percentage, PercentageFault> Create(int value)
    {
        if (value < 0) return Result.Error(PercentageFault.BelowZero);
        if (value > 100) return Result.Error(PercentageFault.AboveHundred);

        return new Percentage(value);
    }

    public override string ToString() => $"{Value} %";   // kept; then no formatting interface is generated, so interpolation reaches it too
}
```

```csharp
bool widest = Weight.MaxValue.Value == decimal.MaxValue;   // true
string shown = $"{Percentage.FromKnownGood(42)}";          // "42 %"
bool ordered = Percentage.FromKnownGood(5) < Percentage.FromKnownGood(50);   // true, the wrapped type's ordering
```

A plain value object over a type with its own `MinValue` and `MaxValue` gets both, and implements
`IMinMaxValue<TSelf>`; a validated one never does, since its range is `Create`'s. Every value object
over a comparable type gets `CompareTo` and the operators, ordinal for a string. The generated
`ToString()` is invariant: `Weight.From(1.5m)` prints `1.5` whatever the current culture.

## Enums, dates and other wrapped types

```csharp
public readonly partial record struct Weekday : IValue<DayOfWeek>;

public readonly partial record struct DueOn : IValue<DateOnly>;

public readonly partial record struct Homepage : IValue<Uri>;
```

```csharp
var day = Weekday.Parse("Friday", null);          // Friday
var fifth = Weekday.Parse("5", null);             // Friday, as Enum.TryParse reads a number
var due = DueOn.Parse("2026-10-13", null);        // 2026-10-13
bool sooner = DueOn.From(new DateOnly(2026, 1, 1)) < due;                   // true
bool sortable = typeof(IComparable<Homepage>).IsAssignableFrom(typeof(Homepage));   // false, a Uri has no ordering
```

The generated members follow what the wrapped type offers: parsing where it parses, formatting where
it formats, comparison where it is comparable. A `Uri` parses and formats but has no ordering; NodaTime's
types parse through their type converters. A null `IFormatProvider` means the invariant culture
throughout, so a value object's text is the same wherever the process runs, and `Parse(x.ToString(), null)`
gives `x` back wherever the wrapped type's own text is lossless: numbers, `Guid`, `DateOnly`,
`TimeSpan`, `Version`, `Uri`, enums, strings. `TimeOnly`, `DateTime` and `DateTimeOffset` print the
invariant culture's short patterns, as the BCL types do, and lose seconds (`TimeOnly`) or the fraction
of a second (and a `DateTime` its `Kind`) on the way: use `x.ToString("O", null)` or the JSON form for
those ([Parsing and formatting](../src/CodoMetis.TypeKit.Generators/README.md#parsing-and-formatting)).

What cannot be wrapped is refused at build time (CMTK1005): a generic type, a tuple included, because
a `List<T>` compares by reference; a nullable; an array; and another value object, since wrapping
what it wraps says the same with one type.

## Which factory

| Call | When | On refusal |
|---|---|---|
| `Create(value)` | Input: a request, a form, a file. The caller has to say what to fix | Returns the fault |
| `TryFrom(value)` | Valid or not is the whole question | `None` |
| `FromKnownGood(value)` | A literal in source, or a value the code has just produced | Throws, naming the call site's expression and the fault, never the value: `request.Code`, not what it held. A literal is its own expression, and is named as written (CMTK0007 points at a parameter given to it) |
| `From(value)` | A plain value object: there are no rules | Throws only for a null |
| `Revalidate()` | An instance read back without the rules, a column or a stored document, against today's `Create` | Returns the fault |

```csharp
Result<Email, EmailFault> fromRequest = Email.Create(request.Email);   // the fault says which rule refused it
Option<Email> maybe = Email.TryFrom(request.Email);                    // is it valid?
Email support = Email.FromKnownGood("support@example.com");            // a refusal here is a bug at this line
InvoiceNo first = InvoiceNo.From(1);                                   // no rules
Result<Email, EmailFault> stillValid = support.Revalidate();           // today's rules, applied again
```

A validated value object has no `From`, so nothing that looks harmless throws on input. The explicit
materializer that EF Core reads a column through is the one way in that skips `Create`, and calling
it anywhere else is CMTK0004 ([EF Core](ef-core.md#reading-back-without-validation)).

## Your own members

A value object may declare `TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)` and
`ToString()`; each is kept, and what depends on it is derived from it. Any other member the generators
introduce, written by hand, is CMTK1011, and any instance state beside the wrapped value is CMTK1012.

```csharp
public readonly partial record struct Priority : IValue<int>
{
    public int CompareTo(Priority other) => other.Value.CompareTo(Value);   // 1 ranks above 2; the operators follow
}
```

```csharp
var first = new[] { Priority.From(3), Priority.From(1) }.Min();   // 3
bool urgent = Priority.From(1) > Priority.From(2);                // true
```

A database cannot run a hand-written `CompareTo`, so an EF Core query that compares or orders such a
value object is refused ([EF Core](ef-core.md#queries)); order by `.Value` where the wrapped value's
order is meant.

A computed property, a static member and a constant are fine, as `Email.Domain` and `Quantity.Limit`
above. A `ToString()` that masks a secret is in [Logging and secrets](logging-and-secrets.md).

## Declared inside another type

```csharp
public sealed partial class Shipment
{
    public readonly partial record struct Id : IValue<Guid>;   // Shipment.Id; its companion class is ShipmentIdExtensions

    public required Id Key { get; init; }
}
```

The companion class, which holds `GetValue()` and `ValueOrNull()` for EF Core queries, is named
after the whole nesting chain, so `Shipment.Id` and `Order.Id` in one namespace do not collide. The
OpenAPI document names the component the same way, `ShipmentId`.

## Generic code over value objects

The contracts are static abstract, so one method serves every value object of a kind. The generated
types also implement `IEqualityOperators`, `IComparisonOperators` and `IMinMaxValue` where the wrapped
type allows, so they satisfy generic-math constraints.

```csharp
static Result<IReadOnlyList<TSelf>, TFault> CreateAll<TSelf, T, TFault>(IEnumerable<T> values)
    where TSelf : IValidatedValue<TSelf, T, TFault>
    where T : notnull
    where TFault : notnull =>
    values.Traverse(TSelf.Create);   // the first fault stops it; nothing after it is created
```

```csharp
string outcome = CreateAll<Email, string, EmailFault>(["ops@example.com", "nobody"]).Match(list => $"{list.Count} valid", fault => $"refused: {fault}");   // "refused: NoAt"
```

## Reading what was generated

Set `<MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>` in the
declaring project and read `obj/Debug/net10.0/metalama/`: every generated member is there as ordinary
C#. A declaration the generators cannot complete fails the build with CMTK1000 to CMTK1012 or CMTK1022, listed in
the [Generators README](../src/CodoMetis.TypeKit.Generators/README.md#build-errors).
