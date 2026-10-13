# Changelog

All notable changes to CodoMetis.TypeKit and its satellite packages.

The packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a given package saw no change in it.
Versions follow [Semantic Versioning](https://semver.org/). Entries are newest first.

A version's heading reads `## <version> — Unreleased` until the release is prepared, when it gets
the release date. The release workflow refuses a tag whose section still says `Unreleased`, and the
section is the GitHub release's notes.

## 2.0.0 — Unreleased

A major version: it targets `net10.0` and Metalama 2026.1 as 1.x does, and the breaks are the ones
Semantic Versioning reserves for a major, listed under Changed as they land.

### Added

- **`CodoMetis.TypeKit`**: `Result.Catch`, the one place an exception becomes data. `Catch(() => …)`
  runs the call and answers `Result<T, Exception>`, or `Result<Exception>` for an action;
  `Catch<T, TException>` catches that type only and lets any other propagate; `CatchAsync` awaits the
  task, so an exception thrown after its first `await` is the error too. An `OperationCanceledException`
  passes through every overload, a delegate that returns a `Task` given to `Catch` is a compile error
  that names `CatchAsync`, and a delegate that returns null throws as `Result.Success(null)` does.
- **The closed-set family**, the release's headline: `IClosedSet<TSelf>` in `CodoMetis.TypeKit`, generated
  by `CodoMetis.TypeKit.Generators`. A sealed partial class whose every instance is a `public static
  readonly` field, with constant data through a private constructor where the members carry some. The
  generators add `Name`, `QualifiedName`, `All` in declaration order, `Match` with one required delegate
  per member, `TryParse` and `Parse` by name, `ToString()`, a JSON converter that writes the name and
  reads the member itself back, a private constructor where none is declared, and the attribute the
  satellites read. CMTK1013 to CMTK1019 refuse a declaration that is not partial, not sealed, without
  members, with a constructor that is not private, with another public static member of its own type,
  generic, or with a member not initialized with `new` where it is declared; CMTK1020 refuses a
  record. A hand-written `ToString()` is kept, and an overload of a generated method with another
  signature; any other generated member written by hand is CMTK1011. An instance the set's
  own code makes beside its members has no name, and reading it throws rather than write a null.
  CMTK0002 covers a closed set without the generators.
  CMTK1021 refuses a member named as a generated member, or one whose `Match` parameter another
  member's already is (`Open` and `open`). `Match` checks every delegate for null before it picks one,
  and a set is a JSON dictionary key by its name.
  `CodoMetis.TypeKit.EntityFrameworkCore` maps a closed set to a text column of the member's name, in a
  primitive collection and a JSON column too, through a compiled model and under Native AOT
  (`ClosedSetConverter<TSet>` names it explicitly); `CodoMetis.TypeKit.AspNetCore` describes it as a
  string enum. Nothing is registered per set.
- **`CodoMetis.TypeKit.Analyzers`**: CMTK0012, a closed set's `Match` given an argument by position.
  Every parameter has the same type, one delegate per member in declaration order, so reordering two
  members swapped their branches at such a call and compiled. The code fix names every argument after
  the parameter it binds to.
- **`CodoMetis.TypeKit`**: `Patch<T>`, a field of a JSON Merge Patch (RFC 7396): absent, null or a
  value, where System.Text.Json alone cannot tell an absent property from a null one. `default` is
  absent, the one meaningful default of the package, and the analyzers leave it alone. Reading never
  refuses a null; a value object inside a patch keeps its own rule. `Match`, `TryGetValue`, `IfValue`,
  `IfPresent` and `ToOption` apply it without reflection; `IfPresent` hands a clear over as null, a
  `T?` over a struct too, never as `default(T)`. Writing an absent patch is refused, and
  `[JsonIgnore(Condition = WhenWritingDefault)]` leaves it out. Under a source-generated context a
  primitive, a string, a `Guid`, a date, a `Uri` and a value object need no listing; a class or an
  enum does. Works under Native AOT: the converter is registered by the closed type's static
  constructor, since a converter built by reflection has no code there.
- **`CodoMetis.TypeKit.AspNetCore`**: `AddTypeKit()` describes a `Patch<T>` property as `T`'s schema,
  inlined, not required and admitting null, in 3.1 and 3.0; a value object inside a patch through the
  type it wraps.
- **`CodoMetis.TypeKit.Analyzers`**: CMTK0010, the constraints a type parameter cannot declare.
  `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` (new in the base package) on a type parameter
  say what C# has no `where` for, and the rule reports a type argument that breaks it at any closed
  use: a base type, a member, a parameter, a local, a `typeof`, a call with its arguments written
  or inferred. A type parameter forwarded without the attribute is a breach too, so the requirement
  follows the generic chain; `struct` proves `[Sealed]` and `[Concrete]`, `new()` proves `[Concrete]`.
- **`CodoMetis.TypeKit.Analyzers`**: CMTK0011, with the package's first code fix. A type marked
  `[RequireParameterlessConstructor]` (new in the base package, with `ConstructorAccessibility`)
  requires every concrete type that derives from or implements it, directly or through another, to
  have a parameterless constructor of the given accessibility, for the host that creates such types
  by reflection. A class that declares no constructor satisfies `Public` as it is; a missing
  constructor is reported at the type and the fix adds it, a wrong accessibility at the constructor
  and the fix changes it; a type with a primary constructor gets no fix. The code fixes ship in
  `CodoMetis.TypeKit.Analyzers.CodeFixes.dll` inside the analyzer package.
- **`CodoMetis.TypeKit`**: the text guards. `text.AsText()` is for text a person wrote: trimmed, every
  space separator folded to an ordinary space, composed to NFC so one name has one spelling, and
  refused when it carries a character that renders as nothing, such as a zero-width space, a Hangul
  filler or NUL, which `IsNullOrWhiteSpace` lets through; the two zero-width joiners are kept, since
  they spell Persian, Arabic and Indic scripts and emoji sequences. `text.AsIdentifier()` is for a
  value another system issued, a subject claim or a session id: trimmed and otherwise left as it
  arrived, with the joiners refused too. Both answer `Result<string, TextFault>`, where `Blank` is
  the caller's to read as an error or as absence and `Hidden` is a refusal in both; a null receiver
  is `Blank`. A value object over either is one line, shown in the README.

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
