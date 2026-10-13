# History: the phases

The phase log as it was kept in `design/plan.md` until 2026-10-13: phases 0 to 13, each step with its
date and the measurements it recorded. It is closed. What is open is in the [roadmap](roadmap.md), what
was decided is in [the decisions](adr/README.md), and nothing here is read before a change: it is where
a "why" that the changelog does not hold is found.

Status at the split: **1.0.0 released 2026-09-28** (nuget.org, GitHub release `v1.0.0`). Phases 0 to 11 are done;
what is left for 1.x is listed at the end of phase 11, and package validation holds every pack to the
1.5.0 surface. Phase 12 is 1.5.0, released 2026-10-11; 1.5.1 followed the same day with a fix to parameter schemas. Phase 13 is the next release, 2.0.0 (decisions [42](adr/0042-2-0-0-next-release-bundles.md) and [44](adr/0044-2-0-0-net10-0-metalama.md): `net10.0`, Metalama 2026.1). The decisions are in [§8](adr/README.md), Native AOT in §11. The fabric spike
([spikes/FabricSpike](../spikes/FabricSpike/README.md)), the translation comparison
([spikes/ValueTranslation](../spikes/ValueTranslation/README.md)), the EF mapping spike
([spikes/EfMapping](../spikes/EfMapping/README.md)) and the OpenAPI spike
([spikes/OpenApiSchemas](../spikes/OpenApiSchemas/README.md)) are done, and so is phase 12's
`Result`-to-HTTP spike ([spikes/ResultHttp](../spikes/ResultHttp/README.md)).

## The phases

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
     `[DebuggerDisplay]` that does ([§9](option-result.md)).
   - Guards, each proven by seeding its defect:
     - one throw case per branching member (21), plus a completeness test that fails when a
       public `Result` member is added without being classified as branching or not;
     - `ToString` never contains the content, including for the markers;
     - the debugger display names an existing private property and shows the content;
     - the collapsing `Match` runs its success callback.
   - Found in the fourth review on 2026-09-27, and fixed: `Option`/`Result` serialized as `{}` and
     `{"State":1}` and read back as `None` and uninitialized, silently. Every exported struct now
     carries `NotWireTypeJsonConverterFactory` ([§9](option-result.md)). Guard: with the attributes removed, 58 of the 62
     `NotWireTypeTests` fail, the other 4 pin the escape hatch and the absent-property gap.
   - Found in the fifth review on 2026-09-27, and fixed: `Result` never checked its delegates, so a
     null for the branch not taken passed until the other outcome first arrived (`Match(null, …)` on
     every error), and `Option.ToResult` and the zips lost `Option`'s own check inside a lambda. Every
     delegate is now checked before a branch is picked ([§9](option-result.md)). Guard: `NullDelegateTests` runs each case
     on both branches, and its completeness test holds every delegate parameter of the assembly to a
     case; with the checks reverted, 29 of its 42 tests fail.
2. **Value-object contracts and analyzer. ✅ Done 2026-09-27.**
   - The contracts: `IValueObject<,>`, `IPlainValueObject<,>`, `IValue<>`, `IValidatedValue<,,>`,
     `IValueObjectMaterializer<,>`, `GeneratedFactories`, `TranslatedAsWrappedValueAttribute`, and
     `GuidValueExtensions` with `New()` and `New(DateTimeOffset)`. No NodaTime.
   - CMTK0001 and CMTK0002, resolved by symbol once per compilation ([§3](discovery.md)). CMTK0001 also follows a
     type parameter's constraints (`default(T)`/`new T()` where `T : struct, IValue<…>`, or
     `T : IValue<…>, new()`, which only a struct value object satisfies).
   - The verifier tests compile against the real `CodoMetis.TypeKit` assembly, with no copy of the
     contracts, and `default(Option<int>)` is one of the positive controls.
   - Packaging ([§10](analyzers.md)): `CodoMetis.TypeKit` depends on `CodoMetis.TypeKit.Analyzers`, which ships only
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
     built on Metalama 2026.1.28 ([§5](aspects.md)).
   - Every generated way into a validated value object applies `Create` ([§5](aspects.md), entry points).
   - Probes in `test/CodoMetis.TypeKit.Generators.Probes`, one per JSON and parsing strategy plus
     the edge cases, and behaviour tests for every generated member against them. The tests reach
     the generators only through the probes, which is the transitive-fabric check.
   - The generated-surface snapshot ([§6](test-strategy.md)), `GeneratedSurface.verified.txt`.
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
     back; a record class's comparison threw on null ([§5](aspects.md)); `Option.Some(null)` reported a value;
     and `Result<TError>` did not accept the `Result.Error(e)` marker ([§9](option-result.md)).
   - Found in the second review on 2026-09-27, and fixed the same way (39 guards fail with every fix
     reverted): two nested value objects of one name crashed Metalama, and a declared
     `{Name}Extensions` failed the aspect (CMTK1007 now); malformed JSON threw `FormatException`,
     which a minimal API answers with 500; interpolation formatted in the current culture, so
     `Parse($"{x}", null)` read "1,5" as 15; a `DateTime` without an offset was converted through
     the server's time zone; `From(null)` wrapped null; `Result` held a null value or error;
     `Result<TError>.Match` could not see the error ([§9](option-result.md)); a derived record-class value object failed
     inside the generated code (CMTK1006 now); CMTK0001 missed `new T()` under `new()`.
   - Found in the third review on 2026-09-27: a hand-written comparison operator failed the aspect
     (LAMA0500) and a hand-written object `CompareTo` was kept silently beside the generated generic
     one; both are CMTK1008 now, and the one seam, `CompareTo(TSelf)`, is documented and pinned ([§5](aspects.md)).
   - Found by the OpenAPI spike on 2026-09-27 (finding 16): a record class that wraps itself, or
     two that wrap each other, compiled without a word, and a struct cycle failed as LAMA0611. Every
     value object over a value object is CMTK1005 now ([§5](aspects.md)). Guards, each proven by seeding its
     defect: without the refusal 12 build-outcome tests fail (the struct cycle's LAMA0611 among
     them); reporting a cycle as plain nesting fails 4 (a self-wrap then compiles again); allowing
     nesting without a cycle fails 2.
   - Found in the fifth review on 2026-09-27: a hand-written constructor. One with the generated
     constructor's signature failed as LAMA0611, a positional record (`OrderId(Guid Value)`) as
     LAMA0500 or an exception in the aspect (LAMA0041), and any other compiled without a word: it
     skipped `Create`, left `Value` null, or, as a record's copy constructor, did that to `with`.
     Every hand-written instance constructor is CMTK1009 now ([§5](aspects.md)). Guards, each proven by seeding its
     defect: without the refusal 13 build-outcome tests fail, the LAMA errors among them; refusing a
     static constructor too fails 2.
4. **EF Core. ✅ Done 2026-09-27.**
   - The mapping spike first (spikes/EfMapping): an additive type-mapping-source plugin maps every
     value object, with keys, foreign keys, nullable properties and primitive collections, on
     SQLite and PostgreSQL, whatever the registration order, and beside a library that replaces
     EF's converter selector ([decision 9](adr/0009-ef-mapping-additive-irelationaltypemappingsourceplugin.md)).
   - `CodoMetis.TypeKit.EntityFrameworkCore`: `ValueObjectConverter<TSelf, T>`, the plugin, the
     `.Value`/`GetValue()`/`ValueOrNull()` translators, `UseTypeKit()`, and
     `AddEntityFrameworkTypeKit()` for an application that builds EF's internal service provider.
   - Stored JSON ([§4](ef-core.md)): `StoredJsonConverterFactory` in the base package ([decision 10](adr/0010-stored-json-storedjsonconverterfactory.md)).
   - Tests: the model on SQLite, SQL snapshots on PostgreSQL via `ToQueryString`, the default
     comparer against `.Value`, and one Testcontainers PostgreSQL round trip covering every
     wrapped-type family, stored values the rules refuse, and the translated queries.
   - Guards, each proven by seeding its defect: a plugin that stops recognising value objects
     fails 33 of 34 EF tests (all but the control), one that drops the column facets fails the
     facet test; losing the member or the method translator
     fails 4 and 3; a validating `Materialize` fails the stored-row test; a stored-JSON converter
     that validates fails the stored value and key tests. The SQL snapshots first failed on the
     cast `Convert` produced, which led to re-typing the column ([§4](ef-core.md)).
   - Found in the fifth review on 2026-09-27, and fixed: the method translator took any static
     one-argument method carrying the public `TranslatedAsWrappedValueAttribute` for the unwrap, so
     a hand-marked `Length(this ProbeCode)` became `WHERE o."Code" = '3'`. It now requires the
     unwrap's signature ([§4](ef-core.md)). Guard: the hand-marked test fails without the check, and the
     `GetValue`/`ValueOrNull` snapshots, now with a record class's `ValueOrNull`, still translate.
5. **ASP.NET Core. ✅ Done 2026-09-27.**
   - The measurement spike first (spikes/OpenApiSchemas): a schema transformer reaches a value
     object that only ever appears as a property, which the earlier measurement had denied; container
     elements and `TryParse`-bound parameters need handling of their own ([§7](openapi.md), [decision 11](adr/0011-openapi-schema-transformer.md)).
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
     parameter whose value object is declared in the same project ([decision 12](adr/0012-asp0020-documented-suppressed.md)).
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
     `Result<T, TError>`, which does not compile ([§9](option-result.md)); and a host that has
     `Microsoft.AspNetCore.OpenApi` only through the satellite fails with CS9137, because that
     package enables its generator's interceptors in `build/`, which NuGet imports for a direct
     reference only. The satellite README says to reference it, as the `webapi` template does; a copy
     of Microsoft's switch in our own `buildTransitive` would drift from theirs.

7. **Native AOT. ✅ Done 2026-09-27** ([§11](native-aot.md)).
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
       nothing. The satellites now read `GeneratedValueObjectAttribute<,>` ([decision 17](adr/0017-run-time-discovery-reads-generatedvalueobjectattribute-tvalueobject.md)).
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
     `[UnconditionalSuppressMessage]`, since the attribute keeps the interface alive ([§11](native-aot.md)).
8. **Pre-release naming and syntax. ✅ Done 2026-09-27.**
   - Names ([decision 18](adr/0018-name-per-concept.md)): the wrapped value, the plain value object, the value object's own type
     parameter and the delegate parameters each have one name, and the namespaces follow who uses
     a type ([§1](packages.md)). Every rename is a compile error for code that used the old name; the analyzer's
     metadata-name tests and the generated-surface snapshot moved with them, the snapshot by exactly
     the interface rename and nothing else.
   - Syntax ([decision 19](adr/0019-concise-implicit.md)): `Result.Success` (was `Result.Ok`), an `Option.None()` marker, `MapError`,
     `Bind` from a valued result onto `Result<TError>`, `Or(fallback)` (was `Coalesce`), and a
     `ToResult(error)` that keeps the value; the marker-lambda overloads and the collapsing `Match`
     are gone. Measured first against a replica of the conversions: the marker works in a return, a
     conditional beside `Some`, a `Bind` lambda and an `async` method, and the new `Bind` resolves
     beside the generic one. The completeness tests forced a case for every new member; seeding
     `None` out of the refusal, `MapError` past the uninitialized check, or the command into the
     error branch of `Bind` fails 7, 1 and 1 tests. `ToResult` checks its error on either branch,
     like the delegates, and a `Some` that accepted a null error fails its test.
9. **Before the first release: rule set, Result pipelines, performance. ✅ Done 2026-09-28.**
   - The analyzer rule set ([decision 13](adr/0013-first-release-s-rule-set-cmtk0001.md)): CMTK0003–CMTK0008 beside CMTK0001/0002 ([§10](analyzers.md)). CMTK0004
     ships at Error, which only the first release can do without a major version.
   - `Revalidate()` on a validated value object ([§5](aspects.md)): `Create` applied to the value it holds, for
     finding the stored values a rule added later refuses.
   - `Result` ([§9](option-result.md)): `MapAsync`/`BindAsync` on both shapes, and on a `Task<Result<…>>` a continuation
     of every combinator (`MapAsync`, `BindAsync`, `MapErrorAsync`, `TapAsync`); `Zip` for two to six
     results; `Sequence` and `Traverse`. All stop at the first error.
   - Benchmarks (`benchmarks/`), against the wrapped types, and a spike against Vogen
     ([spikes/ValueObjectPerformance](../spikes/ValueObjectPerformance/README.md)).
   - Found by the benchmarks, and fixed: `TryFormat` boxed the wrapped value (32 bytes, 3.5 times a
     `Guid`'s time); comparing an enum-backed value object boxed both operands (sorting 1,000 took 8
     times as long and allocated 367 KB); the JSON converter wrote and read a number through a nested
     serializer call (1.35–1.40 times). All at parity after the fix ([§5](aspects.md)).
   - Found while measuring the rules on woven code: Metalama runs analyzers on the source before
     weaving, where a call to a generated member of a value object in the same project does not bind,
     so CMTK0003 missed an ignored `TryFrom` and CMTK0007 every `FromKnownGood` ([§10](analyzers.md)).
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
    - Release: the first release is 1.0.0 ([decision 21](adr/0021-first-release-1-0-0.md)); package tags, copyright and release notes,
      held by `PackageReadmeTests`; the analyzer's Roslyn floor measured and held ([decision 25](adr/0025-analyzer-s-roslyn-floor-stays-5.md)), and the
      build-time `Microsoft.CodeAnalysis.Analyzers` on 5.9.0; registration extensions in the framework
      namespaces ([decision 22](adr/0022-registration-extensions-live-framework-s-namespaces.md)), which the smoke test's consumers use without a `using`.
    - `Option`/`Result`: `Ensure`, `TapError` and their continuations; query syntax with more than
      one `from` (the one-selector `SelectMany` made the second `from` a compile error);
      `GetValueOrNone`; `FirstOrNone()`/`LastOrNone()` without a predicate; `FirstOrError`/`LastOrError`
      check their error on either branch, as `ToResult` does. The completeness tests forced a null and
      an uninitialized case for each; six seeds fail seven tests.
    - Generators ([§5](aspects.md)): a hand-written `ToString()` was replaced silently (a secret printed); a value
      the wrapped type could not parse was quoted in the exception; the JSON differed from the wrapped
      type's for `TimeOnly`, date keys, host converters and key policies; an enum-backed value object
      allocated 4.7 KB per JSON value; `Uri` parsing disagreed with JSON; `ToType` failed the identity
      conversion; nine declarations failed as Metalama errors (CMTK1005, CMTK1010, CMTK1011 now). Guards:
      `ToStringSeamTests`, `UnreadableInputTests`, `JsonParityTests`, the allocation and parsing tests,
      18 build-outcome rows.
    - Analyzers ([§10](analyzers.md)): nothing fired in Razor; `OrDefault()` and `FirstOrDefault` handed out defaults
      unreported (CMTK0009); false positives on guard checks and zero-length arrays; false negatives
      in the declaring project, for `x!`/`(x)`/named arguments, static comparisons, and a `Task` of a
      result converted to `Task`; CMTK0001 35% faster. 42 verifier tests and six build-outcome cases
      added, every seed recorded failing.
    - Satellites ([§4](ef-core.md), [§7](openapi.md)): `.Value` on a collection element failed to translate; a key over an integer
      lost its identity column ([decision 23](adr/0023-value-object-key-over-integer-generated.md)); nested value objects shared an OpenAPI component
      ([decision 24](adr/0024-nested-value-objects-named-after-nesting.md)). The smoke test's AOT consumer inserts through an identity key.
    - Documentation: every C# block of the READMEs is compiled and its stated outputs asserted ([§6](test-strategy.md),
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
    - Analyzers ([§10](analyzers.md)): where Metalama compiles a project, source generators run after its
      transformation (Metalama 2025.0), and an analyzer sees only the source. Every rule was silent in
      the `.razor` files of the usual Blazor layout (a web project referencing a value-object project)
      and missed forms in the declaring project. The analyzer package's `buildTransitive` props lists
      the analyzers' namespace as a `MetalamaTransformedCodeAnalyzer` item ([decision 33](adr/0033-analyzers-run-code-metalama-transformed.md)).
      `AnalyzerPackagingTests` holds every analyzer to it; the smoke test's layered consumer gains a
      Razor library and a guard in Domain, and both build clean with the props left out. Every rule
      has a `helpLinkUri` (`AnalyzerHelpLinkTests`). The README adds `[RequireCustomInitialization]` for
      a struct made of value objects, and `.globalconfig` for severities in Razor, which no
      `.editorconfig` section reaches (measured).
    - `Option`/`Result` ([§9](option-result.md)): `Result<T, TError>` lost its `bool` conversion ([decision 29](adr/0029-only-result-terror-converts-bool.md));
      `TapErrorAsync(Func<TError, Task>)` on both shapes and both continuations, since an `async` error
      callback ran as `async void` and its exception ended the process ([decision 30](adr/0030-asynchronous-callbacks-return-task.md)); `TryCast` on
      `object?` removed ([decision 31](adr/0031-no-extension-object-unconstrained-t.md)). CMTK0003 treats the instance `TapErrorAsync` like `TapAsync`.
      Five seeds fail twelve tests.
    - Generators ([§5](aspects.md)): state besides the wrapped value is CMTK1012 (a `Currency` lost in JSON, a lazy
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
    - Satellites ([§4](ef-core.md), [§7](openapi.md)): a container of nullable value objects, and a nullable value object inlined
      by `CreateSchemaReferenceId`, admit null as the wrapped type does, in 3.1 and 3.0 ([decision 36](adr/0036-nullable-value-object-admits-null-openapi.md));
      `InlinedValueObjectTests` compares 34 positions with the wrapped type. On SQLite an integer
      value-object key is not `AUTOINCREMENT`, so the model never matched its snapshot and `Migrate()`
      threw: the README documents `UseAutoincrement()`, and `MigrationSnapshotTests` checks it
      ([decision 35](adr/0035-sqlite-s-autoincrement-application-s-line.md)). The READMEs state the 10.0.12 floors (NU1605 below them), `AddTypeKit()` per
      document or `ConfigureAll<OpenApiOptions>`, that `UseTypeKit()` needs a relational provider, and
      that value objects with one name in different namespaces share a component, as any type does.
    - Packaging ([§6](test-strategy.md)): the SBOMs also exclude what the SDK adds (`Microsoft.NET.ILLink.Tasks`,
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
    - Found and left for 1.x: CMTK0003 over `.Wait()` and `await foreach` (closed in 2.0.0 before the
      rule became an Error, [decision 50](adr/0050-warnings-cmtk0003-alone-becomes-error-2.md)); CMTK0008 over tuple `==`, `Contains` and `is var`, and
      composite join keys (a Warning, [decision 34](adr/0034-errors-fixed-within-major-version-warnings.md)). Raw Metalama errors
      for a value object named after a generated member (`From`, `TryFrom`, `MinValue`), a wrapped type
      whose `Parse` does not return it or that is abstract, and a public base member whose signature
      matches a generated one (LAMA0500). `ToString()` of a default `Uri`/`Version` value object throws; `IConvertible` quotes
      the value; `class Email : IValue<string>` reports one of its three errors at a time; `IValue<object>`
      writes `{}`. JSON on `Option<T>?` throws the wrong exception; a default marker converts.
      `UseTypeKit()` on a non-relational provider could refuse in `Validate`.
12. **1.5.0: the boundaries. ✅ Done 2026-10-11.** Every item is additive, so it is a minor version. The
    scope follows a survey of the competing libraries' trackers on 2026-10-11 (demand is small
    everywhere, at most 22 reactions on one request), keeping what no library ships or what only
    preview packages serve, and what follows from what TypeKit already holds to.
    - **A `Result` as an HTTP response**, in `CodoMetis.TypeKit.AspNetCore`: a success as its value, a
      fault as RFC 9457 problem details, with its status mapped from the fault in the host, so the
      domain never references ASP.NET; and the endpoint's responses in the OpenAPI document. Mapping a
      result to a response is not serializing it, so [§9](option-result.md)'s refusal stands. The most-requested open item
      in the field (FluentResults #149); the packages that answer it are previews or unmaintained.
      Measured 2026-10-11 in [spikes/ResultHttp](../spikes/ResultHttp/README.md), under JIT and Native AOT,
      minimal APIs and MVC: an explicit `ToHttpResult()` returning ASP.NET's own
      `Results<Ok<T>, FaultProblem<TError>>`, a mapping per fault type registered in the host
      (`AddFaultResponses`, which calls `AddProblemDetails()`, without which Native AOT writes no
      problem), endpoint metadata recording only the fault type (MVC hands `PopulateMetadata` an empty
      service provider), an operation transformer adding a problem response per mapped status, and a
      startup filter that refuses a host with an endpoint whose fault type has no mapping. Returning
      the `Result` itself from a handler is not offered: it documents a `Result`, and a Native AOT host
      would need it in its JSON context. **✅ Implemented 2026-10-11** ([decision 38](adr/0038-result-becomes-response-explicit-tohttpresult-faults.md)): `FaultResponseTests`
      (27, against ASP.NET's own `ProducesProblem` as the control, in OpenAPI 3.1 and 3.0, minimal APIs
      and MVC), the README's sample and its problem JSON read from the README, and the smoke test's
      Native AOT consumer. Fifteen seeds each fail their guard. The weaving review found one guard
      vacuous: every host also mapped ProducesProblem controls, so a problem component the package
      never added went unnoticed; a host without them now holds it. Without `AddProblemDetails()` only
      the `traceId` assertion notices under JIT, so the AOT consumer is its real guard. A handler typed
      `IResult` loses the endpoint's metadata, as ASP.NET's typed results do; the README says so.
    - **A value object's fault as a validation error.** A body or parameter that `Create` refuses is a
      `400` whose problem details name the member and the fault, as ASP.NET Core 10's validation
      reports a data annotation, instead of a bare `400` from a `JsonException`. No library does this.
      Measured 2026-10-11 in [spikes/ValidationFaults](../spikes/ValidationFaults/README.md): ASP.NET
      Core 10's validation never sees a value that failed to bind, a refused route or query value has
      lost its fault (`TryParse` says only `false`), and MVC already puts the converter's message in its
      validation problem. What remains is a minimal-API body, reachable only with `ThrowOnBadRequest`
      turned on for the whole host and an exception handler, and only once the converter's refusal
      carries its fault as data. The README's "Binding" said `AddProblemDetails()` gives a refused body
      a response body; it needs `UseStatusCodePages()` too, which it now says. **Dropped 2026-10-11**
      ([decision 39](adr/0039-value-object-s-fault-does-become.md)).
    - **`CodoMetis.TypeKit.Testing`**: assertions for `Option`/`Result` (`ShouldBeSome()`,
      `ShouldBeError(fault)`), which a type without `.Value` needs more than any other, and generators
      of valid and refused values that go through `Create`. **✅ Assertions implemented 2026-10-11**
      ([decision 40](adr/0040-codometis-typekit-testing-holds-assertions-hand.md)): `AssertionTests` (23), the README's block run as a test and its quoted failure read
      from the README, the smoke test's core consumer installing the package. Generators are not built
      ([decision 40](adr/0040-codometis-typekit-testing-holds-assertions-hand.md)).
    - **Sensitive value objects**: a marker that keeps a value out of `ToString()`, the debugger and
      logs (`Microsoft.Extensions.Compliance` classification, structured-logging destructuring). It
      extends [decision 27](adr/0027-no-refusal-quotes-input.md) from refusals to every place a value is printed. No library does this.
      Measured 2026-10-11 in [spikes/SensitiveValues](../spikes/SensitiveValues/README.md): the
      `ToString()` seam the generators keep already masks a value in every formatting path and every
      Microsoft.Extensions.Logging path, `[LogProperties]` included, so a marker would generate the one
      line a user writes. A classification on the type makes the logging generator refuse the method
      (LOGGEN035). The one leak is Serilog's `{@x}`, which destructures the public `Value` of every
      value object, masked or not; a destructuring policy closes it. **✅ Implemented 2026-10-11**
      ([decision 41](adr/0041-sensitive-value-object-declares-tostring-serilog.md)): no marker; the Generators README's "A value that must not be printed", and
      `CodoMetis.TypeKit.Serilog`.
    - **`Recover` on `Result`**: the error branch back to a success, which today needs `Match`.
      **✅ Implemented 2026-10-11** on both shapes, with `RecoverAsync` (an asynchronous selector, and
      continuations on a pending result). It returns a `Result`, so a selector recovers from some
      errors and returns the others; the completeness tests (continuations, null delegates,
      uninitialized results) found every new member until each had its case, and four seeds each fail
      their guard.
    - Candidates once those ship, each with its own measurement: a Dapper satellite (nothing registered
      per type, through `GeneratedValueObjectAttribute.Accept`), Newtonsoft.Json under [decision 26](adr/0026-value-object-s-json-wrapped-type.md)'s
      parity, and configuration binding, which the configuration binder's source generator does not do
      for `TypeConverter`/`IParsable` types (dotnet/runtime#83599). The CMTK0003/CMTK0008 gaps above
      stay Warnings ([decision 34](adr/0034-errors-fixed-within-major-version-warnings.md)); CMTK0003's are closed in 2.0.0 ([decision 50](adr/0050-warnings-cmtk0003-alone-becomes-error-2.md)).
    - **Ranges are not a TypeKit family**: CodoMetis.ValueRanges models them. A value object over one
      (`IValidatedValue<ContractPeriod, DateRange, PeriodFault>`) works with both packages as they
      ship, measured 2026-10-11 in [spikes/RangeValueObjects](../spikes/RangeValueObjects/README.md):
      - it weaves, so phase 11's "abstract wrapped type" gap does not cover a type that parses to itself;
      - `UseTypeKit()` composes onto the `daterange` mapping in either order;
      - `.Value.Contains(day)` becomes `@>`;
      - the generated JSON and OpenAPI mirror `DateRange` exactly.

      No bridge package is needed. ValueRanges 9.0.0 (re-measured the same day) closes the three wire
      gaps the value object had inherited: `[JsonConverter]` on every range type, ranges as dictionary
      keys, and an OpenAPI transformer to copy, which reaches the value object through `AddTypeKit()`.
      The re-run found a TypeKit defect, **fixed**: a value object's `TryParse`-bound parameter took the
      wrapped type's JSON schema (`{}` over `DateRange`), where ASP.NET describes a parameter of the
      wrapped type as `string` for anything off its own list (`EndpointModelMetadata.GetDisplayType`).
      `ParameterTests` has a control for each entry of that list and one off it (`ProbeSpan`). Open: a
      README section for the pattern, with no host registration needed for JSON; and CMTK1005 refusing
      `RangeSet<,>`, whose equality is a value's, though `IEquatable` alone is no criterion
      (`ImmutableArray<T>`).

13. **2.0.0: closed sets, fault responses, merge patches, exceptions, constraints and text.** In
    progress: steps 1 to 9a done (2026-10-13), the preview and the release to come. Scoped 2026-10-12 ([decision 42](adr/0042-2-0-0-next-release-bundles.md)): the features proposed for 1.x ship in the major,
    not as a 1.6.0 before it, because the smart-enum family changes what a fault is, and fault
    responses built on enums first would be built twice. Pinned 2026-10-13 ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)) to
    `net10.0` and Metalama 2026.1: nothing in it needs .NET 11, C# 15 unions or Metalama 2027.0, and
    it waits for none of them. The build order, each step a commit with its tests, and a spike
    before the part where one is listed:
    1. **Open the release** ✅ 2026-10-13: `Version` 2.0.0 and a `## 2.0.0 — Unreleased` changelog
       section. The package-validation baseline is 1.5.1, the last release (it had stayed at 1.5.0,
       which `ChangelogTests` only sees once the version being built moves on), and reports nothing
       until the first intended break. After moving a baseline, restore the solution: the conventions
       pack with `--no-restore`, and a baseline not yet in the local cache fails every pack.
    2. **The text guards** ✅ 2026-10-13: `StringTextExtensions` and `TextFault` in the base package,
       `StringTextExtensionsTests` (the character table, pinned by code point), the README section
       "Written text" with its two compiled samples. No spike.
    3. **`Result.Catch`** ✅ 2026-10-13, after [spikes/ResultCatch](../spikes/ResultCatch/README.md).
       The spike settled the shape: the async trap is an `[Obsolete(error: true)]` overload over a
       task-returning delegate, more specific than `Func<T>`, so the compiler refuses it and names
       `CatchAsync`; its cost is that a lambda that only throws must state its return type. No
       `ValueTask` overloads: beside `Func<Task<T>>` they are ambiguous for every `async` lambda, as the
       README already says of `MapAsync`. `Catch<T, TException>` spells both type arguments, and the
       one-argument `Catch<TException>(Action)` is the command form. A delegate that returns null throws
       rather than being caught: the value is computed inside the `try`, the success built outside it.
    4. **CMTK0011 and its code fix** ✅ 2026-10-13: `[RequireParameterlessConstructor]` and
       `ConstructorAccessibility` in the base package, `RequiredParameterlessConstructorAnalyzer` in
       `.Analyzers`, the fix in `CodeFixes/`, a project nested in the analyzer project's folder and
       packed into the same package under `analyzers/dotnet/cs`: an analyzer assembly must not
       reference Workspaces (RS1038), and a sibling under `src/` would be a package of its own to the
       conventions. The fix spells the id and the property name it reads, held to the analyzer's by a
       test. Named `Require…`, beside `[RequireCustomInitialization]`, since the rule requires and the
       fix writes; nothing is introduced.
    5. **CMTK0010** ✅ 2026-10-13, after [spikes/TypeArgumentConstraints](../spikes/TypeArgumentConstraints/README.md),
       run with the real rule over a library, a consumer and a Razor component: every seeded breach
       reported once, the forwarding hole closed (a type parameter carries the attribute, or `struct`
       and `new()` prove `[Sealed]`/`[Concrete]`), nothing for the open generic in its own body. The
       attributes are `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` in the base package; a
       generic name is checked where it is written, at the argument, and a call with inferred type
       arguments at the call.
    6. **`Patch<T>`** ✅ 2026-10-13, after [spikes/MergePatch](../spikes/MergePatch/README.md), in the
       base package and the ASP.NET satellite. The spike settled the AOT shape: a converter type built
       with `MakeGenericType` has no constructor to invoke under Native AOT, a static field read by
       reflection is trimmed, an open generic converter in `[JsonConverter]` is refused by the source
       generator (SYSLIB1220); what works is a registry the closed `Patch<T>`'s static constructor
       fills, which the factory runs for the type asked for (`RunClassConstructor`, one IL2059
       suppression with that measurement as its reason). A primitive inside a patch takes the
       serializer's built-in converter, since the source generator never sees what a patch wraps; a
       class is listed in the context. The document inlines a patch as its argument's schema, never
       required, always admitting null; a value object inside a patch is described through what it
       wraps, since a source-generated host has no contract for it.
    7. **The closed-set family** ✅ 2026-10-13, after [spikes/ClosedSets](../spikes/ClosedSets/README.md),
       which answered the compatibility question: a generated type with static members on Metalama
       2026.1 and `net10.0`, nothing newer needed. `IClosedSet<TSelf>` in the base package,
       `ClosedSetAspect` in the generators (applied by the same transitive fabric), CMTK1013–1021, the
       discovery attribute `GeneratedClosedSetAttribute<TSet>` with a typed visitor, a text column in
       the EF satellite and a string enum in the OpenAPI satellite. The fault bits (a kind and a
       sentence per member) are step 8's, since the fault responses read them. Two reviews the same
       day: the first's fixes are CMTK1019–1020 and the kept `ToString()`; the second's are CMTK0012
       (arguments by position), CMTK1021 (a member named as a generated member, or two sharing a
       `Match` parameter), null delegates refused before a branch is picked, dictionary keys, and
       `ClosedSetConverter<TSet>`: the compiled model wrote the internal mapping's helpers out as C#
       in the application (CS0122), so no closed set reached Native AOT. The smoke test's `aot`
       consumer now publishes a set's JSON, its column, a parameter and a JSON column.
    7a. **The research picks** (2026-10-13): a research agent reviewed what 2.0.0 lacks against the
       peers and the issue trackers, and the user took its top five, each a commit:
       `FirstOrNone`/`FirstOrError` on a query and EF's `FirstOrNoneAsync` ✅ ([decision 47](adr/0047-firstornone-query-runs-query.md)); a closed
       set's `[TypeConverter]` and `IParsable` ✅ (explicit, since `Parse(string)` is its surface; the
       reflection binder now binds a set; the binder's source generator reads no type converter,
       dotnet/runtime#83599); Serilog for closed sets ✅ (measured first: `{@Tier}` logged
       `ProbeTier { Rank: 2, Perk: "Priority support.", … }`); continuations of a
       `Task<Option<T>>` ✅ (the `Task<Result>` set's shapes: no `OrAsync`, no `MatchAsync`, no
       `ValueTask` receiver); `Transpose`, `SingleOrNone` and `ElementAtOrNone` ✅ (more than one is a
       refusal, as `SingleOrDefault`'s; a negative index on a query is `None` without a query, since
       `Skip(-1)` skips nothing).
    8. **The fault responses** ✅ 2026-10-13, after [spikes/FaultResponses](../spikes/FaultResponses/README.md),
       whose candidate was the satellite itself and whose thirteen claims are its tests and the smoke
       test's AOT consumer. In the base package: `FaultKind`, `IFault` and `IFaultSet<TSelf>` (a kind
       and an optional description per member), and `Fault<TFault>` (a reason and a detail, refused
       by JSON and by CMTK0001 when `default`). In the ASP.NET satellite: one internal `FaultType<TFault>`
       for an enum and a closed set alike, `FaultResponse` (status, `type`, `title`),
       `AddFaultResponses<TSet>()` by kind, `WithFaultStatus` and `[FaultStatus<TFault>]`,
       `ToHttpResult(success)`, `IFaultFeature` and `FaultResponseOptions.QualifiedFaultNames`
       ([decision 49](adr/0049-fault-responses-client-can-act.md)). `FaultProblem<TFault>`'s constraint relaxed to `notnull`, the first difference
       the 1.5.1 baseline reports, suppressed alone ([decision 48](adr/0048-intended-break-suppressed-alone-baseline-stays.md)).
    9. **The breaks** ✅ 2026-10-13 ([decision 50](adr/0050-warnings-cmtk0003-alone-becomes-error-2.md)). CMTK0003 is the one Warning raised to Error, after
       its gaps were closed: `Wait` on a task the expression made, tasks of results made in the
       statement converted to plain ones (`Task.WaitAll`, `WaitAny`), a `foreach` or `await foreach`
       whose body never reads the results its header made, and a lazy sequence of results a call made,
       materialised and dropped (eleven seeds, each failing its guard; an explicit cast to plain tasks
       stays silent, as a cast to `Task` does). The release-tracking file
       records the change (RS2001 refuses a descriptor that disagrees, RS2007 a change that is none),
       `AnalyzerHelpLinkTests` holds the README's severity column to every descriptor, and the
       build-outcome consumer no longer raises CMTK0003 itself, so its reports there prove the
       default. CMTK0010–0012 needed nothing: they ship at Error in this major and report only on
       what it adds. The 1.5.1 baseline, run over all seven packages with the attribute and
       parameter-name rules on as well, reports the one suppressed difference and compiler-emitted
       attributes only.
    9a. **A review's findings** (2026-10-13), each a commit:
       - **`Catch` over a task** ✅: the command form `Catch<TException>(Action)` took a delegate
         returning a task and discarded it, an `async` lambda as `async void`; a task spelled as the
         type argument (`Catch<Task<int>, …>`) and an inferred `ValueTask` were a success holding the
         task. `Catch<TException>(Func<Task>)` is refused at compile time, and so is
         `Catch<TException>(Func<object?>)`, any value the command form would discard, which is the
         only overload a `ValueTask<T>` reaches (the spike's ambiguity rules out `ValueTask`
         overloads beside `Task` ones: measured again, every `async` lambda became CS0121). A task
         as the value, spelled or inferred, is refused at run time before the delegate runs, and in
         `CatchAsync` a task whose value is a task. Two-type-argument refusals were measured and
         dropped: they make a lambda that only throws, or returns `null!`, ambiguous, and the calls
         they would refuse are already the compiler's conversion errors.
       - **A patch's schema refused or admitted the wrong values** ✅, measured by validating values
         with JsonSchema.Net (test-only) against the document, 3.0's `nullable` read as 3.0.3 defines
         it: a string enum's patch was `"type": "null"` beside its names and admitted nothing, a
         closed set's listed no null in its enum and refused it, and `Patch<object>` was
         `"type": "null"` and admitted only null. Null now joins what restricts the values: the
         enum's list, the type, or, for a schema combining subschemas, a branch beside them.
         ASP.NET's own polymorphic schema needed no branch, its derived schemas stating no type; a
         host's `oneOf` of typed schemas does, and guards it.
       - **CMTK0010 over a method group** ✅: `Action<PlaceOrder> run = Runner.Run;` infers the
         type arguments from the delegate, with no generic name written and no call, and was
         silent. Every method reference is checked as an inferred call is, at the group; a group
         with its arguments written stays the generic name's, reported once.
       - **A closed set's `All` was writable** ✅: it was the backing array, so a cast to
         `IList<T>` or `T[]` replaced a member that `All`, the JSON schema's enum and every
         enumeration then listed, while `Parse`, which reads the fields, answered the original. It
         is the array wrapped by `Array.AsReadOnly`, whose writes throw `NotSupportedException`.
       - **Invisible text passed `AsText()`** ✅: a value of joiners alone (`"\u200D"`), of variation
         selectors alone (filed as non-spacing marks, so no category test reached them), or of a
         lone combining mark rendered nothing and was text. A value now needs a base character, and
         a variation selector stands only directly after one, so emoji presentation, a flag, a
         keycap and an ideographic variant survive; a joiner is not placed, since an Arabic letter
         shown in its joining form on its own ends with one.
       - **The docs** ✅: the README no longer lists fault responses (documented in the ASP.NET Core
         README and guide since step 8) or public aspects (none planned) as still to come;
         `docs/migrating-to-2.md` lists what builds or runs differently for 1.5 code, compiled like
         every page; and the Native AOT page states the query helpers' limitation, which the smoke
         test's `aot` consumer now measures: the base package's `FirstOrNone` on a query and the
         satellite's `FirstOrNoneAsync` throw "Query wasn't precompiled", and the operators written
         at the call site run.
    9b. **A second review's picks** (2026-10-13): two outside reviews of the release were checked
       against the code (one named `Choose`, result aggregation and a dictionary lookup as missing,
       where `Zip`, `Sequence`, `Traverse` and `GetValueOrNone` exist, and read CMTK0006 as a `.Value`
       guard), and the user took what held, each in this release:
       - **`Choose`** ✅: on a sequence with a selector answering an option, and on a sequence of
         options; lazy, the arguments checked at the call (F#'s `choose`, CSharpFunctionalExtensions'
         `Choose`).
       - **`Patch<T>.Map` and `Traverse`** ✅: a patch over what the request sent, validated into a
         patch over the domain's type with `Create`'s own fault, a clear and an absence passing
         through, `Option<T>.Traverse`'s pattern; a `Map` selector answering null is refused, since
         a clear is the Null state and never a value.
       - **`TraverseAsync`** ✅, sequential: one operation awaited at a time, in order, nothing
         started after the first error; the token overloads check it before each operation and hand
         it on, and a cancellation propagates, as `Catch` lets it. Concurrency stays explicit,
         `Task.WhenAll` and `Sequence()`.
       - **Patch assertions** ✅ in `.Testing`: `ShouldBeValue()` hands the value back,
         `ShouldBeNull()` and `ShouldBeAbsent()` name the state, never the value.
       - **A closed set parsed ignoring case** ✅ ([decision 51](adr/0051-closed-set-parses-ignoring-case-only.md)): `Parse(name, ignoreCase)` and
         `TryParse(name, ignoreCase, out member)`, opted into at the call; two members whose names
         differ only in case are CMTK1021, which subsumes the `Match`-parameter collision.
       - **The README** ✅ refreshed for 2.0.0: the status, the comparison tables' version, the pitch
         naming closed sets and the release's other additions, and a closed-set comparison against
         Ardalis.SmartEnum 8.2.0, Intellenum 2.0.1 and Thinktecture.Runtime.Extensions 10.5.1, read
         from source and nuspecs on 2026-10-13.
       - Not taken: error accumulation and a `Combine` ([§9](option-result.md) non-goals). A stable external name for a
         closed-set member, apart from its C# identifier, touches parsing, JSON, EF, OpenAPI and fault
         identifiers at once and needs a spike first; it is 2.x.
    9c. **An outside review before the tag** (2026-10-13, a separate Opus 5.5 session over the three
       commits and the library), its must-fix findings each fixed with a seeded test:
       - A closed set in two partial files listed the second file's members as null in `All` (a field
         initializer runs with its own part); `All` is filled by the names initializer, after every
         part's fields. Two nested sets of one simple name shared a `QualifiedName` prefix and one
         OpenAPI component; both are the nesting chain. A non-public static field of the set's type
         was an instance that is no member; CMTK1017. The nested converters are sealed.
       - A closed set was a string enum as a body property only: a route or query parameter was a bare
         `string`, a container's element was missing. The transformer takes the value-object
         transformer's three positions.
       - `ElementAtOrNone(^n)` on a query read every row through the sequence overload; refused as
         `LastOrNone` is. `fault == reason` on a `Fault<TFault>` compiled and was false with a detail;
         the mixed forms are refused, naming `Reason`. `CatchAsync` over a delegate returning no task
         was an error holding a `NullReferenceException`; it throws before awaiting.
       - CMTK0010 reported doc-comment crefs and called arrays not sealed; a method implementing or
         overriding a constrained method without the attribute let a call through the class pass.
         CMTK0003 missed a result task picked by a conditional or a `switch` expression and a method
         group answering a collection of results: [decision 50](adr/0050-warnings-cmtk0003-alone-becomes-error-2.md)'s "complete" was not, and is now
         measured by those forms too.
       - Documentation: the claims the review found misleading corrected (a set as a parameter, the
         `Index` form on a query, "everything works" under Native AOT, `AsText()` and line breaks,
         `CatchAsync` over a null task, the `ignoreCase` overloads and binding, the Testing messages'
         error, the EF limitations' count), the migration guide's name list completed, `Fault<T>` under
         "Not wire types", `WithTitle`, the command `ToHttpResult(success)` and `[FaultStatus]` on a
         class documented, the package descriptions naming closed sets, and the prose it flagged.
       - Can wait for 2.1, recorded here: the fault name is sent even for a member without a
         description; `Uri.ToString()` unescapes the problem `type`; the CMTK0011 code fix on a partial
         type with a primary constructor elsewhere, and its doc comment beside an attribute; CMTK0003
         over `WhenAny` of held tasks; `Choose`'s `notnull` receiver; `Patch<string?>` under `Traverse`.
    10. **`2.0.0-preview.1` to nuget.org** once steps 7 and 8 exist, the consumer smoke test
        against it, then the release.
    - **A closed-set (smart enum) family**, declared by interface and joining `.Generators` ([§1](packages.md)), the
      release's headline. A member is a named instance with constant data. The generators emit `All`,
      parsing and formatting by name, JSON, an OpenAPI string enum, and a `Match` with one required
      delegate per member. C# never proves an enum `switch` complete; it only warns about a missing
      discard (CS8524). As a fault type, the family carries what `FaultKind` (proposed 2026-10-11,
      folded in here) would have read from attributes:
      - **A kind per member**: NotFound, Conflict, Invalid, Forbidden or Unavailable, nothing
        HTTP-specific, from which `AddFaultResponses` derives a status without a hand-written switch.
        It is a property set where the member is declared, so Native AOT reads it without
        reflection. The explicit mapping still overrides it, so [decision 38](adr/0038-result-becomes-response-explicit-tohttpresult-faults.md)'s switch remains the
        escape hatch.
      - **An optional sentence per member**: the fault's remedy as a person reads it. It is optional
        because a refusal that must not explain itself, a credential's or a secret's, has none on
        purpose.
      - **A generated qualified name** (`OrderFault.NotFound`) beside the bare one, so two fault types
        that share a member name stay apart in one client.

      A smart enum names a fault. It does not replace an error that carries data: a closed set of
      shapes (`NotAmendable(Status)`, `OutsidePeriod(Date)`) is what C# 15 unions are for. Translating
      one names a smart-enum member, and what varies per occurrence travels as
      `Fault<TFault>.Detail` (below). A spike must answer:
      - **Which status for each kind**, and whether a host may change a kind's default.
      - **The startup check holds**: a member with neither a kind nor an explicit mapping still stops
        the host.
      - **A generated type with static members**, not a C# 15 union: 2.0.0 is `net10.0` (decision
        44), and a union-shaped closed set is 3.0.0's question.
      - **Persistence**: an EF column by name, mapped by `.EntityFrameworkCore` as value objects are.
    - **Fault responses a client can act on**, proposed 2026-10-12. Decision 38's response carries a status and the fault's bare name, and its success is
      `200` or `204`. An application whose client shows a refusal to a person, creates resources, or
      reads with an ETag has to leave `ToHttpResult()` for each of those endpoints. The libraries
      surveyed on 2026-10-12 map centrally too, but none models `Location` or `ETag`: Ardalis.Result
      10.1.0, ErrorOr 2.1.1 and FluentResults. Ardalis and FluentResults allow a per-call status. RFC
      9457 §3.1.1 makes `type` the primary identifier, and §3.1.4 has `detail` help the client correct
      the problem. Five additive parts, each opt-in, so [decision 38](adr/0038-result-becomes-response-explicit-tohttpresult-faults.md)'s default response is unchanged:
      - **A response per member**: `AddFaultResponses<TFault>(f => FaultResponse.Status(409).WithType(uri))`,
        an overload beside the `int` one, checked for every member at registration as that one is. It
        adds `type` and `title`, which name the problem type and nothing of the input.
      - **A sentence per occurrence**: `Fault<TFault>(TFault Kind, string? Detail)`, a record struct in
        the base package. `Result<T, Fault<OrderFault>>` infers `TFault` from a type argument, which an
        `IFault<TFault>` constraint could not, since C# infers nothing from a constraint. The mapping and
        the document stay keyed on the enum. The sentence is the application's, so "the problem names
        the fault, never the input" becomes the default rather than a guarantee, and the README says so.
      - **A status per endpoint**: `.WithFaultStatus(OrderFault.NameTaken, 422)` as endpoint metadata,
        read by `FaultProblem`, the operation transformer and the startup check. The same fault can
        mean 404 on one route and 409 on another (RFC 9110 §15.5.10 defines 409 by the target resource).
      - **The success the endpoint chooses**: `ToHttpResult(order => TypedResults.Created(uri, order))`,
        returning `Results<TSuccess, FaultProblem<TFault>>`, so ASP.NET still describes the success. An
        ETag needs a result of the application's own, and this is the hook for it.
      - **The body the host shapes**: the fault reaches `CustomizeProblemDetails` as a request feature
        (`IFaultFeature`: the fault type, the member, the detail), so a host can add extensions such as
        an error code without the package naming any, plus an option to qualify `fault` as
        `OrderFault.NotFound`, since two enums can share a member name in one client's switch.

      A spike must answer, in [spikes/FaultResponses](../spikes/FaultResponses/README.md):
      - **Native AOT** for each part.
      - **One reading of a fault.** The mapping, the startup check and the document read a fault type
        through one internal abstraction, never `Enum` directly, so an enum fault and a smart-enum
        fault (above) take the same path.
      - **Inference**: `ToHttpResult()` over `Result<T, Fault<TFault>>` and over `Result<T, TFault>`
        resolves without type arguments and without an ambiguity between the two.
      - **The document**: a per-endpoint status is listed for that operation only, a `type` URI appears
        in it, and `fault` stays a string on the shared `ProblemDetails` component. A per-operation enum
        is optional, never a union over every fault type, because a fault type added later would change
        every operation's schema.
      - **The startup check** refuses a `WithFaultStatus` for a fault type the endpoint does not answer
        with, and a member that the mapping, its kind and the endpoint all leave unmapped.
      - **MVC**: the endpoint metadata reaches the transformer, where finding 4 found
        `PopulateMetadata` handed an empty service provider.
      - **`CustomizeProblemDetails` sees the feature** under JIT and Native AOT, and the feature is
        absent for every problem `ToHttpResult()` did not write.
    - **`Patch<T>`, a field of a JSON Merge Patch (RFC 7396)**, proposed 2026-10-11 for 1.x; built 2026-10-13 (step 6 above answers the questions). It has
      three states: absent, null, or a value. System.Text.Json cannot tell an absent property from a
      null, so every PATCH endpoint rebuilds this type by hand. It is a wire type on purpose, beside
      `Option`, which still refuses JSON ([decision 15](adr/0015-option-result-wire-types.md)): `Option` says whether a value exists, `Patch`
      whether a request mentioned it. A spike must answer:
      - **`default` is a state.** The serializer calls no converter for an absent property, so
        `default(Patch<T>)` has to mean "absent". That makes this the one type of the package whose
        `default` is meaningful. Is it exempt from CMTK0001 by its contract, and does that stay
        clear of `[RequireCustomInitialization]`?
      - **Native AOT.** A converter factory over an open `T` is `MakeGenericType`, which the refusing
        converter avoided only because it never reads `T`. Find what works under a source-generated
        context: an attribute per closed type, or a generated converter.
      - **Value objects.** `Patch<Email>`: a JSON null and a value `Create` refuses each give the
        response [decision 39](adr/0039-value-object-s-fault-does-become.md) measured.
      - **The OpenAPI document.** The property is not required, and admits null exactly where `T`
        does, in 3.1 and 3.0.
      - **Applying it.** Without reflection over the target ([§11](native-aot.md)): `patch.Name.IfPresent(v => order.Name = v)`,
        not an `ApplyTo(entity)` that walks properties. Optionally an analyzer for a `Patch` that is
        bound and never read.

      It belongs in the base package, its OpenAPI part in the ASP.NET satellite. It is not ASP.NET
      Core 10's JSON Patch (RFC 6902, a list of operations).
    - **`Result.Catch`: an exception as an outcome**, proposed 2026-10-13. A call that throws has no
      way into a `Result` today, so every caller writes the `try`/`catch` by hand, and a `Try<T>`
      type would be `Result<T, Exception>` under a second name ([§9](option-result.md)). Four factories on the `Result`
      static class, each returning a concrete result rather than a marker, since the delegate fixes
      `T`:
      - **`Catch<T>(Func<T>)`** to `Result<T, Exception>`, and **`Catch(Action)`** to
        `Result<Exception>`, the value-less shape, which is why no `Unit` is needed.
      - **`Catch<T, TException>(Func<T>)`** to `Result<T, TException>`, catching that type and
        letting every other exception through, so the error type says what was expected to fail.
      - **`CatchAsync`** for each, over `Task`, `Task<T>` and `ValueTask<T>`.
      - **A cancellation is not an outcome**: `OperationCanceledException` passes through every
        overload. Caught, a cancelled request would read as a failed one and `Recover` would run on
        it.

      `Map`, `Bind` and the rest stay as they are: a selector that throws propagates, as it does
      now, so the one place an exception becomes data is the one that says so. A spike must answer,
      in [spikes/ResultCatch](../spikes/ResultCatch/README.md):
      - **Inference**: `Result.Catch(() => int.Parse(s))` infers `T`; `Catch<T, TException>` needs
        both type arguments spelled, since C# infers all or none, unless a different shape avoids it.
      - **The async trap**: `Result.Catch(() => client.GetAsync(url))` binds `T` to a `Task` and
        catches nothing the task throws. Whether an `[Obsolete]` error overload, a CMTK rule, or
        both, refuses it.
      - **`ToHttpResult()` over `Result<T, Exception>`** is a compile error, since an exception is
        not a fault a client acts on.
      - **Native AOT**: nothing new is reflected, and the smoke test's `aot` consumer says so.
    - **Constraints a type parameter cannot declare**, proposed 2026-10-13. C# has `where T : class`,
      `struct`, `new()` and a base type, but no `where T : abstract`, `sealed` or `record`. A generic
      base whose type argument must be an abstract record, such as the command or event family of an
      aggregate, gets no help from the compiler, and the closed type that violates it compiles.
      Attributes on a generic parameter in the base package, `[Abstract]`, `[Sealed]`, `[Record]` and
      `[Concrete]`, and one analyzer rule, CMTK0010, reported at every closed use: a base type, an
      implemented interface, a field, a parameter, a `typeof`, a call with its type arguments
      inferred. An analyzer rather than an aspect, because the violation is at the use site, which
      need not compile with Metalama, and the base package is Metalama-free ([§3](discovery.md)); an aspect on the
      parameter would see the declaration and never the use. A spike must answer, in
      [spikes/TypeArgumentConstraints](../spikes/TypeArgumentConstraints/README.md):
      - **Forwarding**: `class Sub<U> : Base<U>` where `Base<[Abstract] T>` is a violation unless `U`
        carries `[Abstract]` too, or the hole is the whole rule.
      - **Every use site** is reached, inferred type arguments and generic methods' parameters
        included, and nothing is reported twice for one site.
      - **Reflection** (`MakeGenericType`) is out of reach, and the README says so.
      - **Error in a major version** is within [decision 34](adr/0034-errors-fixed-within-major-version-warnings.md); the rule never ships at Error in a minor.
    - **`[RequireParameterlessConstructor]`** (proposed as `Ensure…` 2026-10-13; `Require…` as built, beside `[RequireCustomInitialization]`). A type the host creates by
      reflection, an endpoint, a module, an entity configuration, needs a parameterless constructor
      of a given accessibility, and C# checks that only through `new()`, which demands public. An
      attribute in the base package, on an interface or a base class, and an analyzer rule, CMTK0011,
      on every concrete type that implements or derives from it, directly or through another type,
      with a code fix that writes the constructor. An analyzer and not an aspect, although an
      `[Inheritable]` aspect introducing the constructor was the first shape considered:
      - **The check is the feature.** The constructor is one empty line; the value is the error at
        the type that lacks it, and that error has to appear in a project that never compiles with
        Metalama, where an inheritable aspect is silent, this repository's characteristic failure.
      - **A code fix beats an introduction.** An introduced constructor demands `partial` on every
        implementer and is invisible in the source; the fix writes it where the reader looks, and a
        type that declares no constructor needs nothing, since it already has a public one.
      - **What the rule checks**: a parameterless constructor of the required accessibility exists,
        or none is declared and public was asked; a record's copy constructor is not one; a wrong
        accessibility is reported at the constructor, with the fix changing it; an abstract type or
        an interface is not checked, only its concrete descendants.
      - **Public aspects stay possible** ([§5](aspects.md)): an aspect that is itself the feature may be public,
        with the attribute and its diagnostics as its surface. This one is not the first, because
        its feature is the check.
    - **Written text**, proposed 2026-10-13. Trimming, refusing the characters that render as
      nothing and composing to NFC is written again in every application that stores what a person
      typed, and `IsNullOrWhiteSpace` is what it usually gets, which lets a zero-width space, a Hangul
      filler and NUL through, and keeps a name written with `ü` and the same name written with `u` and a
      combining diaeresis as two rows of a unique index, rendering alike. Two string extensions in the base package, each returning
      `Result<string, TextFault>` with `TextFault.Blank` or `TextFault.Hidden`:
      - **`text.AsText()`**, for text a person wrote: trimmed, every space separator folded to
        `U+0020`, NFC-composed, every character that renders as nothing refused, the two zero-width
        joiners tolerated because they spell Persian, Arabic and Indic scripts and emoji sequences.
      - **`text.AsIdentifier()`**, for a value another system issued, a subject claim, a session id:
        trimmed, nothing else altered, since canonicalising somebody else's identifier changes an
        attested value, and the joiners refused too, since no issuer needs one.
      - **A null receiver is `Blank`**, since the extension is the door a bound request property goes
        through, and binding enforces no non-nullability.
      - **Two faults, not one**, because a required field makes `Blank` an error and an optional one
        makes it absence; `Hidden` is a refusal in both.
      - **The scan is by `Rune`**: a `char` loop reports `Surrogate` for a character above the BMP and
        misses the tag characters used to hide text in text; an unpaired surrogate is `Hidden`, not a
        throw, since `Normalize` and a database driver both throw on it.
      - **Not refused**: unassigned code points, since .NET's tables lag Unicode and a name spelled
        with a newly assigned character would be refused until the SDK caught up; confusable letters
        across scripts, which is a comparison-key question and not a rejection.

      No value object ships with them, and no `.Text` package either (weighed 2026-10-13). The base
      package is Metalama-free, so it cannot declare one for the generators, and a hand-written one is
      what CMTK0002 exists to refuse. A package shipping a woven one would reference `.Generators`
      with `PrivateAssets="all"` to keep Metalama away from its consumers, and would need an
      exemption in the Metalama-boundary conventions, a package-validation baseline override, a
      smoke consumer of its own under Native AOT, and a third state in the README's Metalama column.
      All of that buys one line per field, and the type is a dead end: a field that later needs a
      length limit cannot wrap it (CMTK1005) and is rewritten over `string`, and two fields of one
      shared type lose what CMTK0008 and assignment give two named types. A consumer declares its
      own in one line, `Create` being `value.AsText().Map(…)`, and names it for the field
      (`Description`, `DisplayName`). The README shows that line. The extensions live in a class
      named `StringTextExtensions`, so a consumer's value object named `Text` has no companion-class
      collision with them (CMTK1007) and nothing is ambiguous with `System.Text`. If demand for a
      ready-made type appears, a minor-version `CodoMetis.TypeKit.Text` holding `WrittenText` and
      `IssuedIdentifier` under those conditions is additive; removing one after shipping is not. No
      spike: every claim is a unit test over a string.
    - **What 2.0.0 breaks** ([decision 50](adr/0050-warnings-cmtk0003-alone-becomes-error-2.md)): CMTK0003 at Error, the one Warning raised; CMTK0010–0012
      new at Error; `FaultProblem<TFault>`'s constraint, suppressed alone against the 1.5.1 baseline
      ([decision 48](adr/0048-intended-break-suppressed-alone-baseline-stays.md)); `FirstOrNone`/`FirstOrError` binding to the query overloads, and `LastOrNone`
      on a query a compile error.
    - **What it does not wait for** ([decision 44](adr/0044-2-0-0-net10-0-metalama.md)): Metalama 2027.0 stable, .NET 11 and C# 15 unions.
      The compiler-made union types have a public `object? Value` and are serialized by
      System.Text.Json, so `Option`/`Result` take part in their pattern matching only after a spike
      shows neither returns, and that spike belongs to 3.0.0.
    - **What comes after**: the Metalama 2027.0 upgrade ([decision 4](adr/0004-metalama-2026-1-x-latest-stable.md)) joins the first release after
      it is stable, as a minor if a consumer's own aspects beside the packed generators still build
      and weave, measured as the fabric spike measured 2026.1 against the 2027.0 preview, and as a
      major otherwise; `[Durable]` returns to the `_value` placeholder with it. A `net11.0` target
      joins as a second target in a 2.x minor when a feature needs it, at the cost priced on
      2026-10-12: EF Core 11 and Microsoft.AspNetCore.OpenApi 11 in the satellites and a Native AOT
      smoke run per target. C# 15 unions and the `net10.0` drop are 3.0.0, timed to .NET 12 LTS
      (November 2027) or to the end of .NET 10's life, whichever its consumers need.
    - **The preview** (`2.0.0-preview.N`) is 2.0.0's own, from `main`, on the .NET 10 SDK and
      Metalama 2026.1, published to nuget.org once the closed-set family and the fault responses
      exist, so the surface gets consumer feedback before the tag. The release workflow takes a
      preview tag as it is, since `v2.0.0-preview.1` passes the tag check against `Version` and the
      changelog check against a dated `## 2.0.0-preview.1` section. The measurement of unions and
      Metalama 2027.0 on the .NET 11 SDK moves to a 3.0.0 spike on a branch of its own, with no
      release attached.
