# CodoMetis.TypeKit.EntityFrameworkCore

EF Core 10 integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit)
value objects: every one maps to a column of the type it wraps, with nothing registered per type,
and `.Value` works in a LINQ query as it does in memory.

```bash
dotnet add package CodoMetis.TypeKit.EntityFrameworkCore
```

It needs EF Core 10.0.12 or later. A project that references `Microsoft.EntityFrameworkCore` or
`Microsoft.EntityFrameworkCore.Relational` directly at an earlier version fails to restore with NU1605
(a package downgrade), so keep the EF Core packages at 10.0.12 or later.

It recognises a value object at run time by the attribute the generators put on it, so the project
that hosts the `DbContext` needs this package and the base package only, not the generators. The
domain project that declares the value objects references `CodoMetis.TypeKit.Generators`.

## Setup

```csharp
services.AddDbContext<ShopDb>(options =>
    options.UseNpgsql(connectionString)
           .UseTypeKit());
```

`UseTypeKit()` adds plugins to EF's type mapping, model conventions and query translation. It
replaces nothing, so it coexists with a provider or a library that replaces EF's converter selector,
and its position relative to `UseNpgsql` or `UseSqlite` does not matter. It lives in
`Microsoft.EntityFrameworkCore`, beside `UseNpgsql`, so it needs no `using` of its own.

It needs a relational provider. EF's in-memory provider has no relational type mapping, so there
nothing is mapped: a struct value object is stored as it is, a class value object fails the model,
and an integer key has no value generator (`SaveChanges` throws `NotSupportedException`). Tests that
need a database in memory use SQLite's (`Data Source=:memory:`).

An application that builds EF's internal service provider itself registers the same services there:

```csharp
var internalServices = new ServiceCollection()
    .AddEntityFrameworkNpgsql()
    .AddEntityFrameworkTypeKit()
    .BuildServiceProvider();
```

## What maps

```csharp
public class Order
{
    public required OrderId Id { get; init; }              // key: uuid
    public required CustomerId CustomerId { get; set; }    // foreign key: uuid
    public required ProductCode Code { get; set; }         // text, keeps HasMaxLength(10)
    public Discount? Discount { get; set; }                // nullable integer
    public required Amount Total { get; set; }             // numeric
    public required PlacedAt PlacedAt { get; set; }        // timestamp with time zone on PostgreSQL
    public List<Tag> Tags { get; set; } = [];              // primitive collection: text[] on PostgreSQL, JSON elsewhere
}
```

Keys, foreign keys, nullable properties, primitive collections and query parameters all map as
scalars, and a facet configured on the property, such as a maximum length or a column type, is
kept. (`required` is what the analyzer's CMTK0006 asks for: a value object that nothing assigns
would be a `default` instance.) A property can still name the converter explicitly:

```csharp
modelBuilder.Entity<Order>().Property(o => o.Code).HasConversion<ValueObjectConverter<ProductCode, string>>();
```

**Keys.** A single-column key over an integer (`int`, `long`, `short`) is generated on add, as a key
of that integer type is. On PostgreSQL and SQL Server it is an identity column, so switching an `int`
key to a value object there changes no schema. A key over a `Guid` is the application's to assign,
`Id = OrderId.New()` (a version 7 Guid, which sorts by creation time), and EF inserts it as given. For
EF to generate it instead, configure `ValueGeneratedOnAdd()` on the property; EF then also takes an
entity whose key is already set, reached through a navigation, for an existing one. An explicit
configuration always wins over these defaults.

**SQLite: configure integer keys with `UseAutoincrement()`.** SQLite fills such a key in, but EF's
SQLite provider makes a key `AUTOINCREMENT` only when the property's CLR type is an integer, and a
value object is not. A migration snapshot records the column as an `int`, which is, so without the
configuration the model never matches its snapshot: every migration repeats an `AlterColumn` (a
table rebuild on SQLite), `migrations has-pending-model-changes` always reports changes, and
`Migrate()` throws for `PendingModelChangesWarning`. Configure each such key:

```csharp
modelBuilder.Entity<Invoice>().Property(i => i.Id).UseAutoincrement();
```

A model that also runs on another provider does this under `if (Database.IsSqlite())`.

**Model-wide conventions follow the property's type.** EF applies `ConfigureConventions` by CLR
type, so `configurationBuilder.Properties<decimal>().HavePrecision(18, 2)` reaches `decimal`
properties and not an `Amount` that wraps one. Configure the value object instead:
`configurationBuilder.Properties<Amount>().HavePrecision(18, 2)`.

**`Option` and `Result` are not columns.** EF cannot map them, and the model says so. An optional
value is `T?` on the entity, and `ToOption()` converts it where the domain wants an `Option`.

## Queries

```csharp
db.Orders.Where(o => o.Id == id);                            // WHERE o."Id" = @id
db.Orders.Where(o => ids.Contains(o.Id));                    // WHERE o."Id" = ANY (@ids)
db.Orders.Where(o => o.Code.Value.StartsWith("A"));          // WHERE o."Code" LIKE 'A%'
db.Orders.Where(o => o.Total.GetValue() > 100m);             // WHERE o."Total" > 100.0
db.Orders.Where(o => o.Discount.ValueOrNull() > 10);         // WHERE o."Discount" > 10
db.Orders.Where(o => o.Tags.Contains(tag));                  // WHERE @tag = ANY (o."Tags")
db.Orders.Where(o => o.Tags.Any(t => t.Value == "x"));       // WHERE EXISTS (SELECT 1 FROM unnest(o."Tags") AS t(value) WHERE t.value::text = 'x')
db.Orders.OrderBy(o => o.PlacedAt);
```

`.Value`, `GetValue()` and `ValueOrNull()` translate to the column itself, so the wrapped type's
own operations, `StartsWith`, arithmetic, comparisons, are available on it. A table's column is
re-typed, not cast, so an index on it still serves the query. The element of a primitive collection
(`o.Tags.Any(t => t.Value == "x")`), a property mapped into a JSON column and a parameter are
converted, which works everywhere and may add a cast. `ValueOrNull()` is for an optional value
object, where `.Value` would first unwrap the `Nullable`.

Ordering in SQL follows the database's collation for the column, while the generated comparison in
memory is ordinal for a string, as it is for any string column.

The mapping is tested on SQLite and PostgreSQL, the SQL of each translation is pinned on PostgreSQL,
and every wrapped-type family makes a round trip through a real PostgreSQL.

## Reading back skips validation

A column is read through `IValueObjectMaterializer<,>`, not through `Create`. A rule added to a
value object later must not make the rows written before it unreadable, and what the application
wrote is trusted by contract. Input still goes through `Create`: nothing in this package is reachable
from a request, and the analyzer reports `Materialize` called anywhere else (CMTK0004).

When a rule is added, `Revalidate()` finds the rows it refuses:

```csharp
var orders = await db.Orders.AsNoTracking().ToListAsync();
foreach (var order in orders.Where(o => o.Code.Revalidate().State == ResultState.Error))
    log.StoredCodeNowRefused(order.Id);
```

## Compiled models and Native AOT

EF Core runs under Native AOT only through a compiled model and precompiled queries, and supports
that experimentally:

```bash
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

`UseTypeKit()` works there: value objects map through the compiled model, `.Value` translates in
precompiled queries, a value-object parameter binds, a stored value reads back without the rules, and
a key over an integer is generated by the database, in the native binary (the consumer smoke test
runs exactly that). A compiled model without Native AOT
works the same way. This package adds no trim or AOT warning of its own; EF's `DbContext`
constructors report theirs, which EF's documentation covers.

Three EF limitations to know:

- **A struct value object that wraps a reference type** (`string`, `Uri`) cannot be a property in a
  compiled model: EF cannot write its sentinel, since `default(ProductCode)` converts to `null`, and
  `dbcontext optimize` fails with "The type mapping for 'ProductCode' has not implemented code
  literal generation". Declare such a value object as a `sealed partial record`.
- **A sealed entity class** does not compile in EF 10's precompiled queries (CS0030 in the generated
  interceptors).
- **Precompiled queries need the converter `UseTypeKit()` composes.** They cast a property's
  converter to the type it had at design time, which the compiled model does not recreate for a
  `ValueObjectConverter<,>` named in `HasConversion`.
