# Migrating from 1.5 to 2.0

This page walks you through moving a project from 1.5 to 2.0. It lists what builds or runs
differently for code that built against 1.5, and what to write instead. The
[changelog](../CHANGELOG.md) lists everything 2.0 adds.

Your platform stays the same. 2.0 targets `net10.0` and Metalama 2026.1, as 1.5 does, so no runtime,
SDK or Metalama version moves with it. The examples import `CodoMetis.TypeKit`.

## Update the packages

All the packages share one version and move together. Raise every TypeKit package you reference,
the EF Core and ASP.NET Core packages included.

```xml
<PackageVersion Include="CodoMetis.TypeKit" Version="2.0.0" />
<PackageVersion Include="CodoMetis.TypeKit.Generators" Version="2.0.0" />
<PackageVersion Include="CodoMetis.TypeKit.EntityFrameworkCore" Version="2.0.0" />
```

## Handle every dropped Result

When a call returns a `Result` or an `Option` and nothing reads it, the analyzer reports CMTK0003. In
1.x that was a warning. In 2.0 it fails the build.

It also catches cases 1.x missed:

- **`Wait()` on a task of a result**, also with a timeout, even where the `bool` that form returns is
  read.
- **Tasks of results gathered into an array or a sequence** and passed to `Task.WaitAll`.
- **A `foreach` or `await foreach`** that never reads the results it walks.
- **A lazy sequence of results** turned into a list and dropped.
- **A task picked by `?:`, a `switch` or `??`** and awaited, with its result never read.
- **A tuple or a dictionary holding results**, dropped.

1.x already reported tasks passed to `Task.WaitAll` or `Task.WaitAny` as separate arguments. If your
build treated warnings as errors, every 1.x case already failed it, and only the new cases are new
errors.

```csharp
await orders.CancelAsync(id);                                 // CMTK0003: an error since 2.0
orders.CancelAsync(id).Wait();                                // CMTK0003: new in 2.0, waited for and forgotten

_ = await orders.CancelAsync(id);                             // the drop, said in the code
await foreach (var _ in orders.CancelEachAsync(ids)) { }      // a loop variable named _ says it too
```

You can answer every report in the code, without a suppression:

- **`_ =`** for a drop nothing depends on.
- **A cast to `Task`** for a task you convert on purpose. `((Task)orders.CancelAsync(id)).Wait(timeout)`
  waits without the result.
- **A loop variable named `_`.**

To migrate a large codebase in steps, lower the rule for a while:

```ini
[*.cs]
dotnet_diagnostic.CMTK0003.severity = warning
```

**Good to know**

- **Chained taps are no longer reported.** 1.x reported a chain of taps on a result or a task you
  hold, such as `result.Tap(log).TapError(warn);`. 2.0 doesn't, because each tap returns its receiver.
- **CMTK0010 to CMTK0013 are new errors, but only for new features.** They report only on what 2.0
  adds: the type-parameter attributes, `[RequireParameterlessConstructor]` and closed sets. No code
  that built against 1.5 runs into them.

## Fix value objects that no longer build

### An abstract value object

In 1.5, an abstract type could declare a value-object marker and still build. It generated nothing
of its own, its sealed derived types were generated, and a value held as the abstract type was
written to JSON as `{}`.

In 2.0 that declaration fails the build (CMTK1022). Declare the marker on the sealed type instead, and
keep the abstract base for what the types share:

```csharp
// public abstract partial record Money : IValue<decimal>;     1.5 built these; 2.0: CMTK1022
// public sealed partial record Euro : Money;

public abstract record Money;
public sealed partial record Euro : Money, IValue<decimal>;    // the marker on the sealed type, which is generated
```

### Two more declarations that fail with CMTK1005

- **A value object over `object`.** 1.5 wrote it to JSON as `{}`. Wrap the type the value actually
  has.
- **A value object named like a member the generators add to it.** 1.5 mostly failed on these inside
  the generated code, but built them where that member wasn't generated: one named `Revalidate` that
  isn't validated, or `CompareTo` over a type with no order. Rename it.

## Expect exceptions from Create to reach TryParse callers

This applies when the underlying type is parsed by a type converter, a static `Parse` or a string
constructor. There, a generated `TryParse` used to turn an exception thrown by your `Create` into
`false`.

In 2.0 the exception propagates. That matches what `Parse` does, and what `TryParse` does over an
underlying type with a `TryParse` of its own. For input you mean to reject, return an error from
`Create` instead of throwing.

## Fix queries and documents that now throw

Three things ran in 1.5, gave wrong answers, and throw in 2.0:

- **`.Value` on a column with your own converter.** `.Value`, `GetValue()` and `ValueOrNull()`
  queried the representation TypeKit's converter would have stored, not the one your `HasConversion`
  stores, so they returned the wrong rows. EF now rejects the query. Map the property with TypeKit's
  converter, or compare the value object itself.
- **Ordering by a hand-written `CompareTo`.** Take a value object that declares its own `CompareTo`.
  A query that compares it, orders by it, or takes its `Min` or `Max` was ordered by the column. For a
  reversed `CompareTo`, that is the opposite of the in-memory order. Such a query is rejected now, and
  a query filter that does it is rejected when the model is built. Order by `.Value` where the
  column's order is what you mean ([EF Core](ef-core.md#queries)).
- **A component name two types claim.** A nested value object or closed set gets a component name
  from its nesting chain, so `Order.Status` becomes `OrderStatus`. When that name was also another
  type's, the document described both as one. Now it throws and names both. Rename one, or give one
  its own name in `CreateSchemaReferenceId`
  ([the ASP.NET Core README](../src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).

## Rebuild libraries and compiled models

Rebuild every project that references `CodoMetis.TypeKit.Generators`, and regenerate any compiled
model (`dotnet ef dbcontext optimize`).

- **Libraries.** A library whose value objects were generated by 1.5 lacks what 2.0 reads at run
  time. One example is the mark that the rejection of ordering by a hand-written `CompareTo` (above)
  looks for. Until you rebuild the library against 2.0, such queries over its value objects aren't
  rejected.
- **Compiled models.** A compiled model generated with 1.5 keeps 1.5's decisions, among them the key
  generation [below](#check-keys-over-small-integer-types).

## Update tests that check messages

A test that asserts an exception or a message will see these changes:

- **Asynchronous combinators check their delegate.** Given a delegate that returns no task, they
  throw an `ArgumentNullException` naming the delegate. Before, the `await` threw a
  `NullReferenceException`.
- **Nested types are named after their containing types.** A `CodoMetis.TypeKit.Testing` failure and
  a fault mapping's rejection now write `Result<AssertionTests.Fault>` and `Api.OrderFault`, where 1.5
  gave the type's name alone.
- **CMTK0009 suggests more.** Its message names `SingleOrNone` and `ElementAtOrNone` beside the other
  replacements.

## Check keys over small integer types

A value-object key over `sbyte`, `ushort`, `uint` or `ulong` is now marked as generated on add, just
as EF marks a plain key of that type. So a migration created against your 1.5 snapshot alters the
key column.

- **On PostgreSQL**, the migration adds an identity to a key over `sbyte`, `ushort` or `uint`, which
  applies cleanly.
- **On SQL Server**, the migrations generator rejects the change for all four types ("To change the
  IDENTITY property of a column, the column needs to be dropped and recreated").
- **A key over `ulong`** isn't generated on PostgreSQL, because `numeric(20,0)` has no identity.
  From 2.0.0 to 2.2.0, adding an entity with a default `ulong` key threw `OverflowException` at
  `Add`, on PostgreSQL and SQL Server alike, where 1.5 stored 0. From 2.3.0 it gets a temporary value,
  as a plain `ulong` key does, except in a compiled model made with `--nativeaot`.

To keep 1.5's behaviour for any of these keys, configure `ValueGeneratedNever()` on it and assign the
key yourself.

A key over `char` is unchanged. Your code assigns it, and a default `'\0'` is stored as it is
([EF Core](ef-core.md#keys)).

## Let FirstOrNone run in the database

In 1.5, `FirstOrNone(predicate)` and `FirstOrError(predicate, error)` on an EF Core query bound to the
`IEnumerable<T>` overloads. Every row was read, and the condition ran in memory.

In 2.0 they bind to `IQueryable<T>` overloads that hand the condition to the provider. You get the
same answer, and only one row is read.

```csharp
Option<Order> large = orders.FirstOrNone(o => o.Total > 100);                    // 2.0: WHERE and LIMIT 1 in the query; 1.5 read every row
Option<Order> local = orders.AsEnumerable().FirstOrNone(o => o.IsLarge());       // a condition the database cannot run, in memory as before
Option<Order> latest = orders.OrderByDescending(o => o.Placed).FirstOrNone();    // where 1.5 took LastOrNone(), now a compile error on a query
```

**Good to know**

- **An untranslatable condition now throws.** Where the provider can't translate the condition, the
  query throws instead of running in memory. Call `AsEnumerable()` first to run it in memory on
  purpose.
- **`LastOrNone` and `LastOrError` on a query don't compile.** The error message says what to write
  instead.
- **Only a lambda written at the call runs in the query.** A `Func<T, bool>` variable or a method
  group still binds to the sequence's overload and reads every row, as in 1.5. Hold an
  `Expression<Func<T, bool>>` instead.
- **Under Native AOT these helpers throw.** EF runs only the queries it precompiled at your own call
  sites, and these helpers compose the query inside the package.
  [Native AOT](native-aot.md#ef-core) lists the operators to write at the call site instead.

## Remove your own copies of new members

2.0 adds members to types 1.5 already had. If you wrote an extension with the same name and
parameters, and import it beside the package's, a call becomes ambiguous (CS0121). Delete yours.

Where the new member is an instance member, nothing is ambiguous, and your extension silently stops
being called. That happens to a `MapAsync`, `BindAsync` or `TapAsync` of yours on `Option<T>`.

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

2.0 also adds types. If a type of yours has one of these names and you import it beside the
package's namespace, a use becomes ambiguous (CS0104). Qualify one of them, or alias yours.

- **In `CodoMetis.TypeKit`:** `Patch<T>` and `Patch`, `PatchState`, `PatchJsonConverterFactory`,
  `SequenceOptionExtensions`, `Fault<TFault>` and `Fault`, `TextFault`, `StringTextExtensions`,
  `AbstractAttribute`, `SealedAttribute`, `RecordAttribute`, `ConcreteAttribute`,
  `RequireParameterlessConstructorAttribute` and `ConstructorAccessibility`.
- **In `CodoMetis.TypeKit.ClosedSets`:** `IClosedSet<TSelf>`, `IFaultSet<TSelf>`, `IFault` and
  `FaultKind`.

## Regenerate clients for fault responses

Your code needs no change. `AddFaultResponses<TFault>(fault => ...)` maps an enum as it did, and
answers with the same response.

The OpenAPI document changes, though. A host with fault responses now describes `fault`, an optional
string, on the shared `ProblemDetails` component. A client generated from the document gains an
optional `fault` member on its problem type. ASP.NET's own problem responses never set it.
Regenerate your clients, and update any checked-in snapshot of the document.

**Good to know**

- **`FaultProblem<TFault>` accepts more.** It takes any non-null error type where it took
  `struct, Enum`, so a closed set can be one. Every type argument it accepted before still works.
- **Answer each error by its kind.** To skip writing a switch, make the error type a closed set:
  [Closed sets](closed-sets.md#use-one-as-an-error-type).

## What is new

[Closed sets](closed-sets.md), [merge patches](merge-patches.md),
[an exception as an outcome](result.md#an-exception-as-an-outcome), [written text](text.md),
[type constraints](type-constraints.md), and [fault responses](aspnet-core.md#a-result-as-a-response)
a client can act on, each with its own page.
