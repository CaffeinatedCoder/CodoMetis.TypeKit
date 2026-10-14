# Migrating from 1.5 to 2.0

2.0 targets `net10.0` and Metalama 2026.1, as 1.5 does: no runtime, SDK or Metalama version moves
with it. This page lists what builds or runs differently for code that built against 1.5, and what
to write instead; the [changelog](../CHANGELOG.md) lists everything 2.0 adds. The examples import
`CodoMetis.TypeKit`.

## Update the packages

The packages share one version and move together: raise every one you reference, satellites
included.

```xml
<PackageVersion Include="CodoMetis.TypeKit" Version="2.0.0" />
<PackageVersion Include="CodoMetis.TypeKit.Generators" Version="2.0.0" />
<PackageVersion Include="CodoMetis.TypeKit.EntityFrameworkCore" Version="2.0.0" />
```

## A dropped Result is a build error

CMTK0003, a `Result` or `Option` a call returns and nobody reads, was a warning in 1.x and is an
error in 2.0. It also reports what 1.x missed:

- `Wait()` on a task of a result, with a timeout too, even where the `bool` that form answers is read;
- tasks of results gathered into an array or a sequence and passed to `Task.WaitAll`;
- a `foreach` or `await foreach` that never reads the results it walks;
- a lazy sequence of results turned into a list and dropped;
- a task picked by `?:`, a `switch` or `??` and awaited, its result never read;
- a tuple or a dictionary holding results, dropped.

Tasks passed to `Task.WaitAll` or `Task.WaitAny` as separate arguments were reported by 1.x already.
A build that treated warnings as errors already failed on every 1.x form; only the new forms are new
errors.

```csharp
await orders.CancelAsync(id);                                 // CMTK0003: an error since 2.0
orders.CancelAsync(id).Wait();                                // CMTK0003: new in 2.0, waited for and forgotten

_ = await orders.CancelAsync(id);                             // the drop, said in the code
await foreach (var _ in orders.CancelEachAsync(ids)) { }      // a loop variable named _ says it too
```

A chain of taps on a result or a task you hold, `result.Tap(log).TapError(warn);`, was reported by
1.x and no longer is, since each tap returns its receiver.

Every report is answered in the code and never needs a suppression: `_ =` for a drop nothing
depends on, a cast to `Task` for a task converted on purpose (`((Task)orders.CancelAsync(id)).Wait(timeout)`
waits without the result), a loop variable named `_`. To migrate
a large codebase in steps, lower the rule for a while:

```ini
[*.cs]
dotnet_diagnostic.CMTK0003.severity = warning
```

CMTK0010 to CMTK0013 are new errors too, but each reports only on what 2.0 adds: the
type-parameter attributes, `[RequireParameterlessConstructor]` and closed sets. No code
that built against 1.5 meets them.

## Value objects that are build errors now

In 1.5, an abstract type that declared a value-object marker built: it generated nothing of its own,
its sealed derived types were generated, and a value held as the abstract type was written to JSON as
`{}`. In 2.0 the declaration is CMTK1022. Declare the marker on the sealed type, and keep the abstract
base for what the types share:

```csharp
// public abstract partial record Money : IValue<decimal>;     1.5 built these; 2.0: CMTK1022
// public sealed partial record Euro : Money;

public abstract record Money;
public sealed partial record Euro : Money, IValue<decimal>;    // the marker on the sealed type, which is generated
```

CMTK1005 refuses two more declarations 1.5 built. A value object over `object` was written to JSON as
`{}`: wrap the type the value has. A value object named as a member the generators add to it, which
1.5 mostly failed on inside the generated code, built where the member was not generated: one named
`Revalidate` that is not validated, or `CompareTo` over a type with no order. Rename it.

## TryParse lets an exception from Create through

Over a wrapped type parsed by a type converter, a static `Parse` or a string constructor, a
generated `TryParse` turned an exception thrown by your `Create` into `false`. In 2.0 it propagates,
as it does from `Parse` and from `TryParse` over a wrapped type with a `TryParse` of its own. Return a
fault from `Create` for input you mean to refuse.

## Queries and documents that ran wrongly now throw

Three things that ran in 1.5 and answered wrongly throw in 2.0:

- **`.Value` on a column with a converter of the application's own.** `.Value`, `GetValue()` and
  `ValueOrNull()` queried the representation TypeKit's converter would have stored, not the one the
  application's `HasConversion` stores, and answered wrong rows; EF now refuses the query. Map the
  property with TypeKit's converter, or compare the value object itself.
- **Ordering by a hand-written `CompareTo`.** A query that compares, orders, or takes `Min` or `Max` of
  a value object that declares its own `CompareTo` was ordered by the column, the opposite of the
  in-memory order for a reversed one; it is refused now, and a query filter that does so is refused
  when the model is built. Order by `.Value`
  where the column's order is meant ([EF Core](ef-core.md#queries)).
- **A component name two types claim.** A document in which a nested value object's or closed set's
  component name, `Order.Status` as `OrderStatus`, is also another type's described both as one; it
  throws now, naming both. Rename one, or give one its own name in `CreateSchemaReferenceId`
  ([the ASP.NET Core README](../src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).

## Rebuild libraries and compiled models

A library whose value objects 1.5's generators wove lacks what 2.0 reads at run time, such as the
mark the hand-written-ordering refusal looks for, so the refusal does not reach its value objects
until the library is rebuilt against 2.0. A compiled model generated with 1.5 keeps 1.5's decisions,
the key generation below among them. Rebuild every project that references
`CodoMetis.TypeKit.Generators`, and regenerate a compiled model (`dotnet ef dbcontext optimize`).

## Messages that changed

A test that asserts an exception or a message meets these:

- An asynchronous combinator given a delegate that returns no task throws an `ArgumentNullException`
  naming the delegate, where the `await` threw a `NullReferenceException`.
- A `CodoMetis.TypeKit.Testing` failure, and a fault mapping's refusal, name a nested type after its
  containing types (`Result<AssertionTests.Fault>`, `Api.OrderFault`), where 1.5 gave the type's name
  alone.
- CMTK0009's message names `SingleOrNone` and `ElementAtOrNone` beside the other replacements.

## Keys over small integer types

A value-object key over `sbyte`, `ushort`, `uint` or `ulong` is now declared generated on add, as EF
declares a plain key of that type, so a migration created against your 1.5 snapshot alters the key
column. On PostgreSQL it adds an identity to a key over `sbyte`, `ushort` or `uint`, which applies
cleanly; on SQL Server the migrations generator refuses the change for all four ("To change the
IDENTITY property of a column, the column needs to be dropped and recreated"). A key over `ulong` is
not generated on PostgreSQL, since `numeric(20,0)` has no identity, and adding an entity with a
default `ulong` key throws `OverflowException` at `Add` on PostgreSQL and SQL Server, where 1.5
stored 0. To keep 1.5's behaviour for any of these keys, configure `ValueGeneratedNever()` on it and
assign the key. A key over `char` is unchanged: the application's to assign, a default `'\0'` stored
as it is ([EF Core](ef-core.md#keys)).

## A query's FirstOrNone runs in the database

In 1.5, `FirstOrNone(predicate)` and `FirstOrError(predicate, error)` on an EF Core query bound to
the `IEnumerable<T>` overloads: every row was read and the condition ran in memory. In 2.0 they bind
to `IQueryable<T>` overloads that hand the condition to the provider, so the answer is the same and
one row is read. A condition the provider cannot translate now throws where it used to run in
memory; say so with `AsEnumerable()`. `LastOrNone` and `LastOrError` on a query are compile errors
whose message says what to write instead. A lambda written at the call is an expression and runs in
the query; a `Func<T, bool>` variable or a method group still binds to the sequence's overload and
reads every row, as in 1.5, so hold an `Expression<Func<T, bool>>` instead.

```csharp
Option<Order> large = orders.FirstOrNone(o => o.Total > 100);                    // 2.0: WHERE and LIMIT 1 in the query; 1.5 read every row
Option<Order> local = orders.AsEnumerable().FirstOrNone(o => o.IsLarge());       // a condition the database cannot run, in memory as before
Option<Order> latest = orders.OrderByDescending(o => o.Placed).FirstOrNone();    // where 1.5 took LastOrNone(), now a compile error on a query
```

Under Native AOT, EF runs only the queries it precompiled at the application's own call sites, and
these helpers compose the query inside the package: they throw there, as
[Native AOT](native-aot.md#ef-core) describes, with the operators to write at the call site instead.

## Names 2.0 adds

2.0 adds members to types 1.5 already had. An extension of your own with the same name and
parameters, imported beside the package's, makes a call ambiguous (CS0121); delete yours. Where the
new member is an instance member, nothing is ambiguous and yours is silently no longer called: a
`MapAsync`, `BindAsync` or `TapAsync` of yours on `Option<T>`.

| On | Added |
|---|---|
| `Option<T>` | `MapAsync`, `BindAsync`, `TapAsync` (instance members), `Traverse` |
| `Task<Option<T>>` | `MapAsync`, `BindAsync`, `FilterAsync`, `TapAsync`, `ToResultAsync` |
| `Option<Result<T, TError>>`, `Result<Option<T>, TError>` | `Transpose` |
| a sequence | `SingleOrNone`, `ElementAtOrNone`, `Choose`, `TraverseAsync` |
| a sequence of options | `Choose` |
| `Patch<T>` | `Map`, `Traverse` |
| a query | `FirstOrNone`, `FirstOrError`, `SingleOrNone`, `ElementAtOrNone` |
| an EF Core query | `FirstOrNoneAsync`, `SingleOrNoneAsync`, in EF Core's namespace |
| `string` | `AsText`, `AsIdentifier` |
| `Result` | `Catch`, `CatchAsync` |

2.0 also adds types to the `CodoMetis.TypeKit` namespace: `Patch<T>` and `Patch`, `PatchState`,
`PatchJsonConverterFactory`, `SequenceOptionExtensions`, `Fault<TFault>` and `Fault`, `TextFault`, `StringTextExtensions`,
`AbstractAttribute`, `SealedAttribute`, `RecordAttribute`, `ConcreteAttribute`,
`RequireParameterlessConstructorAttribute` and `ConstructorAccessibility`; and `IClosedSet<TSelf>`,
`IFaultSet<TSelf>`, `IFault` and `FaultKind` to `CodoMetis.TypeKit.ClosedSets`. A type of yours with one of these names, imported beside the
package's namespace, makes a use ambiguous (CS0104): qualify one of them, or alias yours.

## Fault responses

Nothing to change in the code. `AddFaultResponses<TFault>(fault => …)` maps an enum as it did, and
answers with the same response. The OpenAPI document of a host with fault responses describes
`fault`, an optional string, on the shared `ProblemDetails` component, so a client generated from it
gains an optional `fault` member on its problem type; ASP.NET's own problem responses never set it.
Regenerate clients, and update a checked-in snapshot of the document. `FaultProblem<TFault>` takes any non-null fault type where it took `struct, Enum`,
so that a closed set can be one, and every type argument it accepted still is. To answer each fault
by its kind instead of by a switch, make the fault type a closed set:
[Closed sets](closed-sets.md#as-a-fault-type).

## What is new

[Closed sets](closed-sets.md), [merge patches](merge-patches.md),
[an exception as an outcome](result.md#an-exception-as-an-outcome), [written text](text.md),
[type constraints](type-constraints.md), and [fault responses](aspnet-core.md#a-result-as-a-response)
a client can act on, each with its page.
