# Value objects

A value object gives a plain value, like a `Guid` or a `string`, a type of its own. An `OrderId`
can't be passed where a `CustomerId` is expected, an `Email` is always valid, and you declare each
one in a single line.

This page helps you design them: pick a struct or a class, write the validation, decide what an
error carries, and call the right factory. The
[Generators README](../src/CodoMetis.TypeKit.Generators/README.md) lists everything that is
generated, member by member, and every build error. The interfaces themselves are in the
[base README](../src/CodoMetis.TypeKit/README.md#value-objects). To see value objects cross every
boundary of one service, from the request to the database, the log and back, read the
[Walkthrough](walkthrough.md).

Reference `CodoMetis.TypeKit.Generators` in the project that declares value objects. The examples
import `CodoMetis.TypeKit` and `CodoMetis.TypeKit.ValueObjects`.

## Struct or class

```csharp
public readonly partial record struct Sku : IValue<string>;   // a struct: never null, and default is CMTK0001

public sealed partial record Note : IValue<string>;           // a class: for a string column in an EF Core compiled model
```

Start with a struct. It can't be null, it allocates nothing, and the analyzer stops anyone from
creating an empty `default` one (CMTK0001).

Choose a `sealed partial record` class in two cases:

- **A property of an EF Core compiled model.** A compiled model can't hold a struct that wraps a
  reference type, because EF can't write its sentinel value ([EF Core](ef-core.md#compiled-models)).
- **A JSON shape that can't say `Note?`.** A class reads a JSON `null` as `null`, while a struct
  rejects it ([JSON](json.md#null-and-missing)).

Everything else is the same for both: equality, JSON, parsing and the database column.

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

`New()` creates a version 7 `Guid`, so new identifiers sort by when they were created. It works on
every plain value object over a `Guid`. It lives in the `CodoMetis.TypeKit` namespace, so import that
where you call it.

An identifier over an integer usually comes from the database. Use `From` for a number you already
have, and EF Core fills in a new key on insert ([EF Core](ef-core.md#keys)).

Identifiers of different types can't be mixed up. `orderId == customerId` doesn't compile, and
comparing their `.Value`s gets a warning (CMTK0008).

**Good to know**

- **Sorting is by millisecond.** Two identifiers created in the same millisecond have no order
  between them. SQL Server's `uniqueidentifier` compares other bytes first, so it doesn't sort them
  by time at all.
- **`New(timestamp)` is for migrated records.** It keeps only the milliseconds, and throws
  `ArgumentOutOfRangeException` for a time before the Unix epoch.

## Validate text

You write one method, `Create`. Every way a value gets created runs it: JSON, parsing, the type
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

`Create` is also the place to normalise. Equality, ordering, JSON and the database column all use
the value inside, so two addresses that should be equal must hold the same string. Don't write your
own `Equals` for this. If a plain value object needs normalising, make it an `IValidatedValue` for
that reason alone.

The checks run in the order you write them, and the first one that fails is the error you get. Here
`Blank` comes before `NoAt`, so a form that shows one message shows the first thing to fix.

The private constructor is generated for you and applies no rules. Call it only from `Create`.

## Errors

The type `Create` returns on failure is the error type. TypeKit calls it a fault. A fault names the
rule that failed, never the input. The rejected text could be a password or an address, and the
fault is what ends up in exception messages and logs.

An enum is enough for most value objects. `ToHttpResult()` turns an enum, a closed set or a
`Fault<TFault>` into a problem-details response for you
([ASP.NET Core](aspnet-core.md#a-result-as-a-response)). When a fault needs to carry data, give it
the rule's own data, like the limit that was exceeded, and map it to a response in your endpoint:

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

Name the base type as the type argument, as in `Result.Error<QuantityFault>(...)`. Without it, C#
infers the derived record. With an enum fault, `Result.Error(EmailFault.NoAt)` infers the right type
on its own.

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

**Good to know**

- **`MinValue` and `MaxValue`.** A plain value object over a type that has them gets them too, and
  implements `IMinMaxValue<TSelf>`. A validated one never does, because `Create` decides its range.
- **Comparison.** Every value object over a comparable type gets `CompareTo` and the comparison
  operators. Strings compare ordinally.
- **Culture.** The generated `ToString()` uses the invariant culture, so `Weight.From(1.5m)` prints
  `1.5` on any machine.

### Add, subtract and scale

```csharp
public readonly partial record struct Money : IValue<decimal>,
    IAdditionOperators<Money, Money, Money>,
    ISubtractionOperators<Money, Money, Money>,
    IMultiplyOperators<Money, decimal, Money>,   // Money * 3m
    IDivisionOperators<Money, Money, decimal>;   // two amounts make a ratio
```

```csharp
Money total = Money.From(10.50m) + Money.From(4.50m);   // 15.00
Money tripled = total * 3m;                             // 45.00
decimal share = Money.From(3m) / Money.From(4m);        // 0.75
```

Declare the operator interfaces you want, and TypeKit generates their operators. Each operator does what
the underlying type's operator does: the same result, the same overflow, the same checked context.
Nothing is generated unless you declare it.

The first type argument is the value object. The other operand and the result can each be the value
object or another type, as long as the underlying type has that operator itself. `Money * 3m` needs
`decimal * decimal`, and `Money / Money` returning a `decimal` needs the same.

**Good to know**

- **Which interfaces.** `IAdditionOperators`, `ISubtractionOperators`, `IMultiplyOperators`,
  `IDivisionOperators`, `IModulusOperators`, `IUnaryNegationOperators` and `IUnaryPlusOperators`.
- **Numbers have them, dates don't.** The number types and `char` implement these interfaces.
  `DateTime`, `TimeSpan`, `DateOnly` and `Guid` implement none of them, so a value object over one gets
  no operator, and the interface you declared fails the build (CS0535).
- **Plain value objects only.** A validated value object gets none. An operator can't return a
  `Result`, and a sum outside the range `Create` accepts would have to throw. The interface you
  declared fails the build (CS0535) until you write the operator yourself.
- **Your own operator is kept.** Write one yourself, or implement the interface explicitly, and TypeKit
  keeps it, with its checked form. It generates only the operators you haven't written.
- **The checked context.** `checked(a + b)` uses the checked operator, as it would for an `int`. A value
  object over an `int` throws `OverflowException` there, and wraps around outside it.
- **In an EF Core query.** An operator between two value objects runs in the database, as it would for
  the underlying type: `o.Price + o.Tax > limit` keeps the rows memory keeps. One with another operand or
  result, such as `o.Price * 2m`, throws there for now. Write `o.Price.Value * 2m` instead.

## Enums, dates and other types

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

What's generated depends on what the underlying type can do. You get parsing if it parses,
formatting if it formats, and comparison if it's comparable. A `Uri` parses and formats but has no
order. NodaTime types parse through their type converters.

Text is the same on every machine, because a null `IFormatProvider` means the invariant culture.
`Parse(x.ToString(), null)` gives you `x` back whenever the underlying type's text is lossless:
numbers, `Guid`, `DateOnly`, `TimeSpan`, `Version`, `Uri`, enums and strings.

**Good to know**

- **Some date and time types lose detail as text.** `TimeOnly`, `DateTime` and `DateTimeOffset`
  print the invariant culture's short patterns, as the BCL types do. `TimeOnly` loses its seconds, the
  other two lose the fraction of a second, and a `DateTime` loses its `Kind`. Use
  `x.ToString("O", null)` or JSON for those
  ([Parsing and formatting](../src/CodoMetis.TypeKit.Generators/README.md#parsing-and-formatting)).
- **Some types can't be wrapped, and the build tells you (CMTK1005).**
  - A generic type, tuples included, because a `List<T>` compares by reference.
  - A nullable, or an array.
  - `object`, because the serializer writes it by what it holds, and a value object's converter
    can't.
  - `System.Enum`, which isn't an actual enum.
  - Another value object. Wrap its underlying type directly instead.
- **Interfaces and abstract classes can be wrapped.** Their JSON is the serializer's for that type,
  polymorphic where the type declares it.

### Accept only named enum members

C# lets any number be an enum value, and a plain value object over an enum accepts whatever the enum
accepts. `Parse`, route and query values, and JSON all take a number, even one no member has. To
accept only real members, check in `Create`. Every way of creating the value then rejects the rest:

```csharp
public enum DayFault { NoSuchDay }

public readonly partial record struct ScheduleDay : IValidatedValue<ScheduleDay, DayOfWeek, DayFault>
{
    public static Result<ScheduleDay, DayFault> Create(DayOfWeek value) =>
        Enum.IsDefined(value) ? new ScheduleDay(value) : Result.Error(DayFault.NoSuchDay);
}
```

```csharp
var ninth = Weekday.Parse("9", null);                              // 9, a Weekday no member names
bool scheduled = ScheduleDay.TryParse("9", null, out _);           // false, and JSON's 9 throws JsonException naming ScheduleDay and NoSuchDay
bool friday = ScheduleDay.TryParse("Friday", null, out _);         // true
```

`Enum.IsDefined` also rejects combinations of a `[Flags]` enum's members, so check the bits for those
instead. If you want a fixed set of values that never grows, use a [closed set](closed-sets.md). It
has no numbers at all.

## Pick the right factory

| Call | Use it for | When the value is invalid |
|---|---|---|
| `Create(value)` | Input from a request, a form or a file, where someone needs to know what to fix | Returns the error |
| `TryFrom(value)` | When valid or not is all you need to know | Returns `None` |
| `FromKnownGood(value)` | A literal in your code, or a value your code just produced | Throws. The message names the expression at the call site and the error, never the value: `request.Code`, not what it held. A literal is named as written. CMTK0007 flags a parameter passed straight in |
| `From(value)` | A plain value object, which has no rules | Throws only for null |
| `Revalidate()` | A value read back without validation, from a column or a stored document, checked against today's `Create` | Returns the error |

```csharp
Result<Email, EmailFault> fromRequest = Email.Create(request.Email);   // the fault says which rule refused it
Option<Email> maybe = Email.TryFrom(request.Email);                    // is it valid?
Email support = Email.FromKnownGood("support@example.com");            // a refusal here is a bug at this line
InvoiceNo first = InvoiceNo.From(1);                                   // no rules
Result<Email, EmailFault> stillValid = support.Revalidate();           // today's rules, applied again
```

A validated value object has no `From`, so nothing harmless-looking throws on user input. The only
way around `Create` is the explicit reader EF Core uses for database columns. Calling it anywhere
else fails the build (CMTK0004, [EF Core](ef-core.md#reading-back-without-validation)).

## Write your own members

You can write five members yourself: `TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)` and
`ToString()`. TypeKit keeps yours and builds everything that depends on them from them. Writing any
other generated member by hand fails the build (CMTK1011), and so does a second field next to the
value (CMTK1012). An arithmetic operator you write is kept too ([Add, subtract and scale](#add-subtract-and-scale)).

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

Computed properties, static members and constants are fine, like `Email.Domain` and `Quantity.Limit`
above. To hide a secret in `ToString()`, see [Logging and secrets](logging-and-secrets.md).

**Good to know**

- **A database can't run your `CompareTo`.** An EF Core query that compares or orders such a value
  object is rejected ([EF Core](ef-core.md#queries)). Order by `.Value` when the underlying value's
  order is what you mean.

## Declare one inside another type

```csharp
public sealed partial class Shipment
{
    public readonly partial record struct Id : IValue<Guid>;   // Shipment.Id; its companion class is ShipmentIdExtensions

    public required Id Key { get; init; }
}
```

Nested value objects work. The companion class, which holds `GetValue()` and `ValueOrNull()` for EF
Core queries, is named after the whole nesting chain. So `Shipment.Id` and `Order.Id` in one namespace
don't collide. The OpenAPI document names the schema the same way, `ShipmentId`.

## Write generic code over value objects

The interfaces use static abstract members, so one generic method can serve every value object of a
kind. Where the underlying type allows it, the generated types also implement `IEqualityOperators`,
`IComparisonOperators` and `IMinMaxValue`, so they fit generic-math constraints. The arithmetic operator
interfaces you declare fit them too.

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

## See the generated code

Set `<MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>` in the
project that declares your value objects, then open `obj/Debug/net10.0/metalama/`. Every generated
member is there as ordinary C#. If a declaration can't be generated, the build fails with CMTK1000 to
CMTK1012 or CMTK1022, all listed in the
[Generators README](../src/CodoMetis.TypeKit.Generators/README.md#build-errors).
