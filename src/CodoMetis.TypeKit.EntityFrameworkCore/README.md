# CodoMetis.TypeKit.EntityFrameworkCore

EF Core 10 integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit)
value objects: every one maps to a column of the type it wraps, with nothing registered per type,
and `.Value` works in a LINQ query as it does in memory.

```bash
dotnet add package CodoMetis.TypeKit.EntityFrameworkCore
```

It works at run time against the `IValueObject<,>` interface, so the project that hosts the
`DbContext` needs this package and the base package only, not the generators. The domain project
that declares the value objects references `CodoMetis.TypeKit.Generators`.

## Setup

```csharp
services.AddDbContext<ShopDb>(options =>
    options.UseNpgsql(connectionString)
           .UseTypeKit());
```

`UseTypeKit()` adds plugins to EF's type mapping and query translation. It replaces nothing, so it
coexists with a provider or a library that replaces EF's converter selector, and its position
relative to `UseNpgsql` or `UseSqlite` does not matter.

An application that builds EF's internal service provider itself registers the same services there:

```csharp
var internalServices = new ServiceCollection()
    .AddEntityFrameworkNpgsql()
    .AddEntityFrameworkTypeKit()
    .BuildServiceProvider();
```

## What maps

```csharp
public sealed class Order
{
    public OrderId Id { get; set; }                 // key: uuid
    public CustomerId CustomerId { get; set; }      // foreign key: uuid
    public ProductCode Code { get; set; }           // text, keeps HasMaxLength(10)
    public Discount? Discount { get; set; }         // nullable integer
    public Amount Total { get; set; }               // numeric
    public PlacedAt PlacedAt { get; set; }          // timestamp
    public List<Tag> Tags { get; set; } = [];       // primitive collection: text[] on PostgreSQL, JSON elsewhere
}
```

Keys, foreign keys, nullable properties, primitive collections and query parameters all map as
scalars, and a facet configured on the property, such as a maximum length or a column type, is
kept. A property can still name the converter explicitly:

```csharp
modelBuilder.Entity<Order>().Property(o => o.Code).HasConversion<ValueObjectConverter<ProductCode, string>>();
```

## Queries

```csharp
db.Orders.Where(o => o.Id == id);                            // WHERE o."Id" = @id
db.Orders.Where(o => ids.Contains(o.Id));                    // WHERE o."Id" = ANY (@ids)
db.Orders.Where(o => o.Code.Value.StartsWith("A"));          // WHERE o."Code" LIKE 'A%'
db.Orders.Where(o => o.Total.GetValue() > 100m);             // WHERE o."Total" > 100.0
db.Orders.Where(o => o.Discount.ValueOrNull() > 10);         // WHERE o."Discount" > 10
db.Orders.Where(o => o.Tags.Contains(tag));                  // WHERE @tag = ANY (o."Tags")
db.Orders.OrderBy(o => o.PlacedAt);
```

`.Value`, `GetValue()` and `ValueOrNull()` translate to the column itself, so the wrapped type's
own operations, `StartsWith`, arithmetic, comparisons, are available on it. The column is re-typed,
not cast, so an index on it still serves the query. `ValueOrNull()` is for an optional value
object, where `.Value` would first unwrap the `Nullable`.

## Reading back skips validation

A column is read through `IValueObjectMaterializer<,>`, not through `Create`. A rule added to a
value object later must not make the rows written before it unreadable, and what the application
wrote is trusted by contract. Input still goes through `Create`: nothing in this package is reachable
from a request, and the analyzer reports `Materialize` called anywhere else (CMTK0004).

When a rule is added, `Revalidate()` finds the rows it refuses:

```csharp
var orders = await db.Orders.AsNoTracking().ToListAsync();
foreach (var order in orders.Where(o => !o.Code.Revalidate()))
    log.StoredCodeNowRefused(order.Id);
```

Tested on SQLite and PostgreSQL, with SQL snapshots for every translation above.

## Compiled models and Native AOT

EF Core runs under Native AOT only through a compiled model and precompiled queries, and supports
that experimentally:

```bash
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

`UseTypeKit()` works there: value objects map through the compiled model, `.Value` translates in
precompiled queries, a value-object parameter binds, and a stored value reads back without the rules,
in the native binary (the consumer smoke test runs exactly that). A compiled model without Native AOT
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
