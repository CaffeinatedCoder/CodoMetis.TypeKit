# CodoMetis.TypeKit.EntityFrameworkCore

Use [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit) value objects in EF Core
10 with one line of setup. Every value object is stored in a plain column of its underlying type,
with nothing to register per type, and `.Value` works in a LINQ query the way it does in memory.

```bash
dotnet add package CodoMetis.TypeKit.EntityFrameworkCore
```

This page is the reference for the package. For a walk through a whole model, read the
[EF Core guide](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/ef-core.md).

**Good to know**

- **It needs EF Core 10.0.12 or later.** A project that references `Microsoft.EntityFrameworkCore`
  or `Microsoft.EntityFrameworkCore.Relational` directly at an earlier version fails to restore with
  NU1605 (a package downgrade). Keep the EF Core packages at 10.0.12 or later.
- **The generators stay in your domain project.** This package recognises a value object at run time
  by the attribute the generators put on it. So the project that hosts the `DbContext` needs only
  this package and the base package. The domain project that declares the value objects references
  `CodoMetis.TypeKit.Generators`.

## Setup

```csharp
services.AddDbContext<ShopDb>(options =>
    options.UseNpgsql(connectionString)
           .UseTypeKit());
```

`UseTypeKit()` adds plugins to EF's type mapping, model conventions and query translation. It
replaces nothing. So it works alongside a provider or a library that replaces EF's converter
selector, and it doesn't matter whether you call it before or after `UseNpgsql` or `UseSqlite`. It
lives in the `Microsoft.EntityFrameworkCore` namespace, beside `UseNpgsql`, so it needs no `using` of
its own.

If your application builds EF's internal service provider itself, register the same services there:

```csharp
var internalServices = new ServiceCollection()
    .AddEntityFrameworkNpgsql()
    .AddEntityFrameworkTypeKit()
    .BuildServiceProvider();
```

**Good to know**

- **It needs a relational provider.** EF's in-memory provider has no relational type mapping, so
  nothing is mapped there. A struct value object is stored as it is, a class value object fails the
  model, and an integer key has no value generator (`SaveChanges` throws `NotSupportedException`).
  For a database in memory in tests, use SQLite's (`Data Source=:memory:`).

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

Keys, foreign keys, nullable properties, primitive collections and query parameters all map as plain
columns of the underlying type. A facet you configure on the property, such as a maximum length or a
column type, is kept. (`required` is what the analyzer's CMTK0006 asks for. A value object nothing
assigns would be a `default` instance.)

A property can still name the converter explicitly:

```csharp
modelBuilder.Entity<Order>().Property(o => o.Code).HasConversion<ValueObjectConverter<ProductCode, string>>();
```

### Keys

A single-column key over an integer is generated on add, like a key of that integer type. That's EF's
own list: `int`, `long`, `short`, `sbyte`, `uint`, `ulong` and `ushort`, and not `byte`.

Whether a value then gets generated is up to the provider, as for a plain key. On PostgreSQL and SQL
Server it's an identity column, so switching an `int` key to a value object changes no schema there.
Where the provider has no identity for the type, the key isn't generated, just as a plain one isn't.
On PostgreSQL, for example, a `ulong` is a `numeric(20,0)`, which can't be an identity.

At `Add`, a key that is generated gets the same temporary value a plain key of the underlying type
gets, also where the column is of another type, such as the `decimal` that holds a `ulong`. HiLo and a
value generator of your own still come first. There are two exceptions:

- **A key over `char` is yours to assign.** It's never generated, on any provider, because none
  generates a `char` key. PostgreSQL rejects an identity on `character(1)`, SQLite an `AUTOINCREMENT`
  on `TEXT`, and SQL Server has no identity for `nvarchar(1)`. On SQL Server, `Add` used to give the
  value object one character of a GUID string, which was then stored. TypeKit stops that invented
  value, but it doesn't check that you set a meaningful one. A default key, `'\0'`, is stored as it
  is, and a second such row fails on the primary key. `ValueGeneratedOnAdd()` or a value generator of
  your own on the property still applies.
- **A default key over `ulong` can still throw at `Add`** where the column is a `decimal` (SQL
  Server, PostgreSQL), in two cases. One is a compiled model made with `dotnet ef dbcontext optimize
  --nativeaot`, which writes each column's mapping out as code and loses what gives the key a
  temporary `ulong`. The other is a key you configure with `ValueObjectConverter` yourself. EF's
  temporary value for the column is then a negative `decimal`, and converting it back throws
  `OverflowException`. Otherwise the key gets a temporary `ulong`, as a plain one does. In those two
  cases, configure `ValueGeneratedNever()` on the key and assign it.

A key over a `Guid` is yours to assign, `Id = OrderId.New()` (a version 7 Guid, which sorts by
creation time), and EF inserts it as given. To have EF generate it instead, configure
`ValueGeneratedOnAdd()` on the property. It's then generated as a plain `Guid` key is: a version 7
Guid on PostgreSQL, one that's sequential in SQL Server's order on SQL Server. EF then also treats
an entity whose key is already set, reached through a navigation, as an existing one.

The root of an inheritance hierarchy follows the same rules under TPH and TPT. Under TPC, as for an
`int` key, the key is generated only where the provider numbers the whole hierarchy from a sequence
(PostgreSQL, SQL Server), an abstract root's included. SQLite has no such sequence, so there you
assign it.

A configuration you write yourself always wins over these defaults.

### SQLite: configure integer keys with `UseAutoincrement()`

```csharp
modelBuilder.Entity<Invoice>().Property(i => i.Id).UseAutoincrement();
```

SQLite fills such a key in. But EF's SQLite provider makes a key `AUTOINCREMENT` only when the
property's CLR type is an integer, and a value object isn't one. A migration snapshot records the
column as an `int`, which is. So without this configuration the model never matches its snapshot:

- **Every migration repeats an `AlterColumn`**, which is a table rebuild on SQLite.
- **`migrations has-pending-model-changes`** always reports changes.
- **`Migrate()` throws** for `PendingModelChangesWarning`.

Configure each such key, as above. A model that also runs on another provider does this under
`if (Database.IsSqlite())`.

### Model-wide conventions follow the property's type

EF applies `ConfigureConventions` by CLR type. So
`configurationBuilder.Properties<decimal>().HavePrecision(18, 2)` reaches `decimal` properties, and
not an `Amount` that wraps one. Configure the value object instead:
`configurationBuilder.Properties<Amount>().HavePrecision(18, 2)`.

### Closed sets

A closed set (`IClosedSet<TSelf>`) is stored as a text column of the member's name, and read back as
the member itself. That holds in a table, in a primitive collection of names (`text[]` on PostgreSQL,
a JSON array elsewhere) and in a JSON column alike, with nothing registered per set.

A name the set no longer has throws when it's read, rather than reading as null. An instance that
isn't a member throws when it's written. `ClosedSetConverter<TSet>` names the converter for an
explicit configuration.

### `Option` and `Result` are not columns

EF can't map them, and building the model fails with an error that says so. Declare an optional value
as `T?` on the entity, and call `ToOption()` where your domain wants an `Option`.

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

`.Value`, `GetValue()` and `ValueOrNull()` translate to the column itself. So the underlying type's own
operations work on it: `StartsWith`, arithmetic, comparisons. Use `ValueOrNull()` for an optional
value object, where `.Value` would first unwrap the `Nullable`.

A table's column is re-typed, not cast, so an index on it still serves the query. These are converted
instead, which works everywhere and may add a cast:

- the element of a primitive collection (`o.Tags.Any(t => t.Value == "x")`);
- a property mapped into a JSON column;
- a parameter.

**Good to know**

- **`.Value` translates only where the column holds the underlying value.** That's where TypeKit's
  converter stores it: the one `UseTypeKit()` applies, or `ValueObjectConverter<,>` named in
  `HasConversion`. A property your application converts itself
  (`HasConversion(c => "C:" + c.Value, ...)`, a converter type, a conversion in
  `ConfigureConventions`) doesn't hold `.Value`. So a filter, a sort, a grouping or an aggregate that
  reads `.Value` there fails with EF's "could not be translated", rather than comparing against what
  the column really holds. Selecting `.Value` as the query's last step still works, `Distinct`
  included: EF reads the value object through your converter and takes `.Value` in memory. Comparing
  the value object itself (`o.Code == code`) goes through that converter and still works.
- **Ordering in SQL follows the database's collation** for the column. The generated comparison in
  memory is ordinal for a string, as it is for any string column.
- **A null passes no `<`, `<=`, `>` or `>=` in the database.** SQL leaves out a row whose column is
  NULL, as it does for an `int?` column. A value object that's a record class puts null first in
  memory, so there `null < rank` is true. For a nullable `Rank` record class, `o.Rank < rank` keeps
  an order without a rank in a list, and leaves it out of the query. Write
  `o.Rank == null || o.Rank < rank` to keep it in the query too. A struct value object answers the
  same both ways.
- **Your own `CompareTo(TSelf)` can't run in the database.** A value object that declares one orders
  in memory by that method. The column orders by the underlying value, which could be the opposite of
  a descending `CompareTo`. So these queries throw an `InvalidOperationException` naming the value
  object:
  - a comparison (`<`, `>`, `<=`, `>=`, `CompareTo`);
  - an ordering (`OrderBy`, `ThenBy`, their descending forms, `Order`);
  - the database's own orderings: `EF.Functions.Greatest` and `Least`, and a row-value comparison
    such as Npgsql's `EF.Functions.GreaterThan((o.Priority, o.Id), (priority, id))`;
  - `Min` or `Max`.

  Compare or order by `.Value` where the underlying value's order is what you mean
  (`OrderBy(o => o.Priority.Value)`), or order in memory after the query. Equality isn't ordering,
  and stays allowed. A query filter (`HasQueryFilter`, named or not) that does the same fails when
  the model is built, naming the entity type, since it applies to every query of that type.
- **A value object's arithmetic runs as the underlying type's.** The operators TypeKit generates
  ([Add, subtract and scale](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/value-objects.md#add-subtract-and-scale)) work in a query.
  `o.Price * 2m > limit` keeps the rows memory keeps, and `o.Tax / o.Price` reads a `decimal`. The
  database computes them, on SQLite, PostgreSQL and SQL Server alike.
- **Your own operator can't run in the database.** EF reads `o.Price + o.Tax` as the database's own
  addition of the two columns and never calls your `+`. Whatever your operator does beyond that, such
  as rounding, would be skipped. So a query that uses an arithmetic or bitwise operator you wrote on a
  value object throws an `InvalidOperationException` naming the value object. Compute it from `.Value`
  where the underlying type's arithmetic is what you mean (`o.Price.Value + o.Tax.Value`), or do it in
  memory after the query. A query filter that uses one fails when the model is built. An operator over
  values you pass in, which EF computes before the query, stays allowed.
- **Over a `byte`, `short` or `char`,** an operator throws as well, naming the value object. C#
  computes their arithmetic as an `int`'s, and EF has no expression for theirs.
- **Over a column your own converter stores,** an operator isn't translated, as `.Value` isn't: the
  column doesn't hold the underlying value, so EF's "could not be translated" is the answer. As the
  query's last step, a `Select` still works: EF computes it in memory.

### On SQL Server

A value object behaves on SQL Server as the type inside it does there. That isn't always how it
behaves in memory:

- **Text compares ignoring case.** Under SQL Server's default collation, `o.Note == note`,
  `notes.Contains(o.Note)` and `StartsWith` also match text that differs in case or in trailing
  spaces. `OrderBy` sorts ignoring case too. In memory, a value object over text equals only the same
  text, exactly. Where case matters, give the column a binary collation:
  `UseCollation("Latin1_General_BIN2")`.
- **Guids sort by their last group.** SQL Server orders a `uniqueidentifier` by its last six bytes
  first. So `OrderBy`, `<` and `Min` over Guids come out in another order than in memory.
- **A `decimal` without a precision is a `decimal(18,2)`.** It reads back with two decimal places:
  `10m` comes back as `10.00m`, which is equal but prints `10.00`. More decimal places are rounded
  when the value is written. Configure the precision you need, on the property with `HasPrecision`
  or for every such value object with `configurationBuilder.Properties<Amount>().HavePrecision(18, 4)`.
- **A `TimeSpan` must be shorter than a day.** It's stored as `time`. A longer one fails
  `SaveChanges` in a column. In a JSON column it's stored, but a query that reads it gets another
  value, `01:02:00` for `1.02:00:00`, without an error. Store a longer duration as a number instead,
  such as a value object over the seconds as a `long`.
- **`EF.Functions.Greatest` and `Least` need compatibility level 160.** Configure it with
  `UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(160))`. Below it, EF refuses them.
- **A `DateTime` comes back without its kind.** `datetime2` keeps every tick, and a UTC value reads
  back with `Kind` `Unspecified`. It's still equal to the value you wrote.

The mapping is tested on SQLite, PostgreSQL and SQL Server. The SQL of each translation is pinned on
PostgreSQL. Every family of underlying types makes a round trip through a real PostgreSQL and a real
SQL Server, and every query and key shape of the test catalogue runs on the real SQL Server too.

## An Option for nothing found

```csharp
Option<Order> order = await db.Orders.FirstOrNoneAsync(o => o.Id == id);   // the database runs the condition and returns one row
Option<Order> earliest = db.Orders.OrderBy(o => o.PlacedAt).FirstOrNone(); // the same, synchronous
Result<Order, OrderFault> found = db.Orders.FirstOrError(o => o.Id == id, OrderFault.NotFound);
```

These ask the database for one row and let it run the condition:

- **`FirstOrNoneAsync`**, and the base package's **`FirstOrNone`** and **`FirstOrError`** on a
  query, read one row: `WHERE ... LIMIT @p`. `FirstOrDefaultAsync` would hand back a `default` value
  object when nothing is found (CMTK0009).
- **`SingleOrNoneAsync`** and **`SingleOrNone`** ask for two rows, and throw for more than one.
- **`ElementAtOrNone`** skips to its row: `OFFSET @p`.

**Good to know**

- **The condition must be an expression.** A lambda written at the call is one. A
  `Func<Order, bool>` held in a variable, or a method group, binds `FirstOrNone`, `SingleOrNone` and
  `FirstOrError` to their overloads on a sequence. Then every row is read and the condition runs in
  memory, and the analyzer warns at the call
  ([CMTK0009](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#a-condition-that-reads-every-row)).
  Hold an `Expression<Func<Order, bool>>` instead. `FirstOrNoneAsync` takes only an expression, so a
  `Func` there doesn't compile.
- **`LastOrNone` and `LastOrError` on a query don't compile.** A query has no last row until it's
  ordered. So order it the other way and take the first, or call `AsEnumerable()` to read every row
  on purpose.
- **Under Native AOT these throw "Query wasn't precompiled"** (measured with EF Core 10.0.12). EF
  runs only the queries it precompiled, and it precompiles a query where your application calls one
  of EF's own operators, which these helpers aren't. Write
  `Where(...).Take(1).ToListAsync()` at the call site instead, and take the first element of the
  list.

## Reading back skips validation

```csharp
var orders = await db.Orders.AsNoTracking().ToListAsync();
foreach (var order in orders.Where(o => o.Code.Revalidate().State == ResultState.Error))
    log.StoredCodeNowRefused(order.Id);
```

EF reads a column back without validation, through `IValueObjectMaterializer<,>` rather than
`Create`. A rule you add to a value object later shouldn't make the rows written before it
unreadable. And the database holds what your application wrote, so it's trusted.

Input still goes through `Create`. Nothing in this package can be reached from a request, and calling
`Materialize` anywhere else gets reported by the analyzer (CMTK0004).

When you add a rule, `Revalidate()` finds the rows it rejects, as above.

## Compiled models and Native AOT

```bash
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

EF Core runs under Native AOT only through a compiled model and precompiled queries, and it supports
that experimentally. `UseTypeKit()` works there. In the native binary:

- **Value objects and closed sets map** through the compiled model, a JSON column included.
- **`.Value` translates** in precompiled queries.
- **A value-object or closed-set parameter binds.**
- **A stored value reads back** without the rules.
- **A key over an integer** is generated by the database.

The consumer smoke test runs exactly that. A compiled model without Native AOT works the same way.
This package adds no trim or AOT warning of its own. EF's `DbContext` constructors report theirs,
which EF's documentation covers.

Five EF limitations to know, measured with EF Core 10.0.12:

- **A struct value object that wraps a reference type** (`string`, `Uri`) can't be a property in a
  compiled model. EF can't write its sentinel, since `default(ProductCode)` converts to `null`, and
  `dbcontext optimize` fails with "The type mapping for 'ProductCode' has not implemented code
  literal generation". Declare such a value object as a `sealed partial record`.
- **A sealed entity class** doesn't compile in EF 10's precompiled queries (CS0030 in the generated
  interceptors).
- **Precompiled queries need the converter `UseTypeKit()` composes.** They cast a property's
  converter to the type it had at design time. The compiled model doesn't recreate that for a
  `ValueObjectConverter<,>` or a `ClosedSetConverter<>` named in `HasConversion`.
- **Precompiled queries compile the project they're in without the generators' bodies.**
  `--precompile-queries` reads the generated members as declarations only. So a value object or a
  closed set declared in that project fails it (CS0534, CS0177 in the generated code). Declare them
  in another project, as a domain library usually is.
- **A collection of converted elements can't be read by a precompiled query.** That's a
  `List<OrderId>` or a `List<OrderStatus>` as much as a `List<DayOfWeek>` stored as text by EF's own
  converter. Precompilation fails with
  ``Encountered a constant of unsupported type 'ValueConverter`2'``. An entity with any primitive
  collection, a `List<string>` too, fails to materialize in a precompiled query (a
  `BadImageFormatException` from an accessor EF generates). A query compiled at run time, without
  Native AOT, handles both.
