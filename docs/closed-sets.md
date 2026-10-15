# Closed sets

A closed set is an enum that can carry data and behaviour. It's also called a smart enum. Each member
is a named instance with whatever properties and methods it needs, it travels as its name in JSON and
in the database, and the build fails when a `Match` misses a member.

Plain enums fall short in three ways:

- **Missed cases go unnoticed.** C# only warns when a `switch` expression misses a member, and says
  nothing for a `switch` statement.
- **Members carry no data.**
- **On the wire an enum is a number**, unless you add a converter.

A closed set is a sealed class, and every instance of it is a member declared as a field. You write
the members and their data, and the generators add the rest. This page shows how to use one. The
[Generators README](../src/CodoMetis.TypeKit.Generators/README.md#closed-sets) is the reference. The
examples import `CodoMetis.TypeKit.ClosedSets`.

## Declare a closed set

```csharp
public sealed partial class Shipping : IClosedSet<Shipping>
{
    public static readonly Shipping Standard = new(days: 5, fee: 0m);
    public static readonly Shipping Express = new(days: 1, fee: 9.90m);
    public static readonly Shipping Pickup = new(days: 0, fee: 0m);

    private Shipping(int days, decimal fee) { Days = days; Fee = fee; }

    public int Days { get; }
    public decimal Fee { get; }

    public DateOnly ArrivesBy(DateOnly ordered) => ordered.AddDays(Days);
}
```

Each member is a `public static readonly` field of the set's own type, created with `new` right where
it's declared. Its name is the field's name.

The set is a `sealed partial class` with only private constructors, so no code outside it can create
an instance. That makes every member a singleton, and equality is reference equality.

Put what belongs to a member on the member, as `Fee` and `ArrivesBy` are here. A set without data
needs no constructor at all. The generators add a private one.

```csharp
string name = Shipping.Express.Name;                               // "Express"
string qualified = Shipping.Express.QualifiedName;                 // "Shipping.Express"
string all = string.Join(" ", Shipping.All.Select(s => s.Name));   // "Standard Express Pickup"
bool same = Shipping.Parse("Express") == Shipping.Express;         // true, members are singletons
bool known = Shipping.TryParse("Drone", out _);                    // false, no such member
bool typed = Shipping.TryParse("express", ignoreCase: true, out _); // true, when asked: a header or a query string as a client typed it
Shipping.Parse("Drone");                                           // FormatException naming Shipping and member, never the name
```

- **`All`** holds the members in the order you declared them. It's read-only: casting it to
  `IList<T>` or an array doesn't let anyone write to it.
- **`QualifiedName`** keeps two sets that share a member name apart, in one log or one client.
- **`ToString()`** returns the name. If you write your own `ToString()` for a label, TypeKit keeps it,
  and JSON, parsing and the column still use the name.
- **`Parse` and `TryParse`** compare names ordinally. Pass `ignoreCase: true` to accept any casing.

**Good to know**

- **Everything else is case-sensitive.** JSON, the database column, `IParsable` and a bound route or
  query parameter all compare ordinally, because what is stored or sent is the name exactly as
  written. For a header or a query string that a client may type in any casing, bind it as a `string`
  and parse it with `ignoreCase: true`.
- **Two members whose names differ only in case fail the build (CMTK1021).** That way there is only
  ever one member to find.

## Handle every member

`Match` takes one delegate per member, each named after it. When someone adds a member later, every
`Match` call fails to build until it handles the new one. A `switch` over an enum only warns, at best.

```csharp
string label = shipping.Match(
    standard: () => "Ships within a week",
    express: () => "Arrives tomorrow",
    pickup: () => "Ready at the counter");
```

Every argument is named. Positional arguments would follow the declaration order, so reordering two
members would swap their branches without any compile error. The analyzer flags a positional
argument (CMTK0012), and its code fix adds the names.

When every member's answer is data, put it in a property on the member instead, as `Fee` does. Then
no call has to list the members at all.

**Good to know**

- **Keyword names are escaped.** When a member's lower-cased name is a C# keyword, the parameter is
  escaped: a member `New` is written `@new:`.
- **Null delegates are rejected.** `Match` throws for a null delegate before it picks a branch.

## Send it as JSON

```csharp
public sealed record Quote(Shipping Method, Dictionary<Shipping, decimal> Fees);
```

```csharp
string json = JsonSerializer.Serialize(new Quote(Shipping.Express, new() { [Shipping.Standard] = 0m, [Shipping.Express] = 9.90m }));   // {"Method":"Express","Fees":{"Standard":0,"Express":9.90}}
JsonSerializer.Deserialize<Quote>("""{"Method":"Drone","Fees":{}}""");                                                                   // JsonException naming Shipping and member, never the name
```

A member is written as its name, both as a value and as a dictionary key. It reads back as the member
itself. No naming policy renames it, because a renamed name wouldn't read back.

In the OpenAPI document, with `AddTypeKit()`, a set is a string enum of its names:
`{"enum":["Standard","Express","Pickup"],"type":"string"}`. That holds for a property, a parameter
and a collection's element, and you register nothing per set.

A minimal-API route or query parameter binds through the generated `TryParse`, ordinally. An unknown
name gets a 400.

**Good to know**

- **A JSON `null` reads as `null`**, as for any class. The exception is a property that isn't
  nullable when the serializer's `RespectNullableAnnotations` is on, which then rejects it.
- **ASP0020 can appear on a route parameter.** A set declared in the same project as the endpoint
  triggers it, just as a value object does
  ([why, and the pragma](../src/CodoMetis.TypeKit.AspNetCore/README.md#a-route-parameter-declared-in-the-same-project)).

## Store it in the database

```csharp
public sealed class Order
{
    public int Id { get; set; }
    public Shipping Method { get; set; } = Shipping.Standard;
    public List<Shipping> Offered { get; set; } = [];
}
```

```csharp
db.Orders.Where(o => o.Method == Shipping.Express);   // WHERE o."Method" = 'Express'
```

With `UseTypeKit()`, a set is stored as a text column holding the member's name. That works in a
table, in a primitive collection of names (`text[]` on PostgreSQL, a JSON array elsewhere) and in a
JSON column. You register nothing per set.

**Good to know**

- **Unknown names throw.** A stored name the set no longer has throws when it's read, rather than
  reading as null. An instance that isn't a member throws when it's written.
- **Renaming a member changes what is stored.** Migrate the column's values along with the rename.
- **`ClosedSetConverter<TSet>`** is the converter to name in an explicit configuration.
- **Compiled models and Native AOT** map a set just as they map a value object, with the EF Core
  limitations the
  [EF Core package's README](../src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#compiled-models-and-native-aot)
  lists.

## Read it from configuration

```csharp
public sealed class CheckoutOptions
{
    public Shipping Default { get; set; } = Shipping.Standard;
}
```

```csharp
IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Checkout:Default"] = "Express" }).Build();
string chosen = configuration.GetSection("Checkout").Get<CheckoutOptions>()!.Default.Name;   // "Express"
```

The configuration binder reads a set through its generated type converter. So does MVC, and anything
else that reads a string through `TypeDescriptor`.

A set also implements `IParsable<TSelf>`, explicitly, so generic code can parse one through
`T.Parse`.

A value object can wrap a set, as in `IValue<Shipping>`, declared in the same project or another. It
parses, converts and serializes by the member's name, just as the set does. A validated one runs its
`Create` on the member.

**Good to know**

- **Under Native AOT, bind the name as a string.** The binder's source generator, which Native AOT
  turns on, doesn't use type converters. Bind the name as a `string` and call `Parse`, as
  [Native AOT](native-aot.md#configuration-binding) shows for a value object.

## Log it

`ToString()` returns the name, so Microsoft.Extensions.Logging logs a member as `Express`.

Serilog destructures an object by its public properties, so `{@Quote}` would log a member's `Days`
and `Fee` along with it. `Destructure.WithTypeKit()` (from CodoMetis.TypeKit.Serilog) logs a member
as what it prints instead: its name, or its own `ToString()`. It does the same for a value object
([Logging and secrets](logging-and-secrets.md)).

## Use one as an error type

A closed set whose members are errors (TypeKit calls them faults) declares `IFaultSet<TSelf>` instead
of `IClosedSet<TSelf>`. Through its private constructor, each member gets:

- **A `FaultKind`**: `NotFound`, `Conflict`, `Invalid`, `Forbidden`, `Unavailable`,
  `PreconditionFailed` or `Malformed`. None of them is HTTP-specific.
- **An optional `Description`**, a sentence a person reads.

In CodoMetis.TypeKit.AspNetCore, `AddFaultResponses<TSet>()` answers every member by its kind, with
its description as the problem's `detail`. Your domain names its errors, and the web project maps
nothing by hand
([the ASP.NET Core package's README](../src/CodoMetis.TypeKit.AspNetCore/README.md#a-closed-set-as-the-fault-type)).

Any other closed set works as an error type too, with a mapping of its own.

**Good to know**

- **Per-occurrence details travel beside the member.** A sentence about one occurrence goes in a
  `Fault<TSet>` with a detail. So does the rule that rejected the value, a member of another enum or
  closed set (`WithRule`). Any other data per occurrence calls for a union
  ([below](#when-an-enum-is-still-the-right-choice)).

## What fails the build

A declaration the generators can't complete fails the build and names the type. The Generators
README's [Build errors](../src/CodoMetis.TypeKit.Generators/README.md#build-errors) table has the full
text of each.

| The set | Error |
|---|---|
| isn't `partial` | CMTK1013 |
| isn't `sealed`. A set with subtypes isn't closed | CMTK1014 |
| declares no member | CMTK1015 |
| has a constructor that isn't private | CMTK1016 |
| has another static member of its own type: a property, a field that isn't `readonly`, or a field that isn't public. It would hold an instance that is no member | CMTK1017 |
| is generic, or nested in a generic type | CMTK1018 |
| has a member not created with `new` where it's declared. `Legacy = Open` would give one instance two names | CMTK1019 |
| is a record. `with` would copy a member into an instance that is none | CMTK1020 |
| has a member named like a generated member. Or two members whose names differ only in case (`Open`, `open`), which collide in `Match`'s parameters and in `TryParse(name, ignoreCase: true)`. Or two members whose `Match` parameters would get one name (a Kelvin sign and a `K`). Or the set itself is named like a generated member (`Name`, `Match`), since a member can't have the name of its type | CMTK1021 |
| writes a generated member by hand, other than `ToString()` | CMTK1011 |
| assigns a member outside its declaration, in its static constructor or another field's initializer. `Open = Closed` would leave `Open` holding the instance named `Closed` | CMTK0013 |
| is compiled without CodoMetis.TypeKit.Generators | CMTK0002 |

**Good to know**

- **An instance made by the set's own code has no name.** The set's own code can still call its
  private constructor outside its member declarations. `Name`, `ToString()`, `Match`, JSON and the
  column then throw for that instance, rather than carry a null
  ([why](../src/CodoMetis.TypeKit/README.md#an-instance-that-is-not-a-member-of-its-set)).

## When an enum is still the right choice

- **Flags.** A combination of members, like `Read | Write`, is an enum's job. An instance of a closed
  set is exactly one member.
- **A number another system defines**, like a protocol code or a column you don't own. A set stores
  and sends its names.
- **Data that varies per occurrence.** A member's data is constant, declared once. An error that
  carries what was rejected (`OutsidePeriod(date)`) is a job for a discriminated union. C# 15 unions
  cover that, and whether TypeKit builds on them is a question for 3.0.0.
