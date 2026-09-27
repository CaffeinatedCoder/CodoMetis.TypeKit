# Plan: CodoMetis.TypeKit

Status: **in progress, 2026-09-27.** Phases 0 to 7 are done; 0.1.0 waits on the repository going
public. The decisions are in §8, Native AOT in §11. The fabric spike
([spikes/FabricSpike](../spikes/FabricSpike/README.md)), the translation comparison
([spikes/ValueTranslation](../spikes/ValueTranslation/README.md)), the EF mapping spike
([spikes/EfMapping](../spikes/EfMapping/README.md)) and the OpenAPI spike
([spikes/OpenApiSchemas](../spikes/OpenApiSchemas/README.md)) are done.

## 1. Packages

| Package | Depends on | Metalama | Contents |
|---|---|---|---|
| `CodoMetis.TypeKit` | Analyzers (flows, §10) | **no** | `Option<T>`, `Result<T,TError>`, `Result<TError>` and their extensions, all carrying `NotWireTypeJsonConverterFactory` (§9); the value-object contracts `IValueObject<,>`, `IValueWrapper<,>`, `IValue<T>`, `IValidatedValue<,,>`, `IValueObjectMaterializer<,>` (§4), `KnownGood`; `TranslatedAsUnderlyingValueAttribute`, `RequireCustomInitializationAttribute`; `GeneratedValueObjectAttribute<,>` and `IValueObjectVisitor<>`, which the satellites read (§3, §11); `GuidValueExtensions` (`OrderId.New()` from a version 7 Guid); `Accepted`, `GeneratedParsing`, `GeneratedJson` and `IStoredJsonConverterSource`, which the generated code calls or implements (§5) |
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
| `CodoMetis.TypeKit` | `Option`, `Result`, `NotWireTypeJsonConverterFactory`, `IValueObject`, `IValueWrapper`, `TranslatedAsUnderlyingValueAttribute`, `GeneratedValueObjectAttribute`, `IValueObjectVisitor` |
| `CodoMetis.TypeKit.ValueObjects` | `IValue`, `IValidatedValue`, `KnownGood`, `IValueObjectMaterializer`, `IStoredJsonConverterSource` |
| `CodoMetis.TypeKit.Attributes` | `RequireCustomInitializationAttribute` |
| `CodoMetis.TypeKit.EntityFrameworkCore` | converter, convention, translators |
| `CodoMetis.TypeKit.AspNetCore` | `AddTypeKit()` |

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
   - Found in the fourth review on 2026-09-27, and fixed: `Option`/`Result` serialized as `{}` and
     `{"State":1}` and read back as `None` and uninitialized, silently. Every exported struct now
     carries `NotWireTypeJsonConverterFactory` (§9). Guard: with the attributes removed, 58 of the 62
     `NotWireTypeTests` fail, the other 4 pin the escape hatch and the absent-property gap.
2. **Value-object contracts and analyzer. ✅ Done 2026-09-27.**
   - The contracts: `IValueObject<,>`, `IValueWrapper<,>`, `IValue<>`, `IValidatedValue<,,>`,
     `IValueObjectMaterializer<,>`, `KnownGood`, `TranslatedAsUnderlyingValueAttribute`, and
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
     exactly CMTK1000–1008 and CMTK0001, nothing else, and no CMTK0002 beside the real generators.
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

## 3. Discovery is by interface

No namespace strings, assembly-name prefixes or type-name lists anywhere. A name-based check
passes in tests and breaks for the first consumer who renames something.

| Where | How |
|---|---|
| Analyzer, value-object rules | `CompilationStartAction` resolves `CodoMetis.TypeKit.ValueObjects.IValue`1` / `IValidatedValue`3` with `GetTypeByMetadataName` and compares `OriginalDefinition` with `SymbolEqualityComparer`. If `CodoMetis.TypeKit` is absent, nothing is registered |
| Analyzer, `[RequireCustomInitialization]` | The attribute symbol is resolved the same way |
| Analyzer, CMTK0002 | The compilation references an assembly whose identity is exactly `CodoMetis.TypeKit.Generators`. That is the package's own id, which a consumer cannot rename, and a prefix or suffix match never counts |
| EF Core | EF asks the type-mapping plugin per CLR type, and it answers for any type carrying `GeneratedValueObjectAttribute<TSelf, T>` (§4). There is no type scan and no assembly filter |
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
  is still converted.
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
- **Entry points.** The implementation aspect introduces three private helpers on every value
  object, and they are the only way the JSON, parsing and type-converter aspects create an
  instance: `__FromJson` (a refusal throws `JsonException`), `__FromText` (`FormatException`) and
  `__TryFromText` (`false`). A plain value object constructs directly; a validated one calls
  `Create` through `Accepted` in the base package. The exception is the explicit
  `IValueObjectMaterializer.Materialize`, by contract. `MinValue`/`MaxValue` are only generated for
  a plain value object.
- **Declarations that cannot be generated** are errors, so no type is left half-generated:
  CMTK1000/1001/1002 (not `partial`, not a record, a struct not `readonly`), CMTK1003 (more than one
  marker), CMTK1004 (a validated marker whose first type argument is another type), CMTK1005 (a
  generic value object, one that derives from another value object, a wrapped type that is not
  a class, struct or enum, or one that is a value object: itself, one that reaches it again, or
  any other, see below), CMTK1006 (a record class that is not `sealed`: a derived record compares
  equal only to its own type, which is not value equality), CMTK1007 (the name of the
  `GetValue`/`ValueOrNull` class is taken, see below).
- **The companion class** holding `GetValue()`/`ValueOrNull()` sits at namespace level, as
  extension methods must, and is named after the whole nesting chain: `Order.Id` gets
  `OrderIdExtensions`. Named after the value object alone, `Order.Id` and `Customer.Id` both asked
  for `IdExtensions` and Metalama crashed (LAMA0001). A name that a declared type or another value
  object's companion already has is CMTK1007, naming it.
- **NodaTime.** Only `ValueObjectJsonAspect` handles NodaTime types, and it must not refer to
  NodaTime at compile time, which would force NodaTime on every consumer. It resolves the types
  with `TypeFactory.TryGetType` from the consumer's compilation, compares them by symbol, and uses
  NodaTime's converters (values and dictionary keys) only when
  NodaTime.Serialization.SystemTextJson is referenced too. Otherwise the fallback serializes
  through the options. Parsing reaches NodaTime types through their `[TypeConverter]`, via
  `GeneratedParsing.TypeConverterOf<T>()` (`TypeDescriptor.RegisterType` and
  `GetConverterFromRegisteredType`, the trim-safe lookup).
- **Explicit `IParsable`.** `bool` and `char` implement their parsing interfaces explicitly, so the
  generated code calls the wrapped type's `Parse`/`TryParse` through `GeneratedParsing`, whose
  constrained type parameters reach an explicit implementation.
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
  the options needs reflection. A key uses `GeneratedJson.KeyConverter<T>`.
- **JSON reads never parse by rules of their own.** A value is read by the reader's methods
  (`GetGuid`, `GetDateTime`) or, for `DateOnly`/`TimeOnly`, the serializer's built-in converter; a
  dictionary key by the built-in converter's `ReadAsPropertyName`
  (`JsonMetadataServices.Int32Converter` and so on). Both accept exactly what the serializer
  accepts for the wrapped type, and report malformed text as `JsonException`. A `Parse` in the
  generated code let `FormatException` escape, which a minimal API answers with 500 rather than 400,
  and read number keys with `NumberStyles.Any` ("1,000" as 1000).
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
    breaks nobody, while adding one after 0.1.0 would.
  - The satellites keep their own refusal (§7): a value object declared by hand, with the
    interfaces and `GeneratedValueObjectAttribute<,>` the satellites read (§3), can still wrap itself.
- **A fallback JSON key goes through the wrapped type's own converter**
  (`options.GetConverter(typeof(T))`), which knows the type's key format where it has one (an enum
  by name, a `Uri` as its text) and throws `NotSupportedException` where it has none. Writing the
  serialized value as the property name gave a `Uri` key quotes inside its quotes and an enum key
  its number, and neither read back.
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
  [spikes/ConcurrentNamespaceTypes](../spikes/ConcurrentNamespaceTypes/README.md)).** The
  instances of one aspect layer run in parallel on one code model, and the CMTK1007 check, reading
  the namespace's types from the extensions aspect while sibling instances introduced their
  companion classes into it, missed a declared type in 5 of 60 builds. With Metalama's concurrent
  build off it never missed. What owns a companion name is now answered in the fabric, before any
  introduction, and reaches the aspect through its constructor and the aspect state: 0 misses in
  48. Anything that has to look beyond the aspect's own target goes the same way.
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
   instead of shipping silently. Once 0.1.0 ships, package validation against the last release
   guards the hand-written surface too.
3. **Generated SQL.** `ToQueryString()` snapshots for `.Value`, `GetValue`, `ValueOrNull`,
   `StartsWith`, equality, and `Contains` over a list of ids.
4. **OpenAPI.** A probe host's emitted document, asserted per shape against a control that uses
   the wrapped type directly, so the tests follow what ASP.NET publishes for that type (§7).

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
- **ASP0020** (decision 12): a minimal-API route parameter whose value object is declared in the
  same project fails the build, because the route analyzer reads the source before Metalama weaves
  `IParsable` in. Binding is correct at run time and in the request delegate generator. A
  `DiagnosticSuppressor` has no effect under Metalama's compiler, and Metalama's own suppression is
  scoped to the aspect's targets, so the package README documents the pragma instead.

Vogen and Thinktecture document Swashbuckle only (checked 2026-09-27). The README describes what
the package does and claims nothing about other libraries.

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
15. **`Option`/`Result` are not wire types** (2026-09-27, §9): a `[JsonConverter]` on every exported
    struct of the base package throws `NotSupportedException` in both directions. Not a lossless
    converter, and not an analyzer.
16. **Native AOT wherever the platform allows it** (2026-09-27, §11): the run-time packages build with
    `IsAotCompatible`, the woven code is published with Native AOT in the smoke test, and a path that
    needs reflection or dynamic code is rewritten rather than annotated or suppressed. EF Core itself
    stays experimental under Native AOT; the satellite adds nothing to what EF requires.
17. **Run-time discovery reads `GeneratedValueObjectAttribute<TSelf, T>`** (2026-09-27, §3, §11),
    which the generators put on every value object, not `GetInterfaces()`. Measured under Native AOT:
    the interface was gone from every value object reached through a property, and the OpenAPI
    satellite described each as `{}`. The attribute's constraints are the interfaces, so what a value
    object is has not changed; its `Accept` hands the two types to a visitor as type arguments, which
    is how the EF satellite makes a converter without `MakeGenericType`.

Still open:

13. **Analyzer first-release rule set** (§10 proposes CMTK0001–0005 plus code fixes).
14. **context7.json.** The sibling repos register one. Add it once the repo is public.

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
  `Success { }` and `Error { }`. Only the implicit conversions read it. Both shapes accept both
  markers: `Result<TError>` converts from `Result.Error(e)` as well as from a bare error, so a
  method can `return Result.Error(fault);` whichever shape it returns.
- **`Result<T, TError>` converts from a bare value, never from a bare error** (measured
  2026-09-27). With both conversions, `Result<long, int> r = 5;` compiles and is an **error**:
  `int` is the more specific source type, so C# picks the error conversion. The READMEs had shown
  `return Fault.X;` in valued methods, which does not compile; the consumer smoke test found it.
- **`Option.Some(null)` throws.** `notnull` is an annotation the runtime does not enforce, and a
  `Some` over null reported a value it could not hand out. `Map` and the zips go through `Some`,
  so a selector that returns null throws too.
- **So do `Result`'s factories.** Both shapes and both markers constrain their content to
  `notnull`, and `Success(null)`/`Error(null)`/`Result.Ok(null)`/`Result.Error(null)` throw
  `ArgumentNullException`: a result over null handed it out of `TryGetValue`/`TryGetError` despite
  `[NotNullWhen]`. `Map`, `Bind`, the collapsing `Match` and the conversions go through them.
- **`Result<TError>.Match` hands the error to its error branch.** The only overload took a
  parameterless `onError`, so `TryGetError` was the only way to the error. The parameterless one
  stays, for a branch that does not need it.
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
| CMTK0001 | No `default`/`default(T)`/`new()`/`new T()` of a value object or a `[RequireCustomInitialization]` type | Error | done (unshipped) |
| CMTK0002 | Type implements `IValue<>`/`IValidatedValue<,,>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so it is never woven | Error | done (unshipped) |
| CMTK0003 | `Result`/`Option` return value ignored (expression statement, including an awaited `Task<Result<…>>`; `_ =` is the explicit opt-out). CA1806 can only enforce this per method name via `additional_use_results_methods`, not per return type | Warning | proposed |
| CMTK0004 | `IValueObjectMaterializer<,>.Materialize` or `ValueObjectConverter<,>.Materialize` called outside `CodoMetis.TypeKit.EntityFrameworkCore` and EF's generated compiled model. Enforces the validation-free-path invariant in SECURITY.md (the other sanctioned path, `StoredJsonConverterFactory`, reaches the converter's materializing mode through `IStoredJsonConverterSource`, not `Materialize`) | Error | proposed |
| CMTK0005 | Array of a no-default type created with a length (`new OrderId[n]`, `GC.AllocateUninitializedArray`): every element starts as `default` | Warning | proposed |
| CMTK0006 | Field or auto-property of a no-default type in a class, not `required`, no initializer, not assigned in every constructor. This is the CS8618 equivalent nullable analysis does not give structs, and the largest remaining way to get a `default` value object | Info → Warning | **measure noise first** on EF entities with private parameterless constructors |
| CMTK0007 | `FromKnownGood` called with a non-constant argument | Info | **needs design**: "a value the caller just produced" is legitimate |
| — | Code fixes for the aspect's shape diagnostics CMTK1000–1002 (missing `partial`/`record`/`readonly`) | — | proposed. The aspect keeps its own error as a backstop |

**Release discipline.** Keep `AnalyzerReleases.Shipped/Unshipped.md` tracking (RS2008). A new rule
ships at Warning or Info in a minor version and is raised to Error only in a major. Consumers
build with warnings as errors, so a new Error rule in a minor version would break their builds on
update.

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

