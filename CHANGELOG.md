# Changelog

All notable changes to CodoMetis.TypeKit and its satellite packages.

The five packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a given package saw no change in it.
Versions follow [Semantic Versioning](https://semver.org/). Entries are newest first.

A version's heading reads `## <version> — Unreleased` until the release is prepared, when it gets
the release date. The release workflow refuses a tag whose section still says `Unreleased`, and the
section is the GitHub release's notes.

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
  with `NotSupportedException` in both directions instead of writing `{}`, and a converter registered
  on the options takes precedence.
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
