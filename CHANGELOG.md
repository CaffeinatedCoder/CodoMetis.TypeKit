# Changelog

All notable changes to CodoMetis.TypeKit and its satellite packages.

The packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a given package saw no change in it.
Versions follow [Semantic Versioning](https://semver.org/). Entries are newest first.

A version's heading reads `## <version> — Unreleased` until the release is prepared, when it gets
the release date. The release workflow refuses a tag whose section still says `Unreleased`, and the
section is the GitHub release's notes.

## 2.1.0 — Unreleased

### Added

- **Decimals in one spelling**: `DecimalNumberJsonConverter` and `DecimalStringJsonConverter` write the smallest scale, keys included, and read either token ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#decimals-in-one-spelling)).
- **Converters that say what they write**: `IDescribedJsonConverter`, `JsonSchemaDescriptions` in the options and `DescribedJsonSchema` for the schema exporter; `AddTypeKit()` describes them in every position of the document ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#describing-what-a-converter-writes)).
- **The rule that refused**: `Fault.Of(reason).WithRule(member)` names an enum's or a closed set's member, written as the problem's `rule` and on `IFaultFeature` ([The rule that refused](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#the-rule-that-refused)).
- **`FaultKind.PreconditionFailed`** (412) and **`FaultKind.Malformed`** (400), answered with no mapping ([closed sets as faults](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#a-closed-set-as-the-fault-type)).
- **CMTK0014** (Warning): a `FaultKind` or `ConstructorAccessibility` that names no member, such as `(FaultKind)9` or `default` ([CMTK0014](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0014)).
- **`AsOptionalText()`/`AsOptionalIdentifier()`**: a blank is no text, a hidden character still `Hidden` ([Written text](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/text.md#required-and-optional)).
- **Faults of several types**: `Fault<T1, T2>` and `Fault<T1, T2, T3>` hold a boundary's own fault or a shared one; `ToHttpResult()` answers each as its type is mapped, and a host whose endpoint answers two types writing one fault name does not start ([Faults of several types](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types)).
- **`WithoutFaults`** and `[WithoutFaults<T>]`: a route leaves out of its document the members it never answers with, and reporting one throws ([Leaving out what a route cannot answer](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#leaving-out-what-a-route-cannot-answer)).
- **CMTK1023** (Warning): a base record's `Equals`, `GetHashCode` or `IEquatable`, which the value object's equality no longer calls ([Build errors](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors)).
- **`[Interface]`, `[Unsealed]`, `[ValueObject]`, `[ClosedSet]`, `[FlagsEnum]`, `[ReadOnlyStruct]`** (CMTK0010): six more kinds a type parameter can demand of its argument, `[ValueObject]` and `[ClosedSet]` being the marker interfaces with any type argument ([Type constraints](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/type-constraints.md#the-kind-of-a-type-argument)).
- **`[Distinct]` and `[Unrelated]`** (CMTK0010): two type parameters whose arguments must be different types, or must not derive from, implement or box to each other; `Fault<T1, T2>` and `Fault<T1, T2, T3>` declare theirs `[Distinct]` ([Type constraints](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/type-constraints.md#two-parameters-apart)).
- **One mapping for every fault set**: `AddFaultResponses()` by kind, or `AddFaultResponses(fault => …)` over `IFault`, for each set with no mapping of its own ([Every fault set at once](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#every-fault-set-at-once)).
- **Testing: `ShouldHold(reason)`** on `Fault<T1, T2>` and `Fault<T1, T2, T3>` hands back the fault it holds ([CodoMetis.TypeKit.Testing](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Testing/README.md)).

### Changed

- The OpenAPI problem schema describes `rule` beside `fault`: regenerate clients.

### Fixed

- OpenAPI: a value object or a closed set the JSON options give a converter of their own is no longer described as its wrapped type or its names, which that converter does not write; it is what the converter says, or left as ASP.NET writes it ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: a `Patch<T>` the serializer requires, a constructor parameter under `RespectRequiredConstructorParameters`, stays `required`; declare patches as `init` properties there ([Merge patches](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/merge-patches.md#required-constructor-parameters)).
- Schema descriptions follow the converter a resolver of one's own puts in charge of a value object; they follow a converter factory to the converter it makes, on the options and on a property, and a converter registered for a `Nullable<T>` to the nullable positions; a conditional (`if`) description admits null in a branch beside it ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#describing-what-a-converter-writes)).
- OpenAPI: a type held inside a `Patch<T>` alone is compared between the minimal-API and MVC JSON options too; a converter registered for a nullable value object alone, whose positions refer to the shared component, refuses the document; a described keyword a 3.0 document writes aside (`if`, `contains`, `patternProperties`) or leaves out refuses it, naming the keyword ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).
- OpenAPI: an endpoint answering two fault types lists the problem types of both at a status they share, and its fault statuses in ascending order.
- Over a base record, a value object's equality is its wrapped value's, in a collection of the base too: a base's own equality, explicit `IEquatable` included, or a private field in another assembly made equal values unequal.
- Under `ReferenceHandler.Preserve`, a value object over a class, or a struct holding one, nullable or not, throws `NotSupportedException` instead of writing a duplicate `$id`; an ignored property is passed over.
- A wrapped type's `Parse` that returns another type, or takes the text by `ref`, and an abstract class's string constructor are no parsers: the value object builds without parsing.

## 2.0.0 — 2026-10-14

Still `net10.0` and Metalama 2026.1. What builds or runs differently, and what to write instead:
[Migrating from 1.5 to 2.0](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/migrating-to-2.md).

### Breaking

- CMTK0003, a dropped `Result` or `Option`, is an Error, and reports more forms (`Wait()`, loops that never read, `??`, tuples).
- `FirstOrNone`/`FirstOrError` on an `IQueryable` run in the query (a `Func` variable still reads every row); `LastOrNone`/`LastOrError` on a query no longer compile.
- CMTK1022 refuses an abstract value object; CMTK1005 refuses one over `object` or `System.Enum`, or named as a generated member.
- A generated `TryParse` lets an exception from `Create` through instead of answering `false`.
- EF Core refuses `.Value` on a column with the application's own converter, and ordering by a hand-written `CompareTo`, in queries and query filters.
- EF Core: value-object keys over `sbyte`, `ushort`, `uint` and `ulong` are generated as EF generates the plain type, so a migration may alter the column.
- OpenAPI: a component name claimed by two types throws.
- Async combinators throw `ArgumentNullException` for a delegate that returns no task.
- `FaultProblem<TFault>` takes any `notnull` fault type.

### Added

- **Closed sets** (`IClosedSet<TSelf>`): a fixed set of named instances with `Match`, `Parse`, JSON, EF Core, OpenAPI and Serilog support; CMTK1013 to CMTK1021 refuse what cannot be generated.
- **Fault responses a client can act on**: `Fault<TFault>`, fault sets with a kind per member, `FaultResponse`, `WithFaultStatus`/`[FaultStatus<T>]` per endpoint, group or controller, `ToHttpResult(success)` and `IFaultFeature`.
- **`Patch<T>`**: a JSON Merge Patch field (absent, null or a value; `T` is `notnull`, so `Patch<string>` is a clearable string), with `Map`, `Traverse`, OpenAPI and test assertions.
- **`Result.Catch`/`CatchAsync`**: an exception as a result; a cancellation passes through, a timeout is the error.
- **Text guards**: `AsText()` and `AsIdentifier()` trim, compose to NFC (`AsText`) and refuse invisible characters.
- **Type constraints**: `[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]` (CMTK0010) and `[RequireParameterlessConstructor]` (CMTK0011, with a code fix).
- **CMTK0012**: a closed set's `Match` called by position, with a code fix.
- **CMTK0013**: a closed set's member written outside its declaration, after it was named.
- **`Option`**: async steps and `Task<Option<T>>` continuations, `SingleOrNone`, `ElementAtOrNone`, `Choose`, `Transpose`, `Traverse`; EF Core's `FirstOrNoneAsync` and `SingleOrNoneAsync`.
- **`TraverseAsync`** on a sequence: one at a time, cancellable.
- **Docs**: topic pages under `docs/`, every code block compiled.

### Changed

- The OpenAPI problem schema describes `fault`: regenerate clients.
- CMTK0003 no longer reports a chain of taps on a result the caller holds.
- EF Core: a `char` key stays the application's to assign; a default `ulong` key throws `OverflowException` at `Add` (use `ValueGeneratedNever()`).
- Testing failures name a nested type with its containing types.
- `CodoMetis.TypeKit.Generators` depends on `Metalama.Framework` 2026.1.30 or later (was 2026.1.29).

### Fixed

- A value object marked `[Obsolete(error: true)]` builds.
- A default value object over a reference type formats as `""` instead of throwing.
- Where the host inlines enums, a nullable value object over one admits null in the OpenAPI document.

## 1.5.1 — 2026-10-11

### Fixed

- **`CodoMetis.TypeKit.AspNetCore`**: a value object bound as a route, query or header parameter is
  described as ASP.NET describes a parameter of the wrapped type. ASP.NET uses a type's own schema there
  only for a primitive, an enum, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`,
  `decimal`, `Guid` and `Uri`, and `string` for any other parsable type. `AddTypeKit()` used the wrapped
  type's JSON schema regardless, so a value object over a type with a JSON converter of its own, such as
  a CodoMetis.ValueRanges `DateRange`, was the empty schema `{}` as a parameter, where a `DateRange`
  parameter is `string`. Request and response bodies are unchanged.

## 1.5.0 — 2026-10-11

### Added

- **`CodoMetis.TypeKit.AspNetCore`**: a `Result` as an HTTP response. `result.ToHttpResult()` answers a
  success with `200 OK` and its value (`204 No Content` for a `Result<TError>`) and a fault with RFC 9457
  problem details, whose status the host maps per fault type with
  `services.AddFaultResponses<TFault>(fault => …)` and whose `fault` member names the fault. The fault
  type is an enum the domain declares; only the host references ASP.NET. The return type is ASP.NET's
  own `Results<Ok<T>, FaultProblem<TFault>>`, in minimal APIs and MVC, and `AddTypeKit()` lists a
  problem response for every mapped status in the OpenAPI document. A host with an endpoint whose
  fault type is not mapped does not start; a mapping that gives a member no error status, or throws
  for one, fails where it is registered. `AddFaultResponses` calls `AddProblemDetails()`, which a
  Native AOT host needs to write a problem.
- **`CodoMetis.TypeKit`**: `Recover` on both `Result` shapes, the error branch's `Bind`: the selector's
  result replaces an error, a success or another error, and a success passes untouched.
  `RecoverAsync` takes an asynchronous selector and continues a pending `Task<Result<…>>`, as the other
  combinators do.
- **`CodoMetis.TypeKit.Serilog`**, a new package: `Destructure.WithTypeKit()` logs every value object as
  what it prints, also where Serilog destructures it (`{@Key}`, or a record holding one). Without it,
  Serilog read the public `Value`, so a value object masked by a hand-written `ToString()` was logged
  unmasked there. Nothing is registered per type; Serilog 4.0.0 or later.
- **`CodoMetis.TypeKit.Generators`** README: "A value that must not be printed", the `ToString()` seam
  as the way to mask a value, what still carries it, and why a compliance classification belongs on
  the logging method's parameter, not on the type.
- **`CodoMetis.TypeKit.Testing`**, a new package for test projects: `ShouldBeSome()`, `ShouldBeNone()`,
  `ShouldBeSuccess()` and `ShouldBeError()` (with or without the expected error) on `Option`, both
  `Result` shapes. They hand back the value or the error for the test's own assertions, and otherwise
  throw a `TypeKitAssertionException` that names the error found and never prints a value. An
  uninitialized result fails every assertion as such. No test framework dependency.

### Changed

- **`CodoMetis.TypeKit.Generators`** depends on `Metalama.Framework` 2026.1.29 or later (was 2026.1.28),
  the latest of the 2026.1 line that MIT maintenance covers.

### Fixed

- **`CodoMetis.TypeKit.AspNetCore`** README: a minimal API answers a body a value object refuses with
  an empty `400`, with `AddProblemDetails()` too; it said that call gives the response a body, which
  takes `UseStatusCodePages()` as well.

## 1.0.0 — 2026-09-28

The first release.

### Added

- **`CodoMetis.TypeKit`**: `Option<T>`, `Result<T, TError>` and `Result<TError>`, with no public
  `.Value` or `.Error`, and the `Option.None()`, `Result.Success(...)` and `Result.Error(...)`
  markers that convert to whichever one a method returns. A `default` `Option` is `None`; a
  `default` `Result` is uninitialized, and every member that would pick a branch throws on it.
  `Result<TError>` converts to `bool` for a success; `Result<T, TError>` does not, since its value
  could itself be a `bool`.
  `ToString()` never prints the content, and none of them serializes: System.Text.Json refuses them
  with `NotSupportedException` in both directions instead of writing `{}`, a nullable one too, and a
  converter registered on the options takes precedence. `OrNull()` converts an `Option` of a value
  type or of a reference type to a nullable at the boundary.
  - Every deliberate refusal, of the base package and of generated code, says what to do instead and
    ends with a link to its explanation under "Why does this throw?" in the package README; the
    generators' build errors link to the README's Build errors table.
  - Combinators: `Map`, `Bind`, `MapError`, `Tap`, `TapError`, `Ensure` (a rule checked inside a
    pipeline), `Match`, `TryGetValue`, and query syntax with any number of `from` clauses, for
    `Option` and `Result` alike.
  - Pipelines: `MapAsync`/`BindAsync`, and an `…Async` continuation of `Map`, `Bind`, `MapError`,
    `Tap`, `TapError` and `Ensure` on a `Task<Result<…>>`, so a chain is awaited once; `Zip` for two
    to six results; `Sequence` and `Traverse` over a sequence. All stop at the first error. A
    callback that returns a `Task` is awaited, in `TapAsync` and `TapErrorAsync` alike.
  - Lookups that return an `Option`: `GetValueOrNone(key)` on a dictionary, and `FirstOrNone()` and
    `LastOrNone()` with or without a predicate.
  - The value-object contracts (`IValue<T>`, `IValidatedValue<TSelf, T, TFault>`,
    `IValueObject<TSelf, T>`), `OrderId.New()` for version 7 Guid identifiers, and
    `StoredJsonConverterFactory` for JSON the application stored itself.
- **`CodoMetis.TypeKit.Analyzers`**, arriving with the base package, in C# files and Razor
  components alike; where Metalama compiles a project, on the code it transformed. Each rule links to
  its section of the analyzer README:
  - Errors: CMTK0001 (no `default` of a value object, an `Option`, a `Result` or a
    `[RequireCustomInitialization]` struct; a `default` only compared (`==`, `Equals`, `ThrowIfEqual`,
    an assertion such as `Assert.NotEqual` or `ShouldNotBe`) or assigned to the `out` parameter of a
    `bool` Try method is allowed), CMTK0002 (a value object in a project that does not reference the
    generators), CMTK0004 (`Materialize`, which skips validation, called outside the EF Core
    satellite).
  - Warnings: CMTK0003 (an ignored `Result` or `Option`, or a collection of them the statement made,
    such as `await Task.WhenAll(ids.Select(orders.CancelAsync))`; a `Task` of one converted to a plain
    `Task`, a method group's included), CMTK0005 (an array or span of such a struct created with a
    length), CMTK0006 (a member of such a type that nothing assigns), CMTK0008 (the values of two
    different value objects compared, through `==`, `Equals`, `string.Equals`, a comparer or a LINQ
    join, `join … on a.Value equals b.Value` and `Join`/`GroupJoin`/`LeftJoin`/`RightJoin`, or two
    value objects of different types compared through `Equals`), CMTK0009 (a call that hands out a
    `default` instance when it finds nothing: `FirstOrDefault`, `Find`, `GetValueOrDefault`,
    `Option.OrDefault()` and their kind, on immutable collections too, and `FirstOrDefaultAsync` and
    its kind on `IAsyncEnumerable` and in EF Core).
  - Info: CMTK0007 (`FromKnownGood` given a parameter).
  - The analyzer loads in every .NET 10 SDK: it compiles against Roslyn 5.0.0, the compiler of the
    10.0.1xx band.
- **`CodoMetis.TypeKit.Generators`**: a one-line value-object declaration gets its constructor,
  `Value`, `From`, `TryFrom` and `FromKnownGood` (both derived from the hand-written `Create`),
  `Revalidate()` for values read back without validation, a JSON converter,
  `IParsable`/`ISpanParsable`/`IUtf8SpanParsable`, formatting in the invariant culture, comparison,
  `IConvertible`, a `TypeConverter` and `GetValue()`/`ValueOrNull()` extensions.
  - Every generated way into a validated value object applies `Create`.
  - The JSON is the wrapped type's, byte for byte, under the application's options, as a value and as
    a dictionary key; formatting, comparison and JSON cost about what the wrapped type's own do.
  - A refusal names the value object and the fault, or the wrapped type it could not read, and never
    quotes the input.
  - Seams: a hand-written `TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)` or `ToString()`
    is kept, and what depends on it derived from it; a base record's `sealed` `ToString()` counts.
  - A value object holds its wrapped value alone, and its equality is that value's. A declaration
    that cannot be generated, or that would be generated wrongly (state beside the wrapped value, a
    hand-written equality, an explicit implementation of an interface the generators implement), is a
    build error naming it (CMTK1000–CMTK1012).
  - Metalama 2026.1, and no Metalama license is needed to build.
- **`CodoMetis.TypeKit.EntityFrameworkCore`**: `UseTypeKit()` maps every value object to a column
  of the type it wraps, with nothing registered per type, including keys, foreign keys, nullable
  properties and primitive collections, and translates `.Value`, `GetValue()` and `ValueOrNull()`
  in queries to the bare column, collection elements included. A key over an integer is generated by
  the database, as a key of that integer type is; on SQLite, with `UseAutoincrement()` (see the
  README). EF Core 10.0.12 or later, with a relational provider.
- **`CodoMetis.TypeKit.AspNetCore`**: `AddTypeKit()` on `AddOpenApi` gives every value object the
  schema ASP.NET publishes for the type it wraps, wherever it appears: properties, bodies,
  containers, and route, query and header parameters, a nullable one admitting null there as the
  wrapped type does, whether it is a component or inlined. A nested value object's component is named
  after its nesting chain. Microsoft.AspNetCore.OpenApi 10.0.12 or later.
- `UseTypeKit()`, `AddEntityFrameworkTypeKit()` and `AddTypeKit()` live in EF Core's and
  dependency injection's namespaces, so a host adds them without a `using`.
- **Native AOT**, for every package that runs in an application. The run-time packages are built with
  the trim and AOT analyzers on, and the generated code is published with Native AOT and run in the
  consumer smoke test. With source-generated JSON a context lists the value objects, not what they
  wrap. The EF Core satellite works with a compiled model and precompiled queries, which is how EF
  Core runs under Native AOT. The OpenAPI satellite needs the wrapped types in the host's
  `JsonSerializerContext`, and says which one is missing.
