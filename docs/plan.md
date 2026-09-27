# Plan: CodoMetis.TypeKit

Status: **in progress, 2026-09-27.** Phases 0 and 1 are done. The decisions are in §8. The fabric
spike ([spikes/FabricSpike](../spikes/FabricSpike/README.md)) and the translation comparison
([spikes/ValueTranslation](../spikes/ValueTranslation/README.md)) are done.

## 1. Packages

| Package | Depends on | Metalama | Contents |
|---|---|---|---|
| `CodoMetis.TypeKit` | Analyzers (flows, §10) | **no** | `Option<T>`, `Result<T,TError>`, `Result<TError>` and their extensions; the value-object contracts `IValueObject<,>`, `IValueWrapper<,>`, `IValue<T>`, `IValidatedValue<,,>`, `IValueObjectMaterializer<,>` (§4), `KnownGood`; `TranslatedAsUnderlyingValueAttribute`, `RequireCustomInitializationAttribute` |
| `CodoMetis.TypeKit.Analyzers` | — | no | Roslyn analyzers and code fixes (§10). Nobody references it directly: it reaches every consumer through the base package |
| `CodoMetis.TypeKit.Generators` | TypeKit, Metalama.Framework 2026.1.x (flows) | yes | Internal aspects, internal `TransitiveProjectFabric`, `AspectOrder`. Future generated type families join this package too: one fabric, one aspect order |
| `CodoMetis.TypeKit.EntityFrameworkCore` | TypeKit, EF Core Relational | no | Generic converter, convention, `.Value` / `GetValue` / `ValueOrNull` translators, `UseTypeKit()` |
| `CodoMetis.TypeKit.AspNetCore` | TypeKit, Microsoft.AspNetCore.OpenApi | no | OpenAPI transformers |

**The bare name is the cheapest package.** Someone who only wants `Option`/`Result` references
`CodoMetis.TypeKit` and never receives Metalama by accident. Taking Metalama on is always an
explicit, named choice (`.Generators`). The EF and ASP.NET packages depend on the base package,
not on `.Generators`. They work at run time against the interfaces, so a host that maps value
objects from a referenced domain assembly never needs Metalama itself.

**Namespaces:**

| Namespace | Holds |
|---|---|
| `CodoMetis.TypeKit` | `Option`, `Result`, `IValueObject`, `IValueWrapper`, `TranslatedAsUnderlyingValueAttribute` |
| `CodoMetis.TypeKit.ValueObjects` | `IValue`, `IValidatedValue`, `KnownGood`, `IValueObjectMaterializer` |
| `CodoMetis.TypeKit.Attributes` | `RequireCustomInitializationAttribute` |
| `CodoMetis.TypeKit.EntityFrameworkCore` | converter, convention, translators |
| `CodoMetis.TypeKit.AspNetCore` | transformers |

The internal aspects live in `CodoMetis.TypeKit.Generators`. Users never type that namespace.

## 2. Phases

Each phase ends green, and its guards have been proven by seeding the defect
(`.claude/skills/verify-the-guard`).

0. **Repo skeleton. ✅ Done 2026-09-27.**
   - `Directory.Build.props`: warnings as errors, CS1591, Source Link, and package validation
     without a baseline until the first release.
   - `Directory.Packages.props` (central management; `spikes/` opts out), and `global.json`
     pinned to 10.0.401 / `latestPatch`.
   - The five shipping projects, still empty, in `CodoMetis.TypeKit.slnx`.
   - The analyzer targets netstandard2.0 on Roslyn 5.0.0 (the floor), with RS2008 release tracking.
   - `.github/workflows/dotnet.yml` (build and test, SHA-pinned actions, SDK from global.json) and
     `dependabot.yml`.
   - First guard: `MetalamaBoundaryTests`. Only `.Generators` may resolve Metalama, directly or
     transitively. Proven by seeding a direct reference in the base package, which failed the base
     and both satellites, and a transitive one via the EF satellite, which failed only that
     satellite.
1. **Option and Result. ✅ Done 2026-09-27.**
   - `Option<T>`, `Result<TError>`, `Result<T, TError>`, the `Result.Ok`/`Result.Error` markers,
     their extensions and `RequireCustomInitializationAttribute` in `CodoMetis.TypeKit`. Tests in
     `test/CodoMetis.TypeKit.Tests`.
   - The throw on `Uninitialized`, a `ToString` that never prints the content, and a
     `[DebuggerDisplay]` that does (§9).
   - Guards, each proven by seeding its defect:
     - one throw case per branching member (21), plus a completeness test that fails when a
       public `Result` member is added without being classified as branching or not;
     - `ToString` never contains the content, including for the markers;
     - the debugger display names an existing private property and shows the content;
     - the collapsing `Match` runs its success callback.
2. **Value-object contracts and analyzer.**
   - The interfaces and `KnownGood`.
   - `CodoMetis.TypeKit.Analyzers` with a symbol-based analyzer (§3), and CMTK0001 with its tests.
   - A test that the analyzer tests' verifier compiles against the *real* `CodoMetis.TypeKit`
     assembly, not a copy of the contracts.
   - CMTK0002 and its tests.
   - Once CMTK0001 reaches the test projects, the tests that build an uninitialized `Result` on
     purpose need a local suppression.
3. **Generators.**
   - The ten aspects in `CodoMetis.TypeKit.Generators`, applied by the fabric rather than by
     `[Inheritable]` on the interfaces.
   - No NodaTime compile-time dependency (§5).
   - Behaviour tests for every generated member, against woven probe types.
   - The surface snapshot (§6).
4. **EF Core.** §4. SQL snapshot tests via `ToQueryString` (no database), plus one
   Testcontainers PostgreSQL round trip per underlying type family.
5. **ASP.NET Core.** §7, preceded by its own measurement spike.
6. **Delivery.**
   - Consumer smoke test script: a throwaway project outside the repo, a private
     `NUGET_PACKAGES` and package source mapping, as in the sibling repos. It also covers a
     consumer that references **only** `CodoMetis.TypeKit` (§10).
   - Release workflow with Trusted Publishing, and SBOMs.
   - An **unlicensed-runner build**, which settles fabric spike finding 9.

## 3. Discovery is by interface

No namespace strings, assembly-name prefixes or type-name lists anywhere. A name-based check
passes in tests and breaks for the first consumer who renames something.

| Where | How |
|---|---|
| Analyzer, value-object rules | `CompilationStartAction` resolves `CodoMetis.TypeKit.ValueObjects.IValue`1` / `IValidatedValue`3` with `GetTypeByMetadataName` and compares `OriginalDefinition` with `SymbolEqualityComparer`. If `CodoMetis.TypeKit` is absent, nothing is registered |
| Analyzer, `[RequireCustomInitialization]` | The attribute symbol is resolved the same way |
| EF Core | Value objects are discovered at run time from the EF model (§4). There is no compile-time type scan and no assembly filter |
| OpenAPI | The transformer decides per `JsonTypeInfo.Type` whether it implements `IValueObject<,>` (§7). There is no referenced-assembly walk |

## 4. EF Core

- **Materializer.** An interface in `CodoMetis.TypeKit.ValueObjects`:
  `IValueObjectMaterializer<TSelf,T> { static abstract TSelf Materialize(T value); }`. The aspect
  implements it **explicitly**, so it is invisible on the type's public surface and reachable only
  through a constrained generic. Documented contract: *skips validation; for values this
  application wrote itself; never call it on input.* CMTK0004 enforces who may call it.
- **One converter.** `ValueObjectConverter<TVO,T> : ValueConverter<TVO,T>`. Pitfall: an expression
  tree cannot call a static abstract member directly (CS8927). The from-provider lambda therefore
  calls a plain generic helper, `Materializer.Create<TVO,T>(v)`, which does the constrained call.
- **No per-type comparer.** A `readonly record struct` already has value equality, and EF's default
  comparer uses it. A test pins that it agrees with comparing `.Value`.
- **Application.** `optionsBuilder.UseTypeKit()` registers an options extension. The name is
  deliberately not value-object specific, so a later `Option<T>` column mapping can join it. It
  adds:
  - either a **type-mapping-source plugin** that answers any CLR type implementing
    `IValueObject<,>` with the underlying type's mapping plus the converter,
  - or a pre-convention (`ConfigureConventions`) scan.
  **Spike this first.** The plugin wins if EF discovers struct value-object properties through it
  as scalars, with no `Properties<T>()`. The spike must also show that keys, foreign keys and
  primitive collections of value objects behave as they do with an explicit per-type
  `HasConversion`.
- **Translators.** `ValueObjectMemberTranslatorPlugin` / `ValueObjectMethodCallTranslatorPlugin`
  are registered by the same options extension, so a consumer wires nothing else.
- **How far the translation is unique, measured 2026-09-27**
  ([spikes/ValueTranslation](../spikes/ValueTranslation/README.md)):
  - `.Value` in a query predicate fails in Vogen 8.0.7 and in plain EF Core 10. Thinktecture
    10.5.0 has no public `.Value`.
  - Querying the underlying value through a **conversion-operator cast** works in plain EF Core
    10, and that is how Thinktecture users get it.
  - README claim: "`.Value` works in LINQ as it does in memory". Never "the only library that can
    query by the underlying value".

## 5. Aspects

- **Aspect-class count.** Ten classes. The Metalama Open Source edition (MIT) has no aspect-class
  limit (pricing page, 2026-09-26). The unlicensed CI build confirms it (phase 6).
- **NodaTime.** `ValueObjectJsonAspect` and `ValueObjectParsableAspect` handle NodaTime value
  types, but must not refer to NodaTime at compile time, which would force NodaTime on every
  consumer. They resolve the types by metadata name from the target's compilation, and skip the
  strategy when the type is absent. The generated code only mentions NodaTime when the value type
  is a NodaTime type, and then the consumer already references it.
- **Metalama 2026.1.** Aspect state uses `IDurableRef`, which exists in 2026.1. `[Durable]` on the
  `_value` template placeholder is 2027.0-only and stays out until the upgrade (decision 4). Build
  each aspect on 2026.1 as it lands, and use no 2027.0-only API.
- **`AspectOrder`** lives in `CodoMetis.TypeKit.Generators`. Open point: ordering against a
  consumer's own aspects (fabric spike D).
- **Not in scope:** a `Try`/`Unit` type (§9 non-goals), clock-type analyzers, text-guarding types,
  and decimal JSON converters unless an aspect needs them (check during phase 3).

## 6. Test strategy

1. **Behaviour.** Every public member of `Option`/`Result` and every generated member has a
   behaviour test. Generated members are tested against woven probe types, never against code
   written by hand in the test assembly.
2. **Generated surface.** A small tool reflects over the woven probe types. For each value object
   it renders the public members, implemented interfaces and attributes as sorted text, which is
   committed and asserted. An aspect change that adds or loses a member then shows up in review
   instead of shipping silently. Once 0.1.0 ships, package validation against the last release
   guards the hand-written surface too.
3. **Generated SQL.** `ToQueryString()` snapshots for `.Value`, `GetValue`, `ValueOrNull`,
   `StartsWith`, equality, and `Contains` over a list of ids.
4. **OpenAPI.** A probe host's emitted document, asserted per shape (§7).

## 7. OpenAPI: conflicting evidence, measure first

The goal is a per-type **schema** transformer. An earlier measurement found exactly that failing:
a schema transformer never reached the component of a type that only ever appears as a property,
so a document transformer was used instead. The current Microsoft docs (aspnetcore-10.0, updated
2026-08-19) say schema transformers run *before* schemas are hoisted into components. That
contradicts the measurement.

Phase 5 opens with a probe: a value object that appears only as a property, as a list element,
as a dictionary value, and as a route/query parameter. Assert the emitted document for each.
Then:
- **Schema transformer suffices** → a single `ValueObjectSchemaTransformer` that maps any
  `IValueObject<,>` to `context.GetOrCreateSchemaAsync(underlyingType)`. The underlying type can
  be anything, not only Guid or string.
- **The earlier measurement still holds** → keep that underlying-type mapping, but host it in a
  document transformer for components and parameters, plus a schema transformer for containers.

Vogen and Thinktecture document Swashbuckle only (checked 2026-09-27), so support for the built-in
`Microsoft.AspNetCore.OpenApi` is a gap worth filling. Confirm that before a README claims it.

## 8. Decisions

1. **License: MIT** (2026-09-26).
2. **Name: `CodoMetis.TypeKit`** (2026-09-27), replacing `CodoMetis.ValueObjects`, which was too
   narrow. Package map in §1.
3. **Analyzer ids: `CMTK`** (2026-09-27). Ids are public contract: a rule keeps its id even if it
   later moves to another package.
4. **Metalama 2026.1.x (latest stable)** (2026-09-26), not the 2027.0 preview.
   - Aspect state keeps `IDurableRef`, which exists in 2026.1.
   - `[Durable]` on the `_value` template placeholder is omitted, because it is 2027.0-only, and
     is added back on upgrade.
   - A 2026.1-built package works in a 2027.0 consumer (fabric spike finding 11).
   - Once 2027.0 is stable, upgrading is a planned task: MIT maintenance covers only the latest
     `YYYY.N`.
   - **Metalama is the generator technology for this project**, not a candidate for replacement.
5. **Option/Result** (2026-09-27): pure `Option`/`Result`, not a LanguageExt-style framework.
   They ship in the base package. No `Try`/`Unit` (§9).
6. **`Result` shape** (2026-09-27): `Result<T, TError>` (value first), `readonly record struct`,
   private fields, non-positional (§9).
7. **Analyzer as its own package** (2026-09-27), reaching consumers through the base package (§10).
8. **An uninitialized `Result` throws** (2026-09-27) instead of reporting `default(TError)` (§9).

Still open:

9. **Analyzer first-release rule set** (§10 proposes CMTK0001–0005 plus code fixes).
10. **context7.json.** The sibling repos register one. Add it once the repo is public.

## 9. Option and Result

`Result<T,TError>` (with the `Success`/`Error` markers and the `Result.Ok()`/`Result.Error()`
helpers), `Result<TError>` and `Option<T>` ship in `CodoMetis.TypeKit`. The value-object contracts
depend on them: `IValidatedValue.Create` returns a `Result`, and the generated `TryFrom` returns an
`Option`.

**Non-goals.** No Either, no Validation applicative, no effect or IO types, no higher-kinded type
emulation, no immutable collections, no `Try` or `Unit`. Anything that turns this into a second
LanguageExt is out.

**Shape, decided 2026-09-27.**
- **`Result<T, TError>`, value first.** Error-first (`Either e a`) exists in Haskell and Scala
  only because a generic type there can be filled in from the left alone, which C# cannot do.
  Rust, F#, Swift, DotNext, CSharpFunctionalExtensions and the csharplang unions design note all
  put the value first. `IValidatedValue<TValueObject, T, TFault>` does too.
- **`readonly record struct`, non-positional, private fields.** The synthesized equality is
  correct (`State` included). The synthesized `ToString` prints only public members:
  `Option { }` and `Result { State = Error }`. So it never prints a value, in line with
  `KnownGood`'s rule. **Never make these types positional:** positional parameters become public
  properties, which reintroduces `.Value` and puts the value into `ToString`.
- **The `Result.Ok(x)`/`Result.Error(e)` markers** keep their content internal too, so they print
  `Success { }` and `Error { }`. Only the implicit conversions read it.
- **`[DebuggerDisplay]`** on `Option` and both `Result` shapes shows the content in the debugger,
  where it is what someone stepping through wants to see. The debugger is not a log.
- **`Result<TError>` has no `AsEnumerable`.** A sequence of zero or one units says no more than
  the `bool` conversion.

**Decided 2026-09-27: an uninitialized `Result` throws.**
- A `default` result is `State == Uninitialized`. If the branching members tested only
  `State == Success`, it would take the **error** branch with `default(TError)`.
- For an enum fault that is the first member (e.g. `Blank`): a plausible, wrong reason. For a
  reference-type error it is `null`, despite `[NotNullWhen(false)]`.
- The analyzer blocks `default` in source, but array elements, class fields (CMTK0005/0006) and
  reflection still produce such instances.
- **Decision:** every member that picks a branch (`Match`, `Map`, `Bind`, `Tap`, `TapAsync`,
  `TryGetValue`/`TryGetError`, `AsEnumerable`, the `bool` conversion) throws
  `InvalidOperationException` on `Uninitialized`. That is a loud failure instead of a fabricated
  fault. `State`, equality and `ToString` stay safe to call.

**Why not an existing package** (checked 2026-09-26). The criteria: no public `.Value`/`.Error`,
a generic `TError`, `default` is not success, maintained.
- **Funcky 3.6.0** is closest (`[NonDefaultable]` enforced by an analyzer, no `.Value`), but its
  `Result<T>` fixes the error type to `Exception`.
- **nlkl/Optional** has the right Option shape but no Result, and no release since 2018.
- **CSharpFunctionalExtensions, DotNext and FluentResults** expose a throwing `.Value`.
- **ErrorOr, Remora.Results and Ardalis.Result** expose an unguarded one. With ErrorOr,
  CSharpFunctionalExtensions and Remora, `default(Result<…>)` reads as success.

C# 15 `union` types (in .NET 11 preview) ship no standard `Option`/`Result`, and they need .NET 11
anyway, while this repo targets net10.0.

**Against the value-object libraries** (checked 2026-09-27):
- Vogen validation returns Ok/Invalid with a plain string, and its `TryFrom` is `bool` + `out`.
- Thinktecture returns a typed `ValidationError` but ships no Result type.
- TypeKit's value objects return `Result<TVO, TFault>` from `Create` and `Option<TVO>` from
  `TryFrom`. That is the reason Option/Result and the value objects ship together.

## 10. Analyzer package and rule set

**Packaging (decided 2026-09-27).** `CodoMetis.TypeKit.Analyzers` (netstandard2.0; analyzers and
code fixes under `analyzers/dotnet/cs`). `CodoMetis.TypeKit` takes a package dependency on it.
- **Why separate:** rules and fixes can ship without a base version bump, and a consumer can take
  a newer analyzer by referencing it directly. The code-fix assembly (which needs Workspaces)
  stays out of a runtime library package.
- **Why still a dependency of the base package:** fabric spike consequence A. Whoever can see the
  interfaces must get the guard.
- **Pitfall, measured in the fabric spike:** a package dependency is emitted with
  `exclude="Build,Analyzers"` by default, and then the analyzer would **not** reach the base
  package's consumers. The reference therefore needs `PrivateAssets="none"`.
- **Guards for the pitfall:** a packaging convention test that reads the nuspec, and a consumer
  smoke test that references only `CodoMetis.TypeKit` and asserts `CMTK0001` fires.

Attributes stay in `CodoMetis.TypeKit`. The analyzer resolves them and the interfaces with
`GetTypeByMetadataName` and compares symbols (§3), so it does not reference the base package.
Inside the analyzer, rules that react to attributes are kept apart from value-object rules, so the
split stays mechanical if a generic analyzer package is ever wanted.

**Scope.** Code that compiles but is wrong about value objects, `Result`/`Option` and types that
forbid `default`. General linting (clock types, style) stays out, since Meziantou and Roslynator
exist.

| Id | Rule | Severity | Status |
|---|---|---|---|
| CMTK0001 | No `default`/`default(T)`/`new()`/`new T()` of a value object or a `[RequireCustomInitialization]` type | Error | planned |
| CMTK0002 | Type implements `IValue<>`/`IValidatedValue<,,>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so it is never woven | Error | planned |
| CMTK0003 | `Result`/`Option` return value ignored (expression statement, including an awaited `Task<Result<…>>`; `_ =` is the explicit opt-out). CA1806 can only enforce this per method name via `additional_use_results_methods`, not per return type | Warning | proposed |
| CMTK0004 | `IValueObjectMaterializer<,>.Materialize` called outside `CodoMetis.TypeKit.EntityFrameworkCore`. Enforces the validation-free-path invariant in SECURITY.md | Error | proposed |
| CMTK0005 | Array of a no-default type created with a length (`new OrderId[n]`, `GC.AllocateUninitializedArray`): every element starts as `default` | Warning | proposed |
| CMTK0006 | Field or auto-property of a no-default type in a class, not `required`, no initializer, not assigned in every constructor. This is the CS8618 equivalent nullable analysis does not give structs, and the largest remaining way to get a `default` value object | Info → Warning | **measure noise first** on EF entities with private parameterless constructors |
| CMTK0007 | `FromKnownGood` called with a non-constant argument | Info | **needs design**: "a value the caller just produced" is legitimate |
| — | Code fixes for the aspect's shape diagnostics (missing `partial`/`record`/`readonly`) | — | proposed. The aspect keeps its own error as a backstop |

**Release discipline.** Keep `AnalyzerReleases.Shipped/Unshipped.md` tracking (RS2008). A new rule
ships at Warning or Info in a minor version and is raised to Error only in a major. Consumers
build with warnings as errors, so a new Error rule in a minor version would break their builds on
update.
