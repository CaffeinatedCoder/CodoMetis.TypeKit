# Plan: CodoMetis.TypeKit

Status: **1.0.0 released 2026-09-28** (nuget.org, GitHub release `v1.0.0`). Phases 0 to 11 are done;
what is left for 1.x is listed at the end of phase 11, and package validation holds every pack to the
1.0.0 surface. The decisions are in §8, Native AOT in §11. The fabric spike
([spikes/FabricSpike](../spikes/FabricSpike/README.md)), the translation comparison
([spikes/ValueTranslation](../spikes/ValueTranslation/README.md)), the EF mapping spike
([spikes/EfMapping](../spikes/EfMapping/README.md)) and the OpenAPI spike
([spikes/OpenApiSchemas](../spikes/OpenApiSchemas/README.md)) are done.

## 1. Packages

| Package | Depends on | Metalama | Contents |
|---|---|---|---|
| `CodoMetis.TypeKit` | Analyzers (flows, §10) | **no** | `Option<T>`, `Result<T,TError>`, `Result<TError>` and their extensions, all carrying `NotWireTypeJsonConverterFactory` (§9); the value-object contracts `IValueObject<,>`, `IPlainValueObject<,>`, `IValue<T>`, `IValidatedValue<,,>`, `IValueObjectMaterializer<,>` (§4); `RequireCustomInitializationAttribute`; `GuidValueExtensions` (`OrderId.New()` from a version 7 Guid); and what only generated code and the satellites use (§3, §5, §11): `GeneratedValueObjectAttribute<,>`, `IValueObjectVisitor<>`, `TranslatedAsWrappedValueAttribute`, `GeneratedFactories`, `GeneratedParsing`, `GeneratedFormatting`, `GeneratedJson`, `IStoredJsonConverterSource` |
| `CodoMetis.TypeKit.Analyzers` | — | no | Roslyn analyzers and code fixes (§10). Nobody references it directly: it reaches every consumer through the base package |
| `CodoMetis.TypeKit.Generators` | TypeKit, Metalama.Framework 2026.1.x (flows) | yes | Internal aspects, internal `TransitiveProjectFabric`, `AspectOrder`. Future generated type families join this package too: one fabric, one aspect order |
| `CodoMetis.TypeKit.EntityFrameworkCore` | TypeKit, EF Core Relational | no | Generic converter, convention, `.Value` / `GetValue` / `ValueOrNull` translators, `UseTypeKit()` |
| `CodoMetis.TypeKit.AspNetCore` | TypeKit, Microsoft.AspNetCore.OpenApi | no | `AddTypeKit()`: one OpenAPI schema transformer (§7) |

**The bare name is the cheapest package.** Someone who only wants `Option`/`Result` references
`CodoMetis.TypeKit` and never receives Metalama by accident. Taking Metalama on is always an
explicit, named choice (`.Generators`). The EF and ASP.NET packages depend on the base package,
not on `.Generators`. They work at run time against the interfaces, so a host that maps value
objects from a referenced domain assembly never needs Metalama itself.

**Namespaces:**

| Namespace | Holds |
|---|---|
| `CodoMetis.TypeKit` | `Option`, `Result`, `ResultState`, the markers, `NotWireTypeJsonConverterFactory`, `RequireCustomInitializationAttribute`, `GuidValueExtensions` (`New()` is call-site vocabulary, like `Option`) |
| `CodoMetis.TypeKit.ValueObjects` | Every contract a user declares or constrains on: `IValue`, `IValidatedValue`, `IValueObject`, `IPlainValueObject`, `IValueObjectMaterializer`, and `StoredJsonConverterFactory` |
| `CodoMetis.TypeKit.CompilerServices` | What only generated code and the satellites touch, all `[EditorBrowsable(Never)]`: `GeneratedValueObjectAttribute`, `IValueObjectVisitor`, `TranslatedAsWrappedValueAttribute`, `GeneratedFactories`, `GeneratedParsing`, `GeneratedFormatting`, `GeneratedJson`, `IStoredJsonConverterSource`. The name follows `System.Runtime.CompilerServices` |
| `CodoMetis.TypeKit.EntityFrameworkCore` | `ValueObjectConverter<,>`; the plugins, internal |
| `CodoMetis.TypeKit.AspNetCore` | the schema transformer, internal |
| `Microsoft.EntityFrameworkCore` | `UseTypeKit()`, beside the providers' `UseNpgsql` (decision 22) |
| `Microsoft.Extensions.DependencyInjection` | `AddEntityFrameworkTypeKit()` and `OpenApiOptions.AddTypeKit()`, beside `AddEntityFrameworkNpgsql` and `AddOpenApi` (decision 22) |

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
   - `Option<T>`, `Result<TError>`, `Result<T, TError>`, the `Result.Success`/`Result.Error` markers,
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
   - Found in the fourth review on 2026-09-27, and fixed: `Option`/`Result` serialized as `{}` and
     `{"State":1}` and read back as `None` and uninitialized, silently. Every exported struct now
     carries `NotWireTypeJsonConverterFactory` (§9). Guard: with the attributes removed, 58 of the 62
     `NotWireTypeTests` fail, the other 4 pin the escape hatch and the absent-property gap.
   - Found in the fifth review on 2026-09-27, and fixed: `Result` never checked its delegates, so a
     null for the branch not taken passed until the other outcome first arrived (`Match(null, …)` on
     every error), and `Option.ToResult` and the zips lost `Option`'s own check inside a lambda. Every
     delegate is now checked before a branch is picked (§9). Guard: `NullDelegateTests` runs each case
     on both branches, and its completeness test holds every delegate parameter of the assembly to a
     case; with the checks reverted, 29 of its 42 tests fail.
2. **Value-object contracts and analyzer. ✅ Done 2026-09-27.**
   - The contracts: `IValueObject<,>`, `IPlainValueObject<,>`, `IValue<>`, `IValidatedValue<,,>`,
     `IValueObjectMaterializer<,>`, `GeneratedFactories`, `TranslatedAsWrappedValueAttribute`, and
     `GuidValueExtensions` with `New()` and `New(DateTimeOffset)`. No NodaTime.
   - CMTK0001 and CMTK0002, resolved by symbol once per compilation (§3). CMTK0001 also follows a
     type parameter's constraints (`default(T)`/`new T()` where `T : struct, IValue<…>`, or
     `T : IValue<…>, new()`, which only a struct value object satisfies).
   - The verifier tests compile against the real `CodoMetis.TypeKit` assembly, with no copy of the
     contracts, and `default(Option<int>)` is one of the positive controls.
   - Packaging (§10): `CodoMetis.TypeKit` depends on `CodoMetis.TypeKit.Analyzers`, which ships only
     `analyzers/dotnet/cs`. `AnalyzerPackagingTests` packs both and reads the result.
   - Guards, each proven by seeding its defect:
     - a drifted metadata name silences the positive controls (10 tests for `IValue`1`, 9 for the
       attribute);
     - losing indirect markers (2) or type-parameter constraints (2) fails their tests;
     - matching the generators by prefix or by suffix fails the look-alike cases (1 each);
     - dropping the dependency, excluding its analyzers, packing the analyzer into `lib/` or
       marking it a development dependency each fail a packaging test.
3. **Generators. ✅ Done 2026-09-27.**
   - The ten aspects, the transitive fabric and `AspectOrder` in `CodoMetis.TypeKit.Generators`,
     built on Metalama 2026.1.28 (§5).
   - Every generated way into a validated value object applies `Create` (§5, entry points).
   - Probes in `test/CodoMetis.TypeKit.Generators.Probes`, one per JSON and parsing strategy plus
     the edge cases, and behaviour tests for every generated member against them. The tests reach
     the generators only through the probes, which is the transitive-fabric check.
   - The generated-surface snapshot (§6), `GeneratedSurface.verified.txt`.
   - Build-outcome tests: a throwaway consumer with declarations that cannot be generated gets
     exactly CMTK1000–1009 and CMTK0001, nothing else, and no CMTK0002 beside the real generators.
   - Guards, each proven by seeding its defect:
     - a JSON read, `Parse` or `TryParse` that bypasses `Create` fails 7, 9 and 5 entry-point
       cases; a new public factory without a case fails the completeness tests;
     - `MinValue` on a validated number, a missing JSON-null or `Parse(null)` guard, and a fabric
       that misses indirect markers or nested types each fail their tests, and the surface ones
       the snapshot too;
     - without the more-than-one-marker or the generic-type check, the build-outcome tests see an
       aspect failure instead of CMTK1003/1005;
     - an analyzer that stops seeing value objects fails the CMTK0001 build test, and one that
       looks for a misspelled generators assembly breaks the probes build with CMTK0002.
   - Found while building them, and fixed: JSON reads and parsing constructed validated value objects
     without `Create`; `MinValue`/`MaxValue` wrapped an unvalidated bound; a JSON `null` or
     `Parse(null)` wrapped null; `bool`/`char` (explicit `IParsable`) did not compile; a NodaTime
     `LocalDate` could not be a dictionary key; an internal value object got a public extension
     class; a marker reached through a derived interface would not have been generated.
   - Found in review on 2026-09-27, and fixed with guards proven by reverting: an enum-backed value
     object's parsing threw for every input; string ordering was culture-sensitive while equality
     is ordinal; `Parse(x.ToString(), null)` did not round-trip outside the invariant culture;
     `IValue<int?>` failed as an aspect bug instead of CMTK1005; a fallback JSON key did not read
     back; a record class's comparison threw on null (§5); `Option.Some(null)` reported a value;
     and `Result<TError>` did not accept the `Result.Error(e)` marker (§9).
   - Found in the second review on 2026-09-27, and fixed the same way (39 guards fail with every fix
     reverted): two nested value objects of one name crashed Metalama, and a declared
     `{Name}Extensions` failed the aspect (CMTK1007 now); malformed JSON threw `FormatException`,
     which a minimal API answers with 500; interpolation formatted in the current culture, so
     `Parse($"{x}", null)` read "1,5" as 15; a `DateTime` without an offset was converted through
     the server's time zone; `From(null)` wrapped null; `Result` held a null value or error;
     `Result<TError>.Match` could not see the error (§9); a derived record-class value object failed
     inside the generated code (CMTK1006 now); CMTK0001 missed `new T()` under `new()`.
   - Found in the third review on 2026-09-27: a hand-written comparison operator failed the aspect
     (LAMA0500) and a hand-written object `CompareTo` was kept silently beside the generated generic
     one; both are CMTK1008 now, and the one seam, `CompareTo(TSelf)`, is documented and pinned (§5).
   - Found by the OpenAPI spike on 2026-09-27 (finding 16): a record class that wraps itself, or
     two that wrap each other, compiled without a word, and a struct cycle failed as LAMA0611. Every
     value object over a value object is CMTK1005 now (§5). Guards, each proven by seeding its
     defect: without the refusal 12 build-outcome tests fail (the struct cycle's LAMA0611 among
     them); reporting a cycle as plain nesting fails 4 (a self-wrap then compiles again); allowing
     nesting without a cycle fails 2.
   - Found in the fifth review on 2026-09-27: a hand-written constructor. One with the generated
     constructor's signature failed as LAMA0611, a positional record (`OrderId(Guid Value)`) as
     LAMA0500 or an exception in the aspect (LAMA0041), and any other compiled without a word: it
     skipped `Create`, left `Value` null, or, as a record's copy constructor, did that to `with`.
     Every hand-written instance constructor is CMTK1009 now (§5). Guards, each proven by seeding its
     defect: without the refusal 13 build-outcome tests fail, the LAMA errors among them; refusing a
     static constructor too fails 2.
4. **EF Core. ✅ Done 2026-09-27.**
   - The mapping spike first (spikes/EfMapping): an additive type-mapping-source plugin maps every
     value object, with keys, foreign keys, nullable properties and primitive collections, on
     SQLite and PostgreSQL, whatever the registration order, and beside a library that replaces
     EF's converter selector (decision 9).
   - `CodoMetis.TypeKit.EntityFrameworkCore`: `ValueObjectConverter<TSelf, T>`, the plugin, the
     `.Value`/`GetValue()`/`ValueOrNull()` translators, `UseTypeKit()`, and
     `AddEntityFrameworkTypeKit()` for an application that builds EF's internal service provider.
   - Stored JSON (§4): `StoredJsonConverterFactory` in the base package (decision 10).
   - Tests: the model on SQLite, SQL snapshots on PostgreSQL via `ToQueryString`, the default
     comparer against `.Value`, and one Testcontainers PostgreSQL round trip covering every
     wrapped-type family, stored values the rules refuse, and the translated queries.
   - Guards, each proven by seeding its defect: a plugin that stops recognising value objects
     fails 33 of 34 EF tests (all but the control), one that drops the column facets fails the
     facet test; losing the member or the method translator
     fails 4 and 3; a validating `Materialize` fails the stored-row test; a stored-JSON converter
     that validates fails the stored value and key tests. The SQL snapshots first failed on the
     cast `Convert` produced, which led to re-typing the column (§4).
   - Found in the fifth review on 2026-09-27, and fixed: the method translator took any static
     one-argument method carrying the public `TranslatedAsWrappedValueAttribute` for the unwrap, so
     a hand-marked `Length(this ProbeCode)` became `WHERE o."Code" = '3'`. It now requires the
     unwrap's signature (§4). Guard: the hand-marked test fails without the check, and the
     `GetValue`/`ValueOrNull` snapshots, now with a record class's `ValueOrNull`, still translate.
5. **ASP.NET Core. ✅ Done 2026-09-27.**
   - The measurement spike first (spikes/OpenApiSchemas): a schema transformer reaches a value
     object that only ever appears as a property, which the earlier measurement had denied; container
     elements and `TryParse`-bound parameters need handling of their own (§7, decision 11).
   - `CodoMetis.TypeKit.AspNetCore`: `OpenApiOptions.AddTypeKit()`, which adds
     `ValueObjectSchemaTransformer`. A value object's schema is filled in, keyword by keyword, from
     the schema ASP.NET itself publishes for the wrapped type.
   - Tests in `test/CodoMetis.TypeKit.AspNetCore.Tests`: a probe host with minimal APIs and MVC whose
     document is read per shape (15 value-object properties across the wrapped-type families, nullable, list, array, set, dictionary,
     nested containers, bodies, route/query/header/`[AsParameters]`/MVC `[FromQuery]` parameters, a
     query array), each against a control that uses the wrapped type, in OpenAPI 3.1 and 3.0; the
     host's enum converter and number handling, on the wire too; composition with another
     transformer in either order; a host that inlines value objects; a keyword-completeness test over
     every `OpenApiSchema` property.
   - Guards, each proven by seeding its defect: not registering it fails 66 tests; dropping the
     container branch 9, the parameter branch 18, the MVC model-metadata fallback 8; using the
     parameter descriptor instead fails the MVC `[FromQuery]` object (4); keeping ASP.NET's `string`
     placeholder fails the integer parameters (8); dropping the nullable unwrap 9; copying
     `Metadata` 44; overwriting what a container or a keyword already had 1 and 28; sharing
     collections 5; assigning a missing keyword (null) 174; without the recursion stop every
     document is refused (73); without the cycle refusal the test host dies of a stack overflow.
   - Found while building it: assigning `null` to `OpenApiSchema.Const` is not a no-op, it writes
     `"const": null`, so every keyword is assigned only when the wrapped schema has one, and the
     keyword tests compare the written schema, not the getters.
   - Found in the spike and documented, not fixed: ASP0020 (an error) for a minimal-API route
     parameter whose value object is declared in the same project (decision 12).
6. **Delivery. ✅ Done 2026-09-27**, up to the first tag, which needs the GitHub repository and the
   nuget.org policy (release.yml, "Setup, once").
   - Per-package READMEs: `src/<Package>/README.md`, packed and named by `PackageReadmeFile` from
     `src/Directory.Build.props`, so a new package cannot pack without one. `PackageReadmeTests`
     packs every shipping project and reads the nuspec and the package.
   - `test/consumer-smoke-test.sh [feed]`: packs the five packages (or takes a packed feed) and
     builds three consumers outside the repository, with a private `NUGET_PACKAGES`, package source
     mapping and the repository's `global.json`. `core` references only `CodoMetis.TypeKit`: Option
     and Result work, no Metalama arrives, CMTK0001 and CMTK0002 fire. `layered`: a domain library
     on the generators and an app that reaches them only through it; the app uses the generated
     members, the transitive fabric generates a value object the app declares, and CMTK0001 fires in
     the app with no CMTK0002. `host`: both satellites in a running web host; the OpenAPI document,
     route binding, EF mapping, a round trip and the `.Value` SQL. It asserts on output, diagnostics
     and the resolved package graph, never on an exit code alone.
   - CI (`dotnet.yml`): test, then a pack job (smoke test against the packed feed, SBOMs) and a
     smoke-test job that packs its own feed, the two modes a sibling repository found not to be
     interchangeable.
   - `release.yml`: on a `v*` tag, verify (tag against `Version`, the changelog dates the version,
     full suite, pack, smoke test against the packed feed, one CycloneDX SBOM per package), then
     publish through Trusted Publishing behind the `nuget` environment's approval, then a GitHub
     release with the changelog section as notes and the SBOMs attached. Only the publish job can
     mint a token, and it cannot write to the repository. `CHANGELOG.md` and a release checklist in
     CONTRIBUTING.md.
   - SBOMs exclude the analyzer's `PrivateAssets=all` Roslyn references: unfiltered, the analyzer's
     SBOM listed 13 components for a nuspec that declares none (measured with CycloneDX 6.2.0).
   - The unlicensed build: fabric spike finding 9 confirmed in a clean container, no license file
     and no variable, the whole solution in Release and all 254 generator tests green.
   - Guards, each proven by seeding its defect: the smoke test fails when the base package loses its
     analyzer dependency (and the `default(Option<int>)` build then succeeds) and when `AddTypeKit()`
     does nothing (three OpenAPI assertions); a generators package without its assembly fails at
     pack (NU5128), and a non-flowing Metalama in the repository's own build.
     `ReleaseWiringTests`, `ChangelogTests` and `PackageDependencyTests` (17 seeds, each failing its
     test): a renamed workflow or environment, a token in the verify job, repository write in the
     publish job, a dropped tag or changelog check, either smoke-test mode dropped, a non-executable
     script, a narrowed SBOM filter, an unpinned tool, a second publishing path, a malformed or
     missing changelog section, a dated version with unshipped rules, a satellite without the base
     package, a satellite depending on the generators.
   - Found by the clean container: the build-outcome tests built their consumer in Debug whatever the
     run's configuration, so CI's Release run would have failed 17 of them; the consumer now builds
     in the test assembly's own configuration.
   - Found by the smoke test: every README returned a bare fault from a method typed
     `Result<T, TError>`, which does not compile (§9); and a host that has
     `Microsoft.AspNetCore.OpenApi` only through the satellite fails with CS9137, because that
     package enables its generator's interceptors in `build/`, which NuGet imports for a direct
     reference only. The satellite README says to reference it, as the `webapi` template does; a copy
     of Microsoft's switch in our own `buildTransitive` would drift from theirs.

7. **Native AOT. ✅ Done 2026-09-27** (§11).
   - `IsAotCompatible` on the three run-time packages, so the trim and AOT analyzers run in every
     build and a warning fails it. `AotCompatibilityTests` reads the packed assemblies' metadata; the
     compile-time packages are exempt by name, with the reason.
   - The consumer smoke test's `aot` consumer: a domain library and a web host with JSON, OpenAPI
     and EF Core (compiled model, precompiled queries), published with `PublishAot`. No trim or AOT
     warning may name a package or a woven value object, and the native binary is run and asserted
     on. The workflows install clang and zlib before it, held by a wiring test.
   - Found by publishing with Native AOT, each fixed and guarded:
     - `StoredJsonConverterFactory` found its private constructor by reflection, which the trimmer
       removed: stored JSON was **validated after all**, silently. It now asks the generated
       converter through `IStoredJsonConverterSource`.
     - the OpenAPI satellite described **every value object as `{}`**, silently: the trimmer removes
       `IValueObject<,>` from a type's interfaces when nothing uses it, and `GetInterfaces()` found
       nothing. The satellites now read `GeneratedValueObjectAttribute<,>` (decision 17).
     - `Option`/`Result` refused JSON with the runtime's "missing native code" message instead of
       their own: the refusing converter was made with `MakeGenericType`. One object-typed converter
       now serves every type.
     - the generated JSON converter serialized the wrapped value through the options (20 warnings in
       the consumer's assembly), and a source-generated context without the wrapped type refused a
       `decimal` value object outright, on the JIT too. It now goes through `GeneratedJson.TypeInfo`.
     - the generated parsing of a `[TypeConverter]` type (NodaTime) called
       `TypeDescriptor.GetConverter`, which requires unreferenced code.
     - EF Core: the converter's read path called an internal helper, which a compiled model
       (`dotnet ef dbcontext optimize`) writes out as C# in the application's assembly, where it does
       not compile; a precompiled query cast the converter to its design-time type, which the
       compiled model does not recreate; a value-object parameter, mapped at run time, hit EF's own
       `MakeGenericType` guard composing its JSON reader/writer; and the converter was made with
       `MakeGenericType`.
   - Guards, each proven by seeding its defect: reverting the generated JSON to the options fails 6
     `SourceGeneratedJsonTests`, and in the smoke test's `aot` consumer the warnings assertion (8 woven
     warnings) and the JSON assertions; each reflection or `MakeGenericType` call put back fails the
     build under the analyzers, and suppressed with `#pragma` only, the smoke test's warnings
     assertion (IL2070 from ILCompiler); without the generated attribute, the OpenAPI host under
     Native AOT is `{}` again; dropping `IsAotCompatible` from a package fails
     `AotCompatibilityTests` for that package; a missing or late toolchain step fails the wiring test.
   - Not caught at run time: a `GetInterfaces()` lookup suppressed with
     `[UnconditionalSuppressMessage]`, since the attribute keeps the interface alive (§11).
8. **Pre-release naming and syntax. ✅ Done 2026-09-27.**
   - Names (decision 18): the wrapped value, the plain value object, the value object's own type
     parameter and the delegate parameters each have one name, and the namespaces follow who uses
     a type (§1). Every rename is a compile error for code that used the old name; the analyzer's
     metadata-name tests and the generated-surface snapshot moved with them, the snapshot by exactly
     the interface rename and nothing else.
   - Syntax (decision 19): `Result.Success` (was `Result.Ok`), an `Option.None()` marker, `MapError`,
     `Bind` from a valued result onto `Result<TError>`, `Or(fallback)` (was `Coalesce`), and a
     `ToResult(error)` that keeps the value; the marker-lambda overloads and the collapsing `Match`
     are gone. Measured first against a replica of the conversions: the marker works in a return, a
     conditional beside `Some`, a `Bind` lambda and an `async` method, and the new `Bind` resolves
     beside the generic one. The completeness tests forced a case for every new member; seeding
     `None` out of the refusal, `MapError` past the uninitialized check, or the command into the
     error branch of `Bind` fails 7, 1 and 1 tests. `ToResult` checks its error on either branch,
     like the delegates, and a `Some` that accepted a null error fails its test.
9. **Before the first release: rule set, Result pipelines, performance. ✅ Done 2026-09-28.**
   - The analyzer rule set (decision 13): CMTK0003–CMTK0008 beside CMTK0001/0002 (§10). CMTK0004
     ships at Error, which only the first release can do without a major version.
   - `Revalidate()` on a validated value object (§5): `Create` applied to the value it holds, for
     finding the stored values a rule added later refuses.
   - `Result` (§9): `MapAsync`/`BindAsync` on both shapes, and on a `Task<Result<…>>` a continuation
     of every combinator (`MapAsync`, `BindAsync`, `MapErrorAsync`, `TapAsync`); `Zip` for two to six
     results; `Sequence` and `Traverse`. All stop at the first error.
   - Benchmarks (`benchmarks/`), against the wrapped types, and a spike against Vogen
     ([spikes/ValueObjectPerformance](../spikes/ValueObjectPerformance/README.md)).
   - Found by the benchmarks, and fixed: `TryFormat` boxed the wrapped value (32 bytes, 3.5 times a
     `Guid`'s time); comparing an enum-backed value object boxed both operands (sorting 1,000 took 8
     times as long and allocated 367 KB); the JSON converter wrote and read a number through a nested
     serializer call (1.35–1.40 times). All at parity after the fix (§5).
   - Found while measuring the rules on woven code: Metalama runs analyzers on the source before
     weaving, where a call to a generated member of a value object in the same project does not bind,
     so CMTK0003 missed an ignored `TryFrom` and CMTK0007 every `FromKnownGood` (§10).
   - Guards, each proven by seeding its defect: the new `Result` members' completeness tests force a
     case each (28 null-delegate cases, one uninitialized case per extension, one continuation per
     combinator), and six seeds (zips short-circuiting, a delegate checked after the await, `Sequence`
     enumerating past the error, `MapAsync` calling its selector on an error, a combinator without a
     continuation, the second error first) fail 5, 16, 3, 2, 3 and 1 tests. Each rule's verifier tests
     have a positive control per form and a negative control per exemption; 29 seeds across the six
     rules each fail their test. A `Revalidate()` that skips `Create` fails both of its behaviour
     tests, one of them against PostgreSQL. CMTK0004's exemption of EF's compiled model is measured by the smoke
     test: with generated code analysed, the AOT consumer fails with six CMTK0004 errors in
     `CompiledModels/OrderEntityType.cs`. The build-outcome consumer holds every rule to a value object
     of its own project; without the unbound-call path CMTK0003 and CMTK0007 fail there. The
     performance fixes are held by allocation tests on a Debug build, where the JIT never removes a
     box, and a number-handling matrix; reverting any fix fails them.
10. **Release readiness for 1.0.0. ✅ Done 2026-09-28.** Four independent audits (the generators, the
    satellites, the analyzers, the documentation), each confirming findings empirically in throwaway
    consumers of packed packages, and the fixes below, each with a test that fails with the fix reverted.
    - Release: the first release is 1.0.0 (decision 21); package tags, copyright and release notes,
      held by `PackageReadmeTests`; the analyzer's Roslyn floor measured and held (decision 25), and the
      build-time `Microsoft.CodeAnalysis.Analyzers` on 5.9.0; registration extensions in the framework
      namespaces (decision 22), which the smoke test's consumers use without a `using`.
    - `Option`/`Result`: `Ensure`, `TapError` and their continuations; query syntax with more than
      one `from` (the one-selector `SelectMany` made the second `from` a compile error);
      `GetValueOrNone`; `FirstOrNone()`/`LastOrNone()` without a predicate; `FirstOrError`/`LastOrError`
      check their error on either branch, as `ToResult` does. The completeness tests forced a null and
      an uninitialized case for each; six seeds fail seven tests.
    - Generators (§5): a hand-written `ToString()` was replaced silently (a secret printed); a value
      the wrapped type could not parse was quoted in the exception; the JSON differed from the wrapped
      type's for `TimeOnly`, date keys, host converters and key policies; an enum-backed value object
      allocated 4.7 KB per JSON value; `Uri` parsing disagreed with JSON; `ToType` failed the identity
      conversion; nine declarations failed as Metalama errors (CMTK1005, CMTK1010, CMTK1011 now). Guards:
      `ToStringSeamTests`, `UnreadableInputTests`, `JsonParityTests`, the allocation and parsing tests,
      18 build-outcome rows.
    - Analyzers (§10): nothing fired in Razor; `OrDefault()` and `FirstOrDefault` handed out defaults
      unreported (CMTK0009); false positives on guard checks and zero-length arrays; false negatives
      in the declaring project, for `x!`/`(x)`/named arguments, static comparisons, and a `Task` of a
      result converted to `Task`; CMTK0001 35% faster. 42 verifier tests and six build-outcome cases
      added, every seed recorded failing.
    - Satellites (§4, §7): `.Value` on a collection element failed to translate; a key over an integer
      lost its identity column (decision 23); nested value objects shared an OpenAPI component
      (decision 24). The smoke test's AOT consumer inserts through an identity key.
    - Documentation: every C# block of the READMEs is compiled and its stated outputs asserted (§6,
      item 6); four samples did not compile, one SQL comment was wrong, and a dozen claims were wrong
      or untested (a JSON `null` refused by a record class, configuration binding under the source
      generator, a JSON 400 with a message from a minimal API, "symbols for every package", …). Each
      is now corrected, or backed by a test that did not exist.
    - Found and documented, not fixed: a property absent from a JSON body stays `default` (System.Text.Json
      calls no converter; `required`/`RespectRequiredConstructorParameters`, now in the ASP.NET README
      with a test); `ConfigureConventions` for the wrapped type does not reach a value object; a
      validation attribute on a value-object property lands in the shared OpenAPI component.
    - Upstream: the Metalama issue behind CMTK1010, reproduced with Metalama.Framework 2026.1.28 and
      2027.0.3-preview by a five-line aspect that introduces `ToString` with `OverrideStrategy.Override`
      into a `partial record struct` declared in two parts (LAMA0611 CS0111, then LAMA0613). Lift
      CMTK1010 once Metalama emits the override once.

11. **Second review, against the packed 1.0.0. ✅ Done 2026-09-28.** Five reviews (the generators,
    `Option`/`Result`, the analyzers, the satellites, packaging and release), each building throwaway
    consumers of the packed packages with a private package cache, and the fixes below, each with a
    guard seeded to fail.
    - Analyzers (§10): where Metalama compiles a project, source generators run after its
      transformation (Metalama 2025.0), and an analyzer sees only the source. Every rule was silent in
      the `.razor` files of the usual Blazor layout (a web project referencing a value-object project)
      and missed forms in the declaring project. The analyzer package's `buildTransitive` props lists
      the analyzers' namespace as a `MetalamaTransformedCodeAnalyzer` item (decision 33).
      `AnalyzerPackagingTests` holds every analyzer to it; the smoke test's layered consumer gains a
      Razor library and a guard in Domain, and both build clean with the props left out. Every rule
      has a `helpLinkUri` (`AnalyzerHelpLinkTests`). The README adds `[RequireCustomInitialization]` for
      a struct made of value objects, and `.globalconfig` for severities in Razor, which no
      `.editorconfig` section reaches (measured).
    - `Option`/`Result` (§9): `Result<T, TError>` lost its `bool` conversion (decision 29);
      `TapErrorAsync(Func<TError, Task>)` on both shapes and both continuations, since an `async` error
      callback ran as `async void` and its exception ended the process (decision 30); `TryCast` on
      `object?` removed (decision 31). CMTK0003 treats the instance `TapErrorAsync` like `TapAsync`.
      Five seeds fail twelve tests.
    - Generators (§5): state besides the wrapped value is CMTK1012 (a `Currency` lost in JSON, a lazy
      cache that made equal instances unequal, `required` as LAMA0611 CS9035); a hand-written equality,
      `PrintMembers` without `ToString()`, and explicit implementations of the interfaces the
      generators implement are CMTK1011, or CMTK1008 for comparison (an explicit `IParsable<T>.Parse`
      let a generic `T.Parse` past `Create`); a declared `IConvertible` is CMTK1011 (it was LAMA0041);
      a `file`-local value object below a directive or nested in a `file` type is CMTK1005 (it was
      LAMA0001, failing the whole project); an explicit `Create` beside a public one is CMTK1011, since
      a generic `T.Create` reached a second rule set; and what a base record declares counts as the
      value object's own (an unsealed `ToString()` on the base was replaced and printed the value).
      The first CMTK1012 refused every record base from another assembly (its compiler-generated
      `EqualityContract`); the check now skips `[CompilerGenerated]` members. Decision 32.
    - Satellites (§4, §7): a container of nullable value objects, and a nullable value object inlined
      by `CreateSchemaReferenceId`, admit null as the wrapped type does, in 3.1 and 3.0 (decision 36);
      `InlinedValueObjectTests` compares 34 positions with the wrapped type. On SQLite an integer
      value-object key is not `AUTOINCREMENT`, so the model never matched its snapshot and `Migrate()`
      threw: the README documents `UseAutoincrement()`, and `MigrationSnapshotTests` checks it
      (decision 35). The READMEs state the 10.0.12 floors (NU1605 below them), `AddTypeKit()` per
      document or `ConfigureAll<OpenApiOptions>`, that `UseTypeKit()` needs a relational provider, and
      that value objects with one name in different namespaces share a component, as any type does.
    - Packaging (§6): the SBOMs also exclude what the SDK adds (`Microsoft.NET.ILLink.Tasks`,
      `NETStandard.Library`), read from restore output (`suppressParent: All`); the ASP.NET Core
      satellite drops `Microsoft.AspNetCore.OpenApi.SourceGenerators`, which had baked ten unused types
      into it (assembly 229 → 37 KB), held by `GeneratedCodeTests`; symbol packages are pushed in a step
      of their own, since a duplicate .nupkg made NuGet skip its .snupkg on a re-run; package
      descriptions are plain text, and the release notes omit the changelog heading.
    - Analyzer gaps, closed before the tag: CMTK0003 reports a collection of results the statement
      made (`Task.WhenAll`, `Select(…).ToList()`) and a method group of a `Task<Result>` method
      converted to a delegate returning `Task` (14 seeds); CMTK0008 reports a LINQ join on two
      different value objects' values, query and method syntax (8 seeds); CMTK0009 reports `Find`,
      the immutable collections' own `…OrDefault`, and the async forms of `System.Linq.AsyncEnumerable`
      and EF Core (7 seeds). CMTK0001 no longer reports a `default` passed to a comparison guard or an
      assertion, or assigned to the `out` parameter of a `bool` Try method (11 seeds).
    - Refusals, after a reviewer's note that the refusal-heavy design will draw "why does this throw?"
      reports: every deliberate run-time refusal ends with a link to its subsection of the base README's
      "Why does this throw?" (`Refusals`, `RefusalLinkTests`, 38 refusals), the two raised by generated
      code (`From(null)`, a JSON null) through `CompilerServices` helpers; every CMTK1000–CMTK1012
      message ends with a link to the Generators README's Build errors table, since a Metalama 2026.1
      `DiagnosticDefinition` takes no help link (`BuildOutcomeTests`). `OrNull()` for an `Option` of a
      value type (`ValueTypeOptionExtensions`), so the JSON refusal's advice compiles for `Option<int>`.
      A nullable `Option`/`Result` is refused with its own message, null or not: writing null threw
      `NullReferenceException`, and reading an option gave the `Result` advice. A source-generated
      context cannot refuse `Option<T>?` cleanly (it would need `MakeGenericType`) and rejects the
      contract when it builds it; the README says so. Decision 37.
    - Documentation: the root README gains "How it compares" (when TypeKit fits, a decision table of
      needs another library meets better, and detail tables against Vogen, Thinktecture,
      StronglyTypedId, LanguageExt, CSharpFunctionalExtensions and ErrorOr, each claim read from source
      at the named release on 2026-09-28, the next major in preview included) and "Built with Metalama".
      The Generators README links to the comparison. Both are GitHub-only and can be corrected without
      a release.
    - Found and left for 1.x: CMTK0003 over `.Wait()` and `await foreach`; CMTK0008 over tuple `==`,
      `Contains` and `is var`, and composite join keys (all Warnings, decision 34). Raw Metalama errors
      for a value object named after a generated member (`From`, `TryFrom`, `MinValue`), a wrapped type
      whose `Parse` does not return it or that is abstract, and a public base member whose signature
      matches a generated one (LAMA0500). `ToString()` of a default `Uri`/`Version` value object throws; `IConvertible` quotes
      the value; `class Email : IValue<string>` reports one of its three errors at a time; `IValue<object>`
      writes `{}`. JSON on `Option<T>?` throws the wrong exception; a default marker converts.
      `UseTypeKit()` on a non-relational provider could refuse in `Validate`.

## 3. Discovery is by interface

No namespace strings, assembly-name prefixes or type-name lists anywhere. A name-based check
passes in tests and breaks for the first consumer who renames something.

| Where | How |
|---|---|
| Analyzer, value-object rules | `CompilationStartAction` resolves `CodoMetis.TypeKit.ValueObjects.IValue`1` / `IValidatedValue`3` with `GetTypeByMetadataName` and compares `OriginalDefinition` with `SymbolEqualityComparer`. If `CodoMetis.TypeKit` is absent, nothing is registered |
| Analyzer, `[RequireCustomInitialization]` | The attribute symbol is resolved the same way |
| Analyzer, CMTK0002 | The compilation references an assembly whose name is exactly `CodoMetis.TypeKit.Generators`, ignoring case as assembly names do. That is the package's own id, which a consumer cannot rename, and a prefix or suffix match never counts. The assembly is not strong-named, so the name is the part of its identity to compare; the version must not be |
| EF Core | EF asks the type-mapping plugin per CLR type, and it answers for any type carrying `GeneratedValueObjectAttribute<TValueObject, T>` (§4). There is no type scan and no assembly filter |
| OpenAPI | The transformer reads `GeneratedValueObjectAttribute<,>` on the schema's `JsonTypeInfo.Type`, its element type, or a parameter's type or model metadata (§7). There is no referenced-assembly walk |

Run-time code that holds only a `Type` reads the attribute rather than calling `GetInterfaces()`
(decision 17). Its type arguments are constrained to `IValueObject<TSelf, T>` and
`IValueObjectMaterializer<TSelf, T>`, so the interface still decides what a value object is; the
attribute is how a type says so after trimming, which removes an interface nothing uses and keeps
custom attributes.

## 4. EF Core

- **Materializer.** An interface in `CodoMetis.TypeKit.ValueObjects`:
  `IValueObjectMaterializer<TSelf,T> { static abstract TSelf Materialize(T value); }`. The aspect
  implements it **explicitly**, so it is invisible on the type's public surface and reachable only
  through a constrained generic. Documented contract: *skips validation; for values this
  application wrote itself; never call it on input.* CMTK0004 enforces who may call it.
- **One pair of conversions.** `ValueObjectConverter<TVO,T> : ValueConverter<TVO,T>`, public so a
  property can also name it explicitly. Its expressions call its own public static
  `ProviderValue` and `Materialize`: an expression tree cannot call a static abstract member
  directly (CS8927), and EF's compiled model writes the expressions out as C# in the application's
  assembly, where an internal helper did not compile (§11). `UseTypeKit()` composes a plain
  `ValueConverter<TVO,T>` over the same expressions, the type the compiled model recreates, because a
  precompiled query casts a property's converter to its design-time type.
- **No per-type comparer.** A `readonly record struct` already has value equality, and EF's default
  comparer uses it. A test pins that it agrees with comparing `.Value`.
- **Application (decided 2026-09-27, spikes/EfMapping).** `optionsBuilder.UseTypeKit()` registers
  an options extension. The name is deliberately not value-object specific, so a later `Option<T>`
  column mapping can join it. It adds an `IRelationalTypeMappingSourcePlugin` that answers any
  `IValueObject<TSelf, T>` with the provider's mapping for `T` and the converter composed onto it.
  EF consults it per CLR type wherever it maps one: property discovery, keys, foreign keys,
  primitive-collection elements, query parameters. The lookup's facets are passed on. The plugin
  composes the JSON reader/writer itself, with both types known through the attribute's visitor:
  EF's own composition uses `MakeGenericType`, which it refuses under Native AOT, where a precompiled
  query still maps a value-object parameter at run time.
  - Not a replaced `IValueConverterSelector`: it maps the same, but a second library replacing the
    selector (as strongly-typed-id guides recommend) would take it away, and the resulting model
    error does not name the cause. Plugins are additive. This was first decided the other way on
    an unmeasured assumption, and reversed by the spike the same day.
  - Not a pre-convention: `Properties<T>()` needs the list of types up front, which means a scan.
  - An application that builds EF's internal service provider calls
    `AddEntityFrameworkTypeKit()` on that service collection instead.
- **Translators.** `ValueObjectMemberTranslatorPlugin` / `ValueObjectMethodCallTranslatorPlugin`
  are registered by the same options extension, so a consumer wires nothing else. A column
  operand is **re-typed** as the wrapped type, keeping the column's store type, rather than cast:
  `Convert` produced `o."Code"::text = 'ABC'`, which PostgreSQL discards but which is not a no-op
  everywhere (on SQL Server, a cast to `nvarchar(max)` can stop an index seek). Any other operand
  is still converted. The method translator acts only on a method whose signature is the unwrap,
  from a value object (or its `Nullable`) to what it wraps: the attribute is public, and a
  hand-marked `Length(this ProbeCode)` was translated as the column (`o."Code" = '3'`). Any other
  method carrying it is left to EF, which refuses the call.
  - **The element of a primitive collection is converted, not re-typed (decided 2026-09-28).** EF
    infers a collection's element mapping from the columns that read its table expression (`unnest`,
    `json_each`, `OPENJSON`, `VALUES`), and a re-typed element column handed it the wrapped type's
    mapping for a collection of value objects: `o.Tags.Any(t => t.Value == "x")` and
    `ids.Any(i => i.Value == o.Id.Value)` threw during translation on PostgreSQL and SQLite. EF 10's
    `ColumnExpression` no longer knows its table, so the element column is recognised by the name
    every relational provider gives it, `value`; the name only chooses between two correct
    translations, and a table column named `Value` gets the cast (`t.value::text = 'x'`).
- **Keys over an integer are generated on add (decided 2026-09-28, decision 23).** EF's value
  generation convention reads the property's CLR type, so a value-object key was never generated: an
  entity added without one was stored as 0, and switching an `int` key to a value object dropped its
  identity column. A convention plugin (first among the model-finalizing conventions, so a provider's
  identity strategy sees it) marks a single-column, non-foreign key over `int`, `long` or `short` as
  generated on add, at convention precedence. A key over a `Guid` is left alone: the application
  assigns `OrderId.New()`, and a generated key that is already set makes EF take a new entity reached
  through a navigation for an existing one.
- **Conventions for the wrapped type do not reach a value object.** `ConfigureConventions`
  (`Properties<decimal>()`) matches the property's CLR type; the README says to configure the value
  object (`Properties<Amount>()`). Measured 2026-09-28 for precision, maximum length, column type and
  an enum conversion.
- **Stored JSON (decided 2026-09-27).** The generated JSON converter applies `Create` (§5), which
  is right for input and wrong for JSON the application stored itself (an event store, a document
  column): a rule added later would make old documents unreadable. `StoredJsonConverterFactory`, in
  the base package because it is a System.Text.Json concern, is the opt-in for such a store's
  `JsonSerializerOptions`. It hands out the generated converter in a mode only it can create
  (a private constructor), so stored documents are read in exactly the format they were written
  in, dictionary keys included. SECURITY.md lists it beside the EF satellite as the two
  validation-free paths.
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
- **Entry points.** `ValueObjectAspect` introduces three private helpers on every value
  object, and they are the only way the JSON, parsing and type-converter aspects create an
  instance: `__FromJson` (a refusal throws `JsonException`), `__FromText` (`FormatException`) and
  `__TryFromText` (`false`). A plain value object constructs directly; a validated one calls
  `Create` through `GeneratedFactories` in the base package. The exception is the explicit
  `IValueObjectMaterializer.Materialize`, by contract. `MinValue`/`MaxValue` are only generated for
  a plain value object.
- **Declarations that cannot be generated** are errors, so no type is left half-generated:
  CMTK1000/1001/1002 (not `partial`, not a record, a struct not `readonly`), CMTK1003 (more than one
  marker), CMTK1004 (a validated marker whose first type argument is another type), CMTK1005 (a
  generic value object, one that derives from another value object, a wrapped type that is not
  a class, struct or enum, or one that is a value object: itself, one that reaches it again, or
  any other, see below), CMTK1006 (a record class that is not `sealed`: a derived record compares
  equal only to its own type, which is not value equality), CMTK1007 (the name of the
  `GetValue`/`ValueOrNull` class is taken, see below), CMTK1008 (a comparison member beside the
  seam, see below), CMTK1009 (a hand-written constructor, see below).
- **No hand-written constructor (decided 2026-09-27).** A value object's one constructor is the
  generated private one, and a hand-written instance constructor is CMTK1009. The implementation
  aspect answers it before introducing its own, since it reads only its target: `Constructors` minus
  the implicitly declared ones, measured to catch every form (the generated one's signature, any
  other, one chained to the generated one, a struct's `X()`, a record's copy constructor, a
  positional record's parameter list) and to leave the compiler's own and a static constructor
  alone. The type's own members can still call the private constructor, as `Create` must: that is
  the one trusted seam into a validated value object, stated on `IValidatedValue` and in SECURITY.md.
- **`Revalidate()` (decided 2026-09-28)** on a validated value object returns `Create(Value)`: today's
  rules applied to an instance that was rebuilt without them, by the EF satellite or
  `StoredJsonConverterFactory`. It is how an application finds the stored values a rule added later
  refuses, which the validation-free read path otherwise hides by design. It returns what `Create`
  returns, normalisation included, and a type that declares its own keeps it.
- **The companion class** holding `GetValue()`/`ValueOrNull()` sits at namespace level, as
  extension methods must, and is named after the whole nesting chain: `Order.Id` gets
  `OrderIdExtensions`. Named after the value object alone, `Order.Id` and `Customer.Id` both asked
  for `IdExtensions` and Metalama crashed (LAMA0001). A name that a declared type or another value
  object's companion already has is CMTK1007, naming it.
- **NodaTime.** Only `ValueObjectJsonAspect` handles NodaTime types, and it must not refer to
  NodaTime at compile time, which would force NodaTime on every consumer. It resolves the types
  with `TypeFactory.TryGetType` from the consumer's compilation, compares them by symbol, and uses
  NodaTime's converters (values and dictionary keys) only when
  NodaTime.Serialization.SystemTextJson is referenced too, and only where the options have no
  converter of their own for the type, which wins as it does for the wrapped type. Parsing reaches
  NodaTime types through their `[TypeConverter]` (`TypeDescriptor.RegisterType` and
  `GetConverterFromRegisteredType`, the trim-safe lookup).
- **Explicit `IParsable`.** `bool` and `char` implement their parsing interfaces explicitly, so the
  generated code calls the wrapped type's `TryParse` through `GeneratedParsing`, whose constrained
  type parameters reach an explicit implementation. `Parse` is `TryParse` and a throw (below).
- **Parsing culture (decided 2026-09-27).** In the generated `Parse`/`TryParse` a null
  `IFormatProvider` means the invariant culture, unlike the BCL, because the generated `ToString()`
  is invariant: `Parse(x.ToString(), null)` must round-trip, and with the current culture it parsed
  "1.5" as 15 in de-DE. A provider that is given is used as given. The type converter already did
  this.
- **Formatting culture (decided 2026-09-27),** the counterpart: a null provider means the invariant
  culture in the generated `ToString(format, provider)`, both `TryFormat`s and `IConvertible`.
  Interpolation, `string.Format` and `Convert.ToString` pass null, and with the current culture
  `$"{amount}"` was "1,5" in de-DE while `amount.ToString()` was "1.5". Display text in a culture
  passes it: `string.Create(culture, $"{amount}")`.
- **The wrapped value's JSON contract** comes from `GeneratedJson.TypeInfo<T>(options, builtIn)`
  (decided 2026-09-27, §11): the options' own contract when their resolver has one, so a host's
  number handling and converters apply, and otherwise made in the serializer's order (a converter on
  the options, the type's `[JsonConverter]`, the built-in converter the aspect names by its property
  type). A source-generated context never sees what a value object wraps, and serializing through
  the options needs reflection. A dictionary key goes through the same contract's converter.
- **The bytes are the wrapped type's (decided 2026-09-28, decision 26).** Every wrapped-type family
  writes and reads its values and keys through `GeneratedJsonPlan<T>`, the converter the options have
  for `T` or else the built-in (or NodaTime's) one: exactly what the serializer writes for a `T`, so a
  host's converter, number handling, enum converter and `DictionaryKeyPolicy` apply, and swapping a
  primitive for a value object changes no JSON. The earlier per-family readers and writers wrote
  `TimeOnly` as `"13:45:30.0000000"` and `DateTime` keys with seven fractional digits, ignored a
  host's converter for `Guid`, `DateTime` and integer keys, and skipped the key policy.
  `JsonParityTests` compares a value object with its wrapped type for 14 families under five option
  sets, as a value, a property and a key.
- **The wrapped value is written and read by its converter directly** (decided 2026-09-28), wherever
  that is what the serializer would do: a value converter, and, for a number type only, no number
  handling that changes the output (`WriteAsString`, named literals) or, for a string token, the
  input. Otherwise through the serializer, which alone applies number handling. A nested
  `JsonSerializer.Serialize`/`Deserialize` per value cost 35–40% (measured). The plan is kept per
  converter instance for the options it last saw; a static plan per wrapped type cost 8% on writes.
  The enum's built-in converter is made once per plan: made per call it allocated 4.7 KB per value.
- **Formatting and comparison never box** (decided 2026-09-28). The wrapped type's `ToString`,
  `TryFormat` and UTF-8 `TryFormat` are called through `GeneratedFormatting`, a constrained generic
  call that also reaches an explicit implementation, and comparison through `Comparer<T>.Default`,
  which the JIT specialises per type and, for an enum, compares the underlying values. Casting the
  value to the interface boxed it; an optimising JIT hid that for some types and not for others
  (enums), so the allocation tests run on a Debug build, where it never does.
- **JSON reads never parse by rules of their own.** A value and a key are read by the plan's
  converter, which accepts exactly what the serializer accepts for the wrapped type. A `Parse` in
  the generated code once let `FormatException` escape, which a minimal API answers with 500 rather
  than 400, and read number keys with `NumberStyles.Any` ("1,000" as 1000).
- **What the wrapped type cannot read is refused without quoting it (decided 2026-09-28, decision
  27).** The BCL's and NodaTime's parse messages quote the input ("The input string 'SECRET' was not
  in a correct format."), and a malformed NodaTime JSON value carried it in an inner exception. A
  generated `Parse` is the wrapped type's `TryParse` and a `FormatException` naming the value object
  and the wrapped type; where there is no `TryParse` (a static `Parse`, a string constructor, a type
  converter) the exception is replaced, not wrapped; a JSON read throws `JsonException` the same way.
  An overflow is a `FormatException` too. `UnreadableInputTests` feeds a secret through every strategy
  and entry point and searches the whole exception chain. Out of reach: `JsonException.Path`, which
  the serializer fills with an unreadable dictionary key, and ASP.NET's own binding messages.
- **`Uri` parses relative or absolute (decided 2026-09-28)**, as JSON and `UriTypeConverter` read it:
  `new Uri(s)` made "/orders/7" a `file://` URI on Unix and threw for "orders/7".
- **A hand-written `ToString()` is a seam (decided 2026-09-28, decision 28).** It was replaced
  silently, so a value object that hid a secret printed it. It is kept, and then no formatting
  interface (`IFormattable`, `ISpanFormattable`, `IUtf8SpanFormattable`) is generated, since
  interpolation would reach those first; the type converter and JSON still write the wrapped value.
- **More declarations refused (decided 2026-09-28).** Each failed as a Metalama error naming nothing
  the user wrote, or not at all:
  - CMTK1010: a value object declared in more than one `partial` part. Metalama 2026.1 (and 2027.0
    preview) writes the override of the record's synthesized `ToString` into every part (CS0111 as
    LAMA0611); no other advice overrides it (LAMA0041, LAMA0509, LAMA0500 measured). A part a source
    generator adds is not in `Sources` and does not count. The repro for an upstream report is in the
    phase 10 notes below.
  - CMTK1011: a hand-written member or attribute the generators introduce (`From`, `Value`,
    `Parse`/`TryParse`, `MinValue`/`MaxValue`, `[JsonConverter]`, `[TypeConverter]`, the interfaces),
    checked before anything is introduced, as CMTK1009 is; the seams (`TryFrom`, `FromKnownGood`,
    `Revalidate`, `CompareTo(TSelf)`, `ToString()`) stay allowed. Some of these were introduced with
    `OverrideStrategy.Ignore` and kept silently: a hand-written `Parse` became the binding entry
    point. `Create` implemented explicitly is CMTK1011 too, since the generated code calls
    `{Type}.Create`. Since phase 11 also a hand-written `Equals(TSelf)`/`GetHashCode()`/
    `IEquatable<TSelf>.Equals`, `PrintMembers` without `ToString()`, an explicit implementation of an
    interface the generators implement (CMTK1008 for the comparison interfaces), and a declared
    `IConvertible`.
  - `Create` is declared once, public: an explicit one beside it is CMTK1011, since a generic
    `T.Create` would reach a second rule set. A base record's members count as the value object's
    own: its `sealed` `ToString()` is the seam; an unsealed one, its `PrintMembers`, its explicit
    implementations of the generated interfaces and an `IConvertible` on it are CMTK1011, or CMTK1008
    for comparison. Members the compiler generated on a record from another assembly do not count.
  - CMTK1012: state besides the wrapped value, an instance field, auto-property, `required` member or
    field-like event, declared or inherited. JSON, parsing and the materializer carried the wrapped
    value alone while equality compared the rest. Computed and static members stay allowed. In a
    referenced assembly a base's private fields are invisible, so a writable property counts there.
  - CMTK1005 adds a generic wrapped type, tuples included (its equality is not value equality: a
    `List<T>` compares by reference), a value object named `Value` (CS0542), and a `file`-local one
    (LAMA0001, which failed the whole project).
- **A `DateTime` is UTC on the wire (decided 2026-09-27).** Written and read, value and key, through
  `GeneratedJson.AsUtc`: a `Local` value converts, and an `Unspecified` one (an offset-less string, a
  column without a time zone) is taken as UTC with its digits kept. `ToUniversalTime()` read it as
  server-local, so 12:00 became 10:00Z on a server at +02:00 and 12:00Z on CI. The guards switch the
  process to a +05:30 zone, since CI runs in UTC.
- **`From(null)` throws** `ArgumentNullException` for a reference-type wrapped value, as
  `Option.Some(null)` does. It wrapped a null that `Value` promises it never holds.
- **Enums** parse by name through `Enum.TryParse`, with a `FormatException` for an unknown name.
  `Convert.ChangeType` cannot make an enum from a string, so the former `IConvertible` strategy
  threw for every input while the type advertised `IParsable`. Nothing else that is convertible
  lacks `ISpanParsable`, so that strategy is gone.
- **String comparison is ordinal**, so ordering agrees with the record's equality. Culture
  comparison sorts "a" before "B" and ignores a zero-width space, and a `SortedSet` then dropped a
  value a `HashSet` kept.
- **A nullable wrapped type is CMTK1005.** The markers' `notnull` constraint is only a warning
  (CS8714), and `IValue<int?>` then failed inside the generated code as LAMA0611/0612.
- **A value object never wraps a value object (decided 2026-09-27).** CMTK1005, answered in the
  fabric because it reads the wrapped types (see "An aspect never scans its namespace" below).
  - **A cycle** (`sealed partial record SelfWrap : IValue<SelfWrap>`, or A wraps B wraps A) has no
    finite form: the fallback JSON converter serializes the wrapped value through the options,
    which is its own converter again, and a schema walk never ends. As a record class it compiled
    without a word (spikes/OpenApiSchemas finding 16), as a struct it failed inside the generated
    code (CS0523 as LAMA0611). The message names the chain: `it wraps itself (A -> B -> A)`.
  - **Any other value object** (`Outer : IValue<OrderId>`) terminates, but its surface depended on
    where the inner one was declared. Over one from a referenced project it got every interface;
    over one from its own project it silently lacked `IParsable`, `IComparable`, `ISpanFormattable`,
    `IConvertible`, `IMinMaxValue` and the type converter, and sorting it threw: the aspects of one
    layer do not see what their sibling instances introduce (measured 2026-09-27). The message
    names what to wrap instead (`wrap 'Guid' instead`); a validated value object applies the inner
    one's rules in its `Create`. Refused rather than made to work: a surface that follows the
    project layout is the silent failure this repo exists to prevent, and lifting a refusal later
    breaks nobody, while adding one after 1.0.0 would.
  - The satellites keep their own refusal (§7): a value object declared by hand, with the
    interfaces and `GeneratedValueObjectAttribute<,>` the satellites read (§3), can still wrap itself.
- **Every JSON key goes through the wrapped type's own converter**, which knows the type's key
  format where it has one (an enum by name, a `Uri` as its text) and throws `NotSupportedException`
  where it has none. Writing the serialized value as the property name gave a `Uri` key quotes inside
  its quotes and an enum key its number, and neither read back.
- **A record class sorts null first.** Its generated `CompareTo` and comparison operators take a
  nullable operand, as `IComparable<T>.CompareTo(T?)` and the record's own `==` do, and treat null
  as smallest, where they threw `NullReferenceException`.
- **Ordering follows the wrapped type.** `CompareTo` delegates to the wrapped type's, ordinal for a
  string, so it agrees with equality exactly where the wrapped type's own does. A custom wrapped
  type whose `CompareTo` returns 0 for values its `Equals` tells apart makes a `SortedSet` drop one,
  of the value object and of the wrapped type alike (measured 2026-09-27). That is the wrapped
  type's contract to keep, and the marker interfaces' documentation says so.
- **One comparison seam (decided 2026-09-27).** A hand-written `CompareTo(TSelf)` is kept, and the
  object overload, the operators and the interfaces are derived from it, as `TryFrom` is for the
  factories. Any other hand-written comparison member is CMTK1008: an operator failed the aspect
  (LAMA0500, naming no fix), and an object overload was kept silently beside a generated generic
  one that need not agree with it.
- **An aspect never scans its namespace (decided 2026-09-27,
  [spikes/ConcurrentNamespaceTypes](../spikes/ConcurrentNamespaceTypes/README.md)).** What owns
  a companion name is answered in the fabric, before any introduction, and reaches the aspect
  through its constructor and the aspect state. Anything that has to look beyond the aspect's own
  target goes the same way.
- **Never look up the code model by name (decided 2026-09-28, same spike).** Compile-time code
  finds a type or member by enumerating a collection and comparing names: no `OfName`, no
  `OfExactSignature`/`OfCompatibleSignature`, no string indexer on `Fields`/`Properties`/`Events`.
  Metalama builds a collection's by-name index lazily and without a lock, and runs the fabric's
  factory, like the instances of one aspect layer, in parallel on one code model. An `OfName` that
  overlapped another caller completing the collection returned nothing for a declared
  `TakenNameExtensions`, and the build compiled a second one (CS0260) or Metalama failed the aspect
  (LAMA0531). In the fabric that missed CMTK1007 in 13 of 300 builds; the move there on 2026-09-27
  had measured 0 in 48 by chance and blamed sibling introductions, which was wrong. With concurrent
  build off: 0 in 200. Enumerating, which takes the collection's lock: 0 in 600, and 4 in 150
  again with it reverted. The race has no deterministic test, so `CodeModelLookupTests`
  (conventions) fails on any such lookup in `.Generators`.
- **Metalama 2026.1.** Aspect state uses `IDurableRef`, which exists in 2026.1. `[Durable]` on the
  `_value` template placeholder is 2027.0-only and stays out until the upgrade (decision 4). Build
  each aspect on 2026.1 as it lands, and use no 2027.0-only API.
- **`AspectOrder`** lives in `CodoMetis.TypeKit.Generators`. Open point: ordering against a
  consumer's own aspects (fabric spike D).
- **Not in scope:** a `Try`/`Unit` type (§9 non-goals), clock-type analyzers, text-guarding types,
  and decimal JSON converters (no aspect needs them, checked in phase 3).

## 6. Test strategy

1. **Behaviour.** Every public member of `Option`/`Result` and every generated member has a
   behaviour test. Generated members are tested against woven probe types, never against code
   written by hand in the test assembly.
2. **Generated surface.** A small tool reflects over the woven probe types. For each value object
   it renders the public members, implemented interfaces and attributes as sorted text, which is
   committed and asserted. An aspect change that adds or loses a member then shows up in review
   instead of shipping silently. Once 1.0.0 ships, package validation against the last release
   guards the hand-written surface too.
3. **Generated SQL.** `ToQueryString()` snapshots for `.Value`, `GetValue`, `ValueOrNull`,
   `StartsWith`, equality, and `Contains` over a list of ids.
4. **OpenAPI.** A probe host's emitted document, asserted per shape against a control that uses
   the wrapped type directly, so the tests follow what ASP.NET publishes for that type (§7).
5. **Performance.** Allocation tests hold the generated members to what the wrapped type allocates,
   and `benchmarks/` measures time against the wrapped types. The benchmarks are not a gate: they are
   run before a release and when a template changes.
6. **README samples** (2026-09-28). Every C# block of the READMEs is a region in
   `test/CodoMetis.TypeKit.Samples`, which references the shipping projects as a consumer does, with
   the analyzer on. `ReadmeSampleTests` requires each block to equal exactly one region and each region
   to be shown, so a sample that stops compiling fails the build and an edit on one side fails the test.
   An output stated in a comment is asserted against the README's own comment, found by the expression
   the test evaluates. A pre-release audit had found samples that did not compile (a bare fault as a
   `Result<T, TError>`, `quantity.Value;` as a statement, an ellipsis as a lambda body), CMTK0006 on the
   EF entity sample, and SQL the query does not produce; the consumer smoke test had found the first
   once before (§9). Seeded: a README or sample edited alone, the bare fault back in both, the committed
   READMEs, a changed SQL, JSON or string output, an opt-out on a compiled block or without a reason, an
   unlabelled fence, and either parser finding nothing each fail their test.

## 7. OpenAPI

**Decided 2026-09-27, measured in [spikes/OpenApiSchemas](../spikes/OpenApiSchemas/README.md)**
with Microsoft.AspNetCore.OpenApi 10.0.12. An earlier measurement had found that a schema
transformer never reached a type that only appears as a property, and repaired such types in a
document transformer. It does not hold in 10.0.12: one schema transformer reaches every JSON
position, and the change lands in the hoisted component.

- **The schema is the wrapped type's, as ASP.NET publishes it for this host**, asked of ASP.NET
  through `context.GetOrCreateSchemaAsync(typeof(T))`. It therefore follows the host's JSON options
  (number handling, enum converter) exactly as the generated converter does, and the document's
  other transformers (a NodaTime transformer describes a value object wrapping `Instant` too). No
  format table of our own.
- **Keywords are filled in, never the object replaced**: the transformer receives the instance ASP.NET
  hoists into the value object's own component, so `OrderId` stays a named component. What the
  value object's schema already says is kept. ASP.NET's `Metadata` (the reference id) and the
  schema-identity keywords are never copied; copying `Metadata` made every use a reference to the
  wrapped type's component. A keyword is assigned only when the wrapped schema has one.
- **Containers**: ASP.NET drops `items`/`additionalProperties` for a converter-backed element before
  any transformer runs; the value object's own schema is put back, only where missing.
- **Parameters** bound through the generated `TryParse` reach the transformer as `string`, ASP.NET's
  placeholder for any parsable type, which the wrapped type's schema replaces. Minimal APIs name the
  value object in the parameter's `Type`, MVC in `ModelMetadata.ModelType`; the parameter descriptor
  names the container for an MVC `[FromQuery]` object and is not used. Each parameter then publishes
  exactly what a parameter of the wrapped type publishes.
- **A value object that reaches itself** through what it wraps is an `InvalidOperationException`
  naming the chain, not a stack overflow.
- **Source-generated JSON** (§11): ASP.NET builds the wrapped type's schema from the host's JSON
  contract for it, and a source-generated context has none for a type the host never serializes.
  The transformer then refuses with an `InvalidOperationException` naming the value object and the
  `[JsonSerializable(typeof(Guid))]` to add, rather than the serializer's message about a Guid. A
  resolver of our own cannot be added: the host's options are read-only by then.
- **Component names (decided 2026-09-28, decision 24).** ASP.NET names a component after the type's
  simple name, so `Shop.Id` and `Stock.Id` shared one, and a stock id wrapping an `int` was documented
  as a uuid. `AddTypeKit()` wraps `CreateSchemaReferenceId`: a nested value object that got ASP.NET's
  default name is named after its nesting chain (`ShopId`, as its companion class is `ShopIdExtensions`);
  a name the host's own delegate chose, or `null` for an inlined value object, is kept.
- **Known gaps, documented rather than fixed:** a validation attribute on a value-object property
  lands in the shared component, as ASP.NET does for any referenced schema (rules belong in `Create`);
  a property absent from a request body stays `default`, since System.Text.Json calls no converter
  for it (`required`, `RespectRequiredConstructorParameters`); a minimal API answers a refused body
  with an empty 400; MVC's own binding message quotes the input, as it does for a `Guid`.
- **ASP0020** (decision 12): a minimal-API route parameter whose value object is declared in the
  same project fails the build, because the route analyzer reads the source before Metalama weaves
  `IParsable` in. Binding is correct at run time and in the request delegate generator. A
  `DiagnosticSuppressor` has no effect under Metalama's compiler, and Metalama's own suppression is
  scoped to the aspect's targets, so the package README documents the pragma instead.

Vogen 8.0.7 generates a schema transformer for Microsoft.AspNetCore.OpenApi too, from a table of
wrapped types (checked in its source at the tag, 2026-09-28; the 2026-09-27 note here said
Swashbuckle only). The README describes what the package does and claims nothing about other
libraries.

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

9. **EF mapping through an additive `IRelationalTypeMappingSourcePlugin`** (2026-09-27), not a
   replaced `IValueConverterSelector` or a pre-convention scan (§4, spikes/EfMapping).
10. **Stored JSON through `StoredJsonConverterFactory`** in the base package (2026-09-27, §4).
11. **OpenAPI through one schema transformer** that fills a value object's schema in from the
    wrapped type's, as ASP.NET publishes it (2026-09-27, §7, spikes/OpenApiSchemas), not a document
    transformer and not a format table.
12. **ASP0020 is documented, not suppressed** (2026-09-27, §7): the package cannot suppress it, and
    a blanket suppression would hide the check for every other type.
13. **The first release's rule set is CMTK0001–CMTK0009** (2026-09-28, §10; CMTK0009 added the same
    day, at Warning, for the calls that hand out a default). CMTK0004 ships at Error
    in 1.0.0, the one release where a new Error rule needs no major version; CMTK0003, CMTK0005 and
    CMTK0006 at Warning, CMTK0006 once its fix was measured; CMTK0007 at Info, reporting only a
    value that comes straight from a parameter.
15. **`Option`/`Result` are not wire types** (2026-09-27, §9): a `[JsonConverter]` on every exported
    struct of the base package throws `NotSupportedException` in both directions. Not a lossless
    converter, and not an analyzer.
16. **Native AOT wherever the platform allows it** (2026-09-27, §11): the run-time packages build with
    `IsAotCompatible`, the woven code is published with Native AOT in the smoke test, and a path that
    needs reflection or dynamic code is rewritten rather than annotated or suppressed. EF Core itself
    stays experimental under Native AOT; the satellite adds nothing to what EF requires.
17. **Run-time discovery reads `GeneratedValueObjectAttribute<TValueObject, T>`** (2026-09-27, §3, §11),
    which the generators put on every value object, not `GetInterfaces()`. Measured under Native AOT:
    the interface was gone from every value object reached through a property, and the OpenAPI
    satellite described each as `{}`. The attribute's constraints are the interfaces, so what a value
    object is has not changed; its `Accept` hands the two types to a visitor as type arguments, which
    is how the EF satellite makes a converter without `MakeGenericType`.
18. **One name per concept** (2026-09-27, before the first release, when renaming still costs nothing).
    - What a value object holds is *wrapped*: `GeneratedValueObjectAttribute.WrappedType` and
      `TranslatedAsWrappedValueAttribute`, not "underlying", and not `ValueType`, which in .NET means
      a struct and here returned `typeof(string)` for a string-backed value object.
    - A value object without rules is *plain*: `IPlainValueObject<TSelf, T>`, which has `From`. It was
      `IValueWrapper`, which named what every value object does.
    - The value object's own type parameter is `TSelf` on the self-referencing contracts, as in
      `IParsable<TSelf>`, and `TValueObject` where a helper or attribute names one. Never `TValue`,
      which `KnownGood` used for the value object while "value" means the wrapped value everywhere
      else.
    - Delegate parameters: `onSome`/`onNone`, `onSuccess`/`onError`, `selector`, `predicate`,
      `action`, as LINQ names them. They were `fnSome`, `map`, `fn`, `check` and `defaultProvider`.
    - Namespaces follow who uses a type (§1). The helpers only generated code calls moved to
      `CodoMetis.TypeKit.CompilerServices`, hidden from IntelliSense, and `Accepted` and `KnownGood`
      became one `GeneratedFactories` whose methods name the exception each entry point throws.
    - Internally: `ValueObjectAspect` (was the implementation aspect), `ValueObjectContractAspect`,
      `ValueObjectCompanionAspect`, `ValueObjectKind.Plain`.
19. **Concise, not implicit** (2026-09-27, §9). Markers where a target type exists, explicit type
    arguments where none does.
    - One word for success: `Result.Success(...)` beside `Result<…>.Success(...)`, the `Success`
      markers, `ResultState.Success` and `onSuccess`, as `Option.Some` sits beside `Option<T>`.
      `Result.Ok` was the one place that said it differently.
    - `Option.None()` returns a marker that converts to any `Option<T>`; `Option.None<T>()` stays for
      `var`. The analyzer forbids `default(Option<T>)`, so every None needed its type argument.
    - `MapError` on both shapes, for crossing layers, which needed
      `Match<Result<T, TNew>>(x => x, e => Result.Error(…))`; and `Bind(Func<T, Result<TError>>)`, a
      command after a query, which replaces the collapsing `Match(_ => Result.Ok(), e => e)`, a
      conversion wearing `Match`'s name.
    - `Option.ToResult(error)` keeps the value (`Result<T, TError>`); it returned `Result<TError>`,
      dropping it. `Coalesce` is `Or`, beside `OrDefault()` and `OrNull()`.
    - Not added: an implicit `T` → `Option<T>`, since a null would throw exactly where a reader
      expects `None`; a bare error → `Result<T, TError>` (the ambiguity above); a shipped
      `using static`. A lambda that returns a bare value and `Result.Error(...)` still needs its type
      argument, because C# infers a lambda's return type from its body alone; the README says so.

20. **net10.0 only** (2026-09-28). The satellites cannot go lower (EF Core 10 and
    Microsoft.AspNetCore.OpenApi 10 require it), the contracts use static abstract interface members,
    which rules out netstandard2.0, and .NET 8's support ends on 2026-11-10.
21. **The first release is 1.0.0** (2026-09-28), not 0.1.0. What the reasoning above says of "the
    first release" (CMTK0004 at Error, refusals that can be lifted later) holds for 1.0.0 unchanged.
    From then on the public API, the generated surface and the rule ids and severities follow
    Semantic Versioning, and package validation compares every pack with 1.0.0.
22. **Registration extensions live in the framework's namespaces** (2026-09-28): `UseTypeKit()` in
    `Microsoft.EntityFrameworkCore`, `AddEntityFrameworkTypeKit()` and `OpenApiOptions.AddTypeKit()` in
    `Microsoft.Extensions.DependencyInjection`, as EF's providers and `AddOpenApi` do, so the one line a
    host adds needs no `using` (the docs audit found every setup sample missing one). Types stay in
    `CodoMetis.TypeKit.*`. Moving them after 1.0.0 would break every host.
23. **A value-object key over an integer is generated on add; one over a `Guid` is assigned**
    (2026-09-28, §4). Parity with the wrapped key where only the database can make one, and the
    application's `OrderId.New()` where it can.
24. **Nested value objects are named after their nesting chain in the OpenAPI document**
    (2026-09-28, §7), as their companion classes are.
25. **The analyzer's Roslyn floor stays at 5.0.0** (2026-09-28), the compiler of the 10.0.1xx SDK band,
    which is serviced for all of .NET 10 and is what Linux distributions build. Measured: built on
    5.9.0 (SDK 10.0.4xx) and run by the 5.0.0 compiler, the analyzer is not loaded (CS9057, a warning)
    and `default(Option<int>)` compiles. `AnalyzerPackagingTests` reads the packed analyzer's
    references. The build-time `Microsoft.CodeAnalysis.Analyzers` follows the SDK (5.9.0).
26. **A value object's JSON is its wrapped type's, byte for byte** (2026-09-28, §5), under whatever
    options the application uses: swapping a primitive for a value object changes no wire format.
27. **No refusal quotes the input** (2026-09-28, §5), not even one the wrapped type raises: every
    generated `Parse`, type converter and JSON read replaces the wrapped type's exception with one that
    names the value object and the wrapped type.
28. **Seams are declared, everything else refused** (2026-09-28, §5): `TryFrom`, `FromKnownGood`,
    `Revalidate`, `CompareTo(TSelf)` and `ToString()` may be written by hand and are kept; any other
    member or attribute the generators introduce is CMTK1011, never kept silently. Since phase 11 so
    is an explicit implementation of an interface they implement, a hand-written equality, and
    `PrintMembers` without `ToString()`, declared or inherited from a base record, whose `sealed`
    `ToString()` is the seam; state besides the wrapped value is CMTK1012.
29. **Only `Result<TError>` converts to `bool`** (2026-09-28, §9). On a valued result the conversion
    said whether the operation succeeded where a reader expects the value: `if (await
    IsEmailTakenAsync(email))` over `Result<bool, DbFault>` took the branch for `Success(false)`. The
    same reason keeps a bare error from converting into one. Match it, or compare `State`.
30. **Asynchronous callbacks return `Task`** (2026-09-28, §9). `TapErrorAsync(Func<TError, Task>)` sits
    beside `Action<TError>`, as `TapAsync` already did. It ships in 1.0 because adding it later
    rebinds existing calls. No `ValueTask` overloads: beside the `Task` ones every `async` lambda
    becomes ambiguous (CS0121, measured).
31. **No extension on `object` or on an unconstrained `T`** (2026-09-28, §9): it is offered on every
    expression of every file that imports the namespace, and collides with anyone else's.
    `No_extension_member_extends_every_type` holds it.
32. **A value object holds its wrapped value alone, and its equality is that value's** (2026-09-28,
    §5). Everything generated (JSON, parsing, the type converter, the EF column, comparison) carries
    the wrapped value, so other state (CMTK1012), a hand-written equality or an explicit
    implementation beside a generated member (CMTK1011, CMTK1008) would make two views of one value
    disagree. Normalise in `Create` instead.
33. **The analyzers run on the code Metalama transformed** (2026-09-28, §10), through a
    `MetalamaTransformedCodeAnalyzer` item in the analyzer package's `buildTransitive` props. The
    namespace covers every rule, a future one included.
34. **Errors are fixed within a major version; Warnings may learn** (2026-09-28, §10). A new Error, or
    an Error that reports more, waits for a major version. A Warning or Info may report more forms in
    a minor version. The CMTK0003, CMTK0008 and CMTK0009 gaps the second review found were closed
    before 1.0.0 was tagged all the same, so the rules ship as complete as the review could make them.
35. **SQLite's autoincrement is the application's line, not the convention's** (2026-09-28, §4). EF's
    SQLite provider decides by the CLR type, and the only other way in is an annotation typed by the
    SQLite assembly: setting it means naming the provider or writing a foreign annotation into every
    other provider's model. The README documents `UseAutoincrement()`; `MigrationSnapshotTests` fails
    once EF fixes the check, which is when the note can go.
36. **A nullable value object admits null in the OpenAPI document wherever it appears** (2026-09-28,
    §7): as a property, as a container's element (`oneOf: [null, component]`, ASP.NET's own form),
    and inlined (null added to `type`, or `oneOf` around an inlined enum).
37. **A refusal's message is its landing page** (2026-09-28): an exception has no help link, so every
    deliberate run-time refusal says what to do instead and ends with a link to its subsection of the
    base README's "Why does this throw?", and every generator build error with one to the Build errors
    table. The links are held to the README's headings by tests. Nothing of the input is added.

Still open:

14. **context7.json.** The sibling repos register one. Add it once the repo is public.

## 9. Option and Result

`Result<T,TError>`, `Result<TError>` and `Option<T>`, with the `Option.None()`, `Result.Success()` and
`Result.Error()` markers (decision 19), ship in `CodoMetis.TypeKit`. The value-object contracts
depend on them: `IValidatedValue.Create` returns a `Result`, and the generated `TryFrom` returns an
`Option`.

**Non-goals.** No Either, no Validation applicative, no effect or IO types, no higher-kinded type
emulation, no immutable collections, no `Try` or `Unit`. Anything that turns this into a second
LanguageExt is out.

**Shape, decided 2026-09-27.**
- **`Result<T, TError>`, value first.** Error-first (`Either e a`) exists in Haskell and Scala
  only because a generic type there can be filled in from the left alone, which C# cannot do.
  Rust, F#, Swift, DotNext, CSharpFunctionalExtensions and the csharplang unions design note all
  put the value first. `IValidatedValue<TSelf, T, TFault>` does too.
- **`readonly record struct`, non-positional, private fields.** The synthesized equality is
  correct (`State` included). The synthesized `ToString` prints only public members:
  `Option { }` and `Result { State = Error }`. So it never prints a value, in line with
  `FromKnownGood`'s message. **Never make these types positional:** positional parameters become public
  properties, which reintroduces `.Value` and puts the value into `ToString`.
- **The markers** (decision 19): `Option.None()`, `Result.Success()`, `Result.Success(x)` and
  `Result.Error(e)` return a small struct that converts implicitly to whatever `Option`/`Result`
  the target is, so a method returns one without spelling out type arguments. They keep their
  content internal too, so they print `Success { }` and `Error { }`. Only the implicit conversions
  read it. Both shapes accept both result markers: `Result<TError>` converts from `Result.Error(e)`
  as well as from a bare error, so a method can `return Result.Error(fault);` whichever shape it
  returns. No combinator takes a marker-returning lambda: `Bind(x => Result.Success(x))` was `Map`
  under another name, and is a compile error now.
- **`Result<T, TError>` converts from a bare value, never from a bare error** (measured
  2026-09-27). With both conversions, `Result<long, int> r = 5;` compiles and is an **error**:
  `int` is the more specific source type, so C# picks the error conversion. The READMEs had shown
  `return Fault.X;` in valued methods, which does not compile; the consumer smoke test found it.
- **`Option.Some(null)` throws.** `notnull` is an annotation the runtime does not enforce, and a
  `Some` over null reported a value it could not hand out. `Map` and the zips go through `Some`,
  so a selector that returns null throws too.
- **So do `Result`'s factories.** Both shapes and both markers constrain their content to
  `notnull`, and `Success(null)`/`Error(null)`/`Result.Success(null)`/`Result.Error(null)` throw
  `ArgumentNullException`: a result over null handed it out of `TryGetValue`/`TryGetError` despite
  `[NotNullWhen]`. `Map`, `MapError`, `Bind` and the conversions go through them.
- **Every delegate is checked before a branch is picked**, in `Option`, both `Result` shapes and
  their extensions: a null for the branch not taken passed until the other outcome first arrived.
  `NullDelegateTests` holds every delegate parameter of the assembly to a case run on both branches.
- **`Result<TError>.Match` hands the error to its error branch.** The only overload took a
  parameterless `onError`, so `TryGetError` was the only way to the error. The parameterless one
  stays, for a branch that does not need it.
- **`[DebuggerDisplay]`** on `Option` and both `Result` shapes shows the content in the debugger,
  where it is what someone stepping through wants to see. The debugger is not a log.
- **`Result<TError>` has no `AsEnumerable`.** A sequence of zero or one units says no more than
  the `bool` conversion.
- **Pipelines (decided 2026-09-28).** `MapAsync`/`BindAsync` take an asynchronous selector on a
  result, and every combinator of both shapes continues a `Task<Result<…>>` (`MapAsync`, `BindAsync`,
  `MapErrorAsync`, `TapAsync`), so a chain is awaited once, at its end; a completeness test holds
  every combinator to a continuation. `Match` ends a chain after that `await`. Each continuation
  checks its delegate before awaiting, so a null one is reported even when the pending result fails.
  `Zip` combines two to six results, `Sequence` and `Traverse` a sequence of them. All stop at the
  first error, in argument or sequence order: collecting errors is the validation applicative the
  non-goals exclude. A zip still inspects every argument, so an uninitialized one after an error
  throws. Callbacks return `Task` (decision 30): `TapAsync` and `TapErrorAsync` take a
  `Func<…, Task>` beside the `Action`, so an `async` lambda is awaited; a `ValueTask` one needs
  `.AsTask()`.

**Decided 2026-09-27: an uninitialized `Result` throws.**
- A `default` result is `State == Uninitialized`. If the branching members tested only
  `State == Success`, it would take the **error** branch with `default(TError)`.
- For an enum fault that is the first member (e.g. `Blank`): a plausible, wrong reason. For a
  reference-type error it is `null`, despite `[NotNullWhen(false)]`.
- The analyzer blocks `default` in source, but array elements, class fields (CMTK0005/0006) and
  reflection still produce such instances.
- **Decision:** every member that picks a branch (`Match`, `Map`, `Bind`, `Tap`, `TapAsync`,
  `TapErrorAsync`, `TryGetValue`/`TryGetError`, `AsEnumerable`, the `bool` conversion of
  `Result<TError>`) throws
  `InvalidOperationException` on `Uninitialized`. That is a loud failure instead of a fabricated
  fault. `State`, equality and `ToString` stay safe to call.

**Decided 2026-09-27: `Option`/`Result` are not wire types.**
- Measured: with private state and no converter, System.Text.Json wrote `{}` for a `Some` and read
  it back as `None`, and wrote `{"State":1}` for a success and read it back as `Uninitialized`.
  Nothing raised.
- **Decision:** `NotWireTypeJsonConverterFactory`, on `Option<T>`, both `Result` shapes and the
  `Success`/`Success<T>`/`Error<T>` markers, throws `NotSupportedException` from `Read` and `Write`,
  naming the type and the alternative and never the content. `HandleNull` is on, so a JSON `null`
  gets the same message, and a dictionary key does too. That is what the serializer itself does for
  `System.Type`. It hands every type one converter typed `object`, which the serializer wraps: a
  converter per type needed `MakeGenericType`, and under Native AOT that failed for these structs
  with the runtime's message instead of this one (§11).
- A serialized shape says absent with `T?`; `ToOption()`/`OrNull()` convert at the boundary; a
  result is matched to a response or a document. An application that wants a wire format registers
  its own converter on its options, which takes precedence over the type's attribute, in a
  source-generated context too (measured).
- **Not a lossless converter** (`Some(x)` as `x`, `None` as `null`): its bytes are identical to
  `T?`, so it would only move the CLR type onto the shape the design keeps it off, and a `Result`
  has no sensible wire shape at all.
- **Not an analyzer:** whether a type is serialized is a property of the application's roots and
  options, which the package cannot see. An application can add a shape test of its own.
- **Known gap:** the serializer calls no converter for a property that is absent from the document,
  so it stays `default`. `required` or `RespectRequiredConstructorParameters` closes it, and the
  README says so.
- Completeness: `NotWireTypeTests` walks every exported struct of the assembly, so a struct added
  later refuses too, or is exempted on purpose.

**Why not an existing package** (checked 2026-09-26). The criteria: no public `.Value`/`.Error`,
a generic `TError`, `default` is not success, maintained.
- **Funcky 3.6.0** is closest (`[NonDefaultable]` enforced by an analyzer, no `.Value`), but its
  `Result<T>` fixes the error type to `Exception`.
- **nlkl/Optional** has the right Option shape but no Result, and no release since 2018.
- **CSharpFunctionalExtensions, DotNext and FluentResults** expose a throwing `.Value`.
- **ErrorOr, Remora.Results and Ardalis.Result** expose an unguarded one. With ErrorOr,
  CSharpFunctionalExtensions and Remora, `default(Result<…>)` reads as success.
- **JSON** (checked 2026-09-27): CSharpFunctionalExtensions 3.7.0 ships opt-in converters that write
  a `Result` as a DTO with `IsSuccess`/`Error`/`Value`, for passing outcomes between services;
  LanguageExt (v5 still beta) ships none; ErrorOr, OneOf and FluentResults ship none and serialize
  their public properties as they are. None refuses.

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
- **Measured 2026-09-27 (SDK 10.0.401),** packed and consumed from a local feed:
  - A project that references only `CodoMetis.TypeKit` gets CMTK0001 and CMTK0002, and so does a
    project that reaches it only through another project.
  - With the default `exclude="Build,Analyzers"` on the dependency, the analyzer **still** loads,
    because the SDK takes analyzers from every package in the restore graph. `PrivateAssets="none"`
    is kept so the dependency says what is intended instead of relying on that.
  - What does drop it: a project reference with `ReferenceOutputAssembly="false"` emits **no
    dependency at all**. The reference is therefore an ordinary one with `Private="false"`.
- **Guards:** `AnalyzerPackagingTests` packs both packages and reads the nuspec and the package
  layout. The consumer smoke test (phase 6) asserts CMTK0001 fires, directly and transitively.

Attributes stay in `CodoMetis.TypeKit`. The analyzer resolves them and the interfaces with
`GetTypeByMetadataName` and compares symbols (§3), so it does not reference the base package.
Inside the analyzer, rules that react to attributes are kept apart from value-object rules, so the
split stays mechanical if a generic analyzer package is ever wanted.

**Scope.** Code that compiles but is wrong about value objects, `Result`/`Option` and types that
forbid `default`. General linting (clock types, style) stays out, since Meziantou and Roslynator
exist.

| Id | Rule | Severity | Status |
|---|---|---|---|
| CMTK0001 | No `default`/`default(T)`/`new()`/`new T()` of a value object or a `[RequireCustomInitialization]` type. An operand of `==`/`!=`, or an argument of a call whose name has the word `Equal`, `Equals` or `Compare` or ends in `Be` (`ThrowIfEqual`, `Assert.NotEqual`, `ShouldNotBe`), is exempt: a guard check is the only defence against the defaults the rule cannot see. So is a `default` assigned to an `out` parameter of a method returning `bool`, the Try pattern. Where a sibling branch does not bind (same-project value objects), the target type is taken from the enclosing conditional, switch arm or collection | Error | shipped in 1.0.0 |
| CMTK0002 | Type implements `IValue<>`/`IValidatedValue<,,>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so it is never woven | Error | shipped in 1.0.0 |
| CMTK0003 | `Result`/`Option` a call returns, dropped by an expression statement, awaited or not, or a `Task` of one converted to a plain `Task` (`Task Cancel() => CancelAsync();`); `_ =` opts out. `Tap`, `TapAsync`, `TapError` and `TapErrorAsync` on a stored result or task are exempt, since they return their receiver. Also a collection of results the statement made (an array, `ICollection<T>` or `IReadOnlyCollection<T>`, such as `await Task.WhenAll(ids.Select(orders.CancelAsync))`), and a method group of a `Task<Result>` method converted to a delegate returning `Task`; tasks held in variables are silent, and so is a lazy `IEnumerable<T>`, where nothing has run. CA1806 can only enforce this per method name, not per return type | Warning | shipped in 1.0.0 |
| CMTK0004 | `IValueObjectMaterializer<,>.Materialize`, `ValueObjectConverter<,>.Materialize` or a hand-written value object's public `Materialize`, called or referenced (expression trees included). The satellite's own call is compiled in the satellite; EF's compiled model is generated code, whose diagnostics are not reported (measured: reported, it is six errors in the smoke test's AOT consumer). `StoredJsonConverterFactory` goes through `IStoredJsonConverterSource`, not `Materialize` | Error | shipped in 1.0.0 |
| CMTK0005 | `new T[n]`, `stackalloc T[n]`, `GC.AllocateUninitializedArray/AllocateArray<T>` or `Array.Resize` of a no-default struct: every new slot starts as `default`. A constant length of zero (for `Array.Resize` and the `GC` methods too) or initial elements are silent | Warning | shipped in 1.0.0 |
| CMTK0006 | Field or auto-property of a no-default struct type in a class that no initializer, `required` or constructor sets; a non-public parameterless constructor (EF, serializers) and a record's copy constructor are exempt. Measured 2026-09-28 on the repository's EF entities and request types: 12 of 12 reports on settable properties of a type with only the implicit constructor, each a real way to a default. `required`, the fix the message names where it compiles, works with EF's compiled model, precompiled queries and Native AOT (the smoke test rerun with it). A `[SetsRequiredMembers]` constructor must assign the `required` members it promises | Warning | shipped in 1.0.0 |
| CMTK0007 | `FromKnownGood` given a value straight from a parameter of the enclosing method or lambda, or a member or element of one. A value the code produced itself is legitimate and silent; a parameter copied into a local first is not seen. Info, since test theories and helpers that only receive constants are reported too | Info | shipped in 1.0.0 |
| CMTK0008 | The wrapped values of two different value objects compared (`==`, `!=`, ordering, `Equals`, `CompareTo`) through `.Value`, `?.Value`, `GetValue()` or `ValueOrNull()`: the typed ids unwrapped into the bug they prevent. Recognised by syntax, since `.Value` of a same-project value object does not bind where analyzers run. Also `string.Equals`, `object.Equals`, a comparer's `Equals`, `string.Compare`/`CompareOrdinal` over two such values (the forms MA0006 rewrites `==` into), and two value objects of different types through `Equals(object)`, which is always false. Also a LINQ join on two such values: a query's `join … on a.Value equals b.Value`, with or without `into`, and `Join`/`GroupJoin`/`LeftJoin`/`RightJoin` of `Enumerable`, `Queryable` and `AsyncEnumerable` whose key selectors are lambdas returning them, resolved by symbol, with the candidates where a key selector does not bind; a composite key is not looked into. A raw value on one side, or an explicit cast, is silent | Warning | shipped in 1.0.0 |
| CMTK0009 | A call that returns the `default` of a no-default struct when it finds nothing: `Enumerable`/`Queryable` `FirstOrDefault`, `LastOrDefault`, `SingleOrDefault`, `ElementAtOrDefault`, `DefaultIfEmpty` without a default value, `Nullable<T>.GetValueOrDefault()`, `GetValueOrDefault(dictionary, key)`, `Activator.CreateInstance`, `RuntimeHelpers.GetUninitializedObject`, and `Option.OrDefault()`. The message names `FirstOrNone`, `GetValueOrNone` or `Or`. Warning, since a sequence checked for emptiness first is correct. Also `ImmutableArray`'s own, `Find`/`FindLast` of `List<T>`, `Array` and `ImmutableList<T>`, `ImmutableDictionary.GetValueOrDefault` and the immutable builders', and the async `FirstOrDefaultAsync`, `LastOrDefaultAsync`, `SingleOrDefaultAsync` and `ElementAtOrDefaultAsync` of `System.Linq.AsyncEnumerable` and EF Core, whose type is the method's type argument; EF Core's only where the project references it | Warning | shipped in 1.0.0 |
| — | Code fixes for the aspect's shape diagnostics CMTK1000–1002 (missing `partial`/`record`/`readonly`) | — | proposed. The aspect keeps its own error as a backstop |

**Rules that read calls see the source before weaving (measured 2026-09-28).** Metalama runs
analyzers on the unwoven source, where a call to a member the generators introduce into a value
object of the same project does not bind. CMTK0003 recognises `X.TryFrom(…)` and `x.Revalidate()`,
CMTK0007 `X.FromKnownGood(…)` (a `using static` call included) and CMTK0009 `X.TryFrom(…).OrDefault()`
by the generated member's name on a validated value object's type, which binds; the names are pinned
by the generated-surface snapshot. CMTK0001 takes a `default`'s type from the enclosing conditional or
collection when a sibling does not bind, and CMTK0006 resolves an unbound `this(…)` to the one
constructor it can mean. A local declared `var` from an unbound call, and CMTK0008 over such locals,
stay out of reach. The build-outcome consumer holds every rule to a value object of its own project,
since the verifier tests never weave.

**Generated code (decided 2026-09-28).** The rules analyse generated code and report from it only
where a location maps (`#line`) to a `.razor` or `.cshtml` file: skipping generated code, as Roslyn
does by default, silenced every rule inside Razor components, CMTK0001 included. Anything else
generated stays unreported, which keeps EF's compiled model out of CMTK0004 (the smoke test's AOT
consumer is the measurement). `GeneratedCode.Report` is the one reporting path.

**Transformed code (decided 2026-09-28, decision 33).** Where Metalama compiles a project, source
generators run after its transformation, so the Razor compiler's output is not in the source an
analyzer sees, and neither are the members the aspects introduce. The analyzer package's
`buildTransitive` props lists the analyzers' namespace as a `MetalamaTransformedCodeAnalyzer`, and
Metalama's compiler runs them on the transformed code. The IDE ignores the item, so live analysis
still runs on the source, where the name-based forms above remain the reach.

**Release discipline.** Keep `AnalyzerReleases.Shipped/Unshipped.md` tracking (RS2008). A new rule
ships at Warning or Info in a minor version and is raised to Error only in a major. Consumers
build with warnings as errors, so a new Error rule in a minor version would break their builds on
update. Within a major version an Error never reports more than it did; a Warning or Info may learn
more forms in a minor version (decision 34, stated in the analyzer README).

## 11. Native AOT

**Decided 2026-09-27 (decision 16), measured by publishing consumers with Native AOT** (SDK 10.0.401,
ILCompiler 10.0.12, osx-arm64), JSON through source-generated contexts, OpenAPI in a slim web
host, EF Core through `dotnet ef dbcontext optimize --precompile-queries --nativeaot`.

- **What the analyzers can and cannot see.** `IsAotCompatible` runs the trim and AOT analyzers on
  the packages' own code, and warnings are errors. The generators' product is compiled in the
  consumer, where only the consumer's publish sees it, and the analyzers cannot tell that a run-time
  path still works: the two worst findings (stored JSON validated, every schema `{}`) produced no
  warning at build time on the JIT. So the smoke test publishes with Native AOT and runs the binary.
- **Trimming removes interfaces nothing uses.** ILCompiler keeps an interface on a type only if the
  interface's definition is used somewhere; `typeof(IValueObject<,>)` does not count. Neither
  `[DynamicallyAccessedMembers(Interfaces)]` on the interface nor on the value object kept it (both
  measured). Custom attributes are kept, which is why discovery reads
  `GeneratedValueObjectAttribute<,>` (decision 17).
- **And keeps them once anything uses the interface.** The attribute's own constraint does, and so
  does the EF satellite's converter: with the attribute generated, a `GetInterfaces()` lookup found
  `IValueObject<,>` again under Native AOT, and without it the same host described every value object
  as `{}` (both measured). That is ILCompiler's behaviour, not a contract, so the smoke test cannot
  catch a return to `GetInterfaces()` at run time; the analyzers' IL2070, an error in the packages'
  build, does.
- **JSON.** The generated converter's wrapped value goes through `GeneratedJson.TypeInfo` (§5), so a
  context lists the value objects and never what they wrap. A value object wrapping a type the
  serializer has no converter for (a class of the application's) still needs that type in the
  context, and the serializer's refusal names it.
- **OpenAPI.** The host's context lists what its value objects wrap (§7).
- **EF Core** is experimental under Native AOT and needs a compiled model and precompiled queries;
  its `DbContext` constructors require dynamic code. With the satellite, a value object maps, writes,
  queries by `.Value`, binds as a parameter and reads a stored value without validation, in the
  native binary. EF limitations met on the way, not ours to fix:
  - the compiled model cannot write the sentinel of a **struct value object wrapping a reference
    type** (`default(Code)` converts to `null`, and EF then needs a literal for the struct itself).
    Such a value object on an entity is a `sealed partial record` instead;
  - EF 10's precompiled queries cast an entity to an internal interface, which does not compile for
    a **sealed** entity class.
- **Third-party packages** report their own warnings (NodaTime.Serialization.SystemTextJson 1.4.0,
  EF Core). The smoke test asserts only that none names a CodoMetis package or a woven value object.

