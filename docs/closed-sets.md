# Closed sets

An `enum` names its members and nothing more: C# only warns when a `switch` expression over one
misses a member and says nothing for a `switch` statement, a member carries no data, and on the wire
it is a number unless a converter says otherwise. A closed set, a
smart enum, is a sealed class whose every instance is a named member declared as a field of it, with
whatever data and behaviour the members need; the generators add the rest. The
[generators' README](../src/CodoMetis.TypeKit.Generators/README.md#closed-sets) is its reference;
this page is about using one. The examples import `CodoMetis.TypeKit.ClosedSets`.

## Declaring one

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

A member is a `public static readonly` field of the set's own type, created with `new` where it is
declared, and its name is the field's. The set is a `sealed partial class` whose constructors are
private, so no code outside it can make an instance: members are singletons, and equality is
reference equality. What belongs to a member lives on it, as `Fee` and `ArrivesBy` do; a set without
data declares no constructor, and the generators add a private one.

```csharp
string name = Shipping.Express.Name;                               // "Express"
string qualified = Shipping.Express.QualifiedName;                 // "Shipping.Express"
string all = string.Join(" ", Shipping.All.Select(s => s.Name));   // "Standard Express Pickup"
bool same = Shipping.Parse("Express") == Shipping.Express;         // true, members are singletons
bool known = Shipping.TryParse("Drone", out _);                    // false, no such member
bool typed = Shipping.TryParse("express", ignoreCase: true, out _); // true, when asked: a header or a query string as a client typed it
Shipping.Parse("Drone");                                           // FormatException naming Shipping and member, never the name
```

`All` holds the members in declaration order, read-only: no cast to `IList<T>` or an array writes it. `QualifiedName` keeps two sets that share a member
name apart in one log or one client. `ToString()` is the name, unless the set writes its own, which
is kept, for a label, while the name stays what JSON, parsing and the column use. `Parse` and
`TryParse` compare names ordinally, and with `ignoreCase: true` accept any casing. JSON, the column,
`IParsable` and a bound route or query parameter stay ordinal, since what is stored or sent is the
name as it is: a header or a query string a client typed in any casing is bound as a `string` and
parsed with `ignoreCase: true`. Two members whose names differ only in case are refused (CMTK1021),
so there is one member to find.

## Deciding by member

`Match` takes one delegate per member, each named after it, and a member added later fails every
call until it is handled, where a `switch` over an `enum` at most warns:

```csharp
string label = shipping.Match(
    standard: () => "Ships within a week",
    express: () => "Arrives tomorrow",
    pickup: () => "Ready at the counter");
```

Every argument is named. By position, they would follow the declaration order, and reordering two
members would swap their branches without a compile error, so the analyzer reports an argument by
position (CMTK0012), and its code fix names them. A member whose lower-cased name is a keyword is
escaped where it is written: a member `New` is `@new:`. `Match` refuses a null delegate before it
picks a branch. Where every member's answer is data, a property on the member says it once, as `Fee`
does, and no call has to list the members at all.

## On the wire

```csharp
public sealed record Quote(Shipping Method, Dictionary<Shipping, decimal> Fees);
```

```csharp
string json = JsonSerializer.Serialize(new Quote(Shipping.Express, new() { [Shipping.Standard] = 0m, [Shipping.Express] = 9.90m }));   // {"Method":"Express","Fees":{"Standard":0,"Express":9.90}}
JsonSerializer.Deserialize<Quote>("""{"Method":"Drone","Fees":{}}""");                                                                   // JsonException naming Shipping and member, never the name
```

A member is its name, as a value and as a dictionary key, and reads back as the member itself. No
naming policy renames it, since a renamed name would not read back. A JSON `null` reads as `null`,
as for any class, unless the serializer's `RespectNullableAnnotations` refuses it for a property that
is not nullable. In the OpenAPI document, with `AddTypeKit()`, a set is a string enum of its names,
`{"enum":["Standard","Express","Pickup"],"type":"string"}`, as a property, a parameter or a
collection's element, with nothing registered per set. A minimal-API route or query parameter binds
through the generated `TryParse`, ordinally, and an unknown name is a 400; a set declared in the same project as the endpoint trips ASP0020 on a route parameter, as a
value object does
([why, and the pragma](../src/CodoMetis.TypeKit.AspNetCore/README.md#a-route-parameter-declared-in-the-same-project)).

## In the database

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

With `UseTypeKit()`, a set is a text column of the member's name, in a table, in a primitive
collection of names (`text[]` on PostgreSQL, a JSON array elsewhere) and in a JSON column, with
nothing registered per set. A name the
set no longer has throws when it is read, rather than reading as null, and an instance that is no
member throws when it is written. Renaming a member renames what is stored, so the column's values
migrate with it. `ClosedSetConverter<TSet>` names the converter for an explicit configuration. A
compiled model and Native AOT map a set as they map a value object, with the EF Core limitations the
[satellite's README](../src/CodoMetis.TypeKit.EntityFrameworkCore/README.md#compiled-models-and-native-aot)
lists.

## Configuration and parsing

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

The configuration binder reads a set through its generated type converter, as MVC and anything else
that reads a string through `TypeDescriptor` does. The binder's source generator, which Native AOT
turns on, reads no type converter: there, bind the name as a `string` and `Parse` it, as
[Native AOT](native-aot.md#configuration-binding) shows for a value object. A set also implements
`IParsable<TSelf>`, explicitly, so generic code parses one through `T.Parse`.

A value object can wrap a set, `IValue<Shipping>`, declared in the same project or another: it
parses, converts and serializes by the member's name, as the set does, and a validated one applies its
`Create` to the member.

## In logs

`ToString()` is the name, so Microsoft.Extensions.Logging logs a member as `Express`. Serilog
destructures an object by its public properties, so `{@Quote}` would log a member's `Days` and `Fee`
with it; `Destructure.WithTypeKit()` (CodoMetis.TypeKit.Serilog) logs a member as what it prints, its
name or its own `ToString()`, as it does a value object ([Logging and secrets](logging-and-secrets.md)).

## As a fault type

A closed set whose members are faults declares `IFaultSet<TSelf>` in place of `IClosedSet<TSelf>`:
each member carries a `FaultKind`, `NotFound`, `Conflict`, `Invalid`, `Forbidden`, `Unavailable`,
`PreconditionFailed` or `Malformed`,
nothing HTTP-specific, and optionally a `Description`, a sentence a person reads, both through its
private constructor. In CodoMetis.TypeKit.AspNetCore, `AddFaultResponses<TSet>()` answers every
member by its kind, with its description as the problem's `detail`, so the domain names its faults
and the host maps nothing by hand
([the satellite's README](../src/CodoMetis.TypeKit.AspNetCore/README.md#a-closed-set-as-the-fault-type)).
Any other closed set is a fault type too, with a mapping of its own. A sentence about one occurrence
travels beside the member, as a `Fault<TSet>` with a detail; structured data per occurrence is the
next section's case.

## What the generators refuse

A declaration they cannot complete fails the build, naming it; the generators'
[Build errors](../src/CodoMetis.TypeKit.Generators/README.md#build-errors) table has the full text.

| The set | Error |
|---|---|
| is not `partial` | CMTK1013 |
| is not `sealed`: a set with subtypes is not closed | CMTK1014 |
| declares no member | CMTK1015 |
| has a constructor that is not private | CMTK1016 |
| has another static member of its own type: a property, a field that is not `readonly`, or a field that is not public, which would hold an instance that is no member | CMTK1017 |
| is generic, or nested in a generic type | CMTK1018 |
| has a member not created with `new` where it is declared: `Legacy = Open` would give one instance two names | CMTK1019 |
| is a record: `with` would copy a member into an instance that is none | CMTK1020 |
| has a member named as a generated member, or two whose names differ only in case (`Open`, `open`), which collide in `Match`'s parameters and in `TryParse(name, ignoreCase: true)`, or two whose `Match` parameters would have one name (a Kelvin sign and a `K`), or is itself named as a generated member (`Name`, `Match`): a member cannot have the name of its type | CMTK1021 |
| writes a generated member by hand, `ToString()` apart | CMTK1011 |
| writes a member outside its declaration, in its static constructor or another field's initializer: `Open = Closed` would leave `Open` holding the instance named `Closed` | CMTK0013 |
| is compiled without CodoMetis.TypeKit.Generators | CMTK0002 |

The set's own code can still call its private constructor beside its members, and such an instance
has no name: `Name`, `ToString()`, `Match`, JSON and the column throw for it, rather than carry a
null ([why](../src/CodoMetis.TypeKit/README.md#an-instance-that-is-not-a-member-of-its-set)).

## When an `enum` is still right

- **Flags.** A combination of members, `Read | Write`, is an `enum`'s; a closed set's instance is
  exactly one member.
- **A number another system defines**, a protocol code or a column you do not own: a set stores and
  sends its names.
- **Data that varies per occurrence.** A member's data is constant, declared once; a refusal that
  carries what was refused (`OutsidePeriod(date)`) is a discriminated union's job, which C# 15 unions
  take up, a question for 3.0.0.
