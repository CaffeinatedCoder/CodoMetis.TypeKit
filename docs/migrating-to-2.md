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
error in 2.0. It also reports what 1.x missed: `Wait()` on a task of a result, tasks of results
passed to `Task.WaitAll` or `Task.WaitAny`, a `foreach` or `await foreach` that never reads the
results it walks, and a lazy sequence of results turned into a list and dropped. A build that
treated warnings as errors already met every 1.x form, so the new ones are what to look for.

```csharp
await orders.CancelAsync(id);                                 // CMTK0003: an error since 2.0
orders.CancelAsync(id).Wait();                                // CMTK0003: new in 2.0, waited for and forgotten

_ = await orders.CancelAsync(id);                             // the drop, said in the code
await foreach (var _ in orders.CancelEachAsync(ids)) { }      // a loop variable named _ says it too
```

Every report is answered in the code and never needs a suppression: `_ =` for a drop nothing
depends on, a cast to `Task` for a task converted on purpose, a loop variable named `_`. To migrate
a large codebase in steps, lower the rule for a while:

```ini
[*.cs]
dotnet_diagnostic.CMTK0003.severity = warning
```

CMTK0010, CMTK0011 and CMTK0012 are new errors too, but each reports only on what 2.0 adds: the
type-parameter attributes, `[RequireParameterlessConstructor]` and a closed set's `Match`. No code
that built against 1.5 meets them.

## A query's FirstOrNone runs in the database

In 1.5, `FirstOrNone(predicate)` and `FirstOrError(predicate, error)` on an EF Core query bound to
the `IEnumerable<T>` overloads: every row was read and the condition ran in memory. In 2.0 they bind
to `IQueryable<T>` overloads that hand the condition to the provider, so the answer is the same and
one row is read. A condition the provider cannot translate now throws where it used to run in
memory; say so with `AsEnumerable()`. `LastOrNone` and `LastOrError` on a query are compile errors
whose message says what to write instead.

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
| a sequence | `SingleOrNone`, `ElementAtOrNone` |
| a query | `FirstOrNone`, `FirstOrError`, `SingleOrNone`, `ElementAtOrNone` |
| an EF Core query | `FirstOrNoneAsync`, `SingleOrNoneAsync`, in EF Core's namespace |
| `string` | `AsText`, `AsIdentifier` |
| `Result` | `Catch`, `CatchAsync` |

## Fault responses

Nothing to change. `AddFaultResponses<TFault>(fault => …)` maps an enum as it did, and answers with
the same response. `FaultProblem<TFault>` takes any non-null fault type where it took `struct, Enum`,
so that a closed set can be one, and every type argument it accepted still is. To answer each fault
by its kind instead of by a switch, make the fault type a closed set:
[Closed sets](closed-sets.md#as-a-fault-type).

## What is new

[Closed sets](closed-sets.md), [merge patches](merge-patches.md),
[an exception as an outcome](result.md#an-exception-as-an-outcome), [written text](text.md),
[type constraints](type-constraints.md), and [fault responses](aspnet-core.md#a-result-as-a-response)
a client can act on, each with its page.
