# Changelog

All notable changes to CodoMetis.TypeKit and its satellite packages.

The five packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a given package saw no change in it.
Versions follow [Semantic Versioning](https://semver.org/). Entries are newest first.

A version's heading reads `## <version> — Unreleased` until the release is prepared, when it gets
the release date. The release workflow refuses a tag whose section still says `Unreleased`, and the
section is the GitHub release's notes.

## 0.1.0 — Unreleased

The first release.

### Added

- **`CodoMetis.TypeKit`**: `Option<T>`, `Result<T, TError>` and `Result<TError>`, with no public
  `.Value` or `.Error`, and the `Option.None()`, `Result.Success(...)` and `Result.Error(...)`
  markers that convert to whichever one a method returns. A `default` `Option` is `None`; a `default` `Result` is uninitialized, and
  every member that would pick a branch throws on it. `ToString()` never prints the content, and
  none of them serializes: System.Text.Json refuses them with `NotSupportedException` in both
  directions instead of writing `{}`, and a converter registered on the options takes precedence.
  Pipelines: `MapAsync`/`BindAsync`, a continuation of every combinator on a `Task<Result<…>>` so a
  chain is awaited once, `Zip` for two to six results, and `Sequence`/`Traverse` over a sequence, all
  stopping at the first error. The value-object contracts (`IValue<T>`, `IValidatedValue<TSelf, T, TFault>`, `IValueObject<TSelf, T>`),
  `OrderId.New()` for version 7 Guid identifiers, and `StoredJsonConverterFactory` for JSON the
  application stored itself.
- **`CodoMetis.TypeKit.Analyzers`**, arriving with the base package: CMTK0001 (no `default` of a
  value object, an `Option`, a `Result` or a `[RequireCustomInitialization]` struct), CMTK0002 (a
  value object in a project that does not reference the generators), CMTK0003 (an ignored `Result`
  or `Option`, warning), CMTK0004 (`Materialize`, which skips validation, called outside the EF Core
  satellite), CMTK0005 (an array or span of such a struct created with a length, warning), CMTK0006
  (a member of such a type that nothing assigns, warning), CMTK0007 (`FromKnownGood` given a
  parameter, suggestion) and CMTK0008 (the wrapped values of two different value objects compared,
  warning).
- **`CodoMetis.TypeKit.Generators`**: a one-line value-object declaration gets its constructor,
  `Value`, `From`, `TryFrom` and `FromKnownGood` (both derived from the hand-written `Create`),
  `Revalidate()` for values read back without validation, a
  JSON converter, `IParsable`/`ISpanParsable`/`IUtf8SpanParsable`, formatting in the invariant
  culture, comparison, a `TypeConverter` and `GetValue()`/`ValueOrNull()` extensions. Every
  generated way into a validated value object applies `Create`. A declaration that cannot be
  generated is a build error naming it (CMTK1000–CMTK1009). Formatting, comparison and JSON cost
  what the wrapped type's own do, and allocate nothing more. Metalama 2026.1, and no Metalama
  license is needed to build.
- **`CodoMetis.TypeKit.EntityFrameworkCore`**: `UseTypeKit()` maps every value object to a column
  of the type it wraps, with nothing registered per type, including keys, foreign keys, nullable
  properties and primitive collections, and translates `.Value`, `GetValue()` and `ValueOrNull()`
  in queries to the bare column.
- **`CodoMetis.TypeKit.AspNetCore`**: `AddTypeKit()` on `AddOpenApi` gives every value object the
  schema ASP.NET publishes for the type it wraps, wherever it appears: properties, bodies,
  containers, and route, query and header parameters.
- **Native AOT**, for every package that runs in an application. The run-time packages are built with
  the trim and AOT analyzers on, and the generated code is published with Native AOT and run in the
  consumer smoke test. With source-generated JSON a context lists the value objects, not what they
  wrap. The EF Core satellite works with a compiled model and precompiled queries, which is how EF
  Core runs under Native AOT. The OpenAPI satellite needs the wrapped types in the host's
  `JsonSerializerContext`, and says which one is missing.
