# Probe axes: CodoMetis.TypeKit

The `prober` agent reads this file at the start of every round. It says how to write and run a probe
here, where the claims come from, which external APIs to enumerate, which type shapes to generate,
what to compare against, and what is intended. Everything specific to this repository lives here;
the agent itself is generic.

## Harness

Write probes as xunit theories in **`test/CodoMetis.TypeKit.Samples`**, in a folder `Probes/` of the
scratch copy, one file per claim, class name ending in `Probe`, rows fed through `[MemberData]` from
the covering array (`prober rows`), read from a JSON file beside the probe. That project is the
closest thing to a consumer: it references every package, weaves value objects through the real
fabric, and loads the analyzer as an analyzer. A probe file may declare its own value objects
(`readonly partial record struct ProbeId : IValue<Guid>;`); the declarations of the type-shape
catalogue, `tools/probe/corpus/harness/*.g.cs`, are written by the file-based apps of
`tools/probe/generators/` (`dotnet run tools/probe/generators/<app>.cs -- ...`, its README), never by hand,
and `GeneratedHarnessTests` in the conventions tests holds each file to its app's output.

- Start a probe with `prober new --claim <id>` and iterate with `prober try --probe <P> [--rows i,j,...]`
  (tools/Prober/README.md, "Writing a probe"): `new` stages the skeleton in this harness's form, with the
  claim's obligations quoted and a throwing check each, the probe's own rows and the registry entry; `try`
  builds the corpus and the staging in the replay's worktree and runs that one probe, printing each row's
  outcome and recording nothing. Never copy the corpus into a worktree or write a build or sync script.
- Build and run only through the verdict tool, which pins English output, builds before it tests,
  names the exit codes and prints failing tests by name with their assertion:
  `dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll build test/CodoMetis.TypeKit.Samples`
  (cold, about 1 to 2 minutes), then `dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test
  --project test/CodoMetis.TypeKit.Samples --filter-class "*Probe"`. Build the tool first, `dotnet build
  tools/BuildVerdict -c Release`: a fresh worktree has no dll and a stale one refuses (exit 3).
  The full log is at `$(git rev-parse --absolute-git-dir)/build-verdict-last.log`.
- Warnings are errors. Add `#pragma warning disable xUnit1051` at the top of a probe that awaits
  without a cancellation token; an obsolete setting (`IgnoreNullValues`) needs its `SYSLIB` id
  suppressed. A probe that deliberately uses something the analyzer refuses suppresses that one CMTK
  id around the line and says why.
- HTTP probes: `WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName =
  Environments.Production })` (Development adds the developer exception page, and a refusal then
  arrives as a 500), `builder.WebHost.UseTestServer()`, the JSON options through
  `builder.Services.ConfigureHttpJsonOptions(...)`, then `app.StartAsync()` and `app.GetTestClient()`.
  The OpenAPI document is at `/openapi/v1.json` after `builder.Services.AddOpenApi(o => {
  o.OpenApiVersion = ...; o.AddTypeKit(); })` and `app.MapOpenApi()`. A controller must be `internal`
  and added through an `IApplicationFeatureProvider<ControllerFeature>`; a public one is discovered by
  every other host in the project. A host with controllers must put its own converters and resolvers
  on MVC's `JsonOptions` too, or the document is refused on purpose (`DescribedOptionsAgreement`).
  `test/CodoMetis.TypeKit.AspNetCore.Tests/ProbeHost.cs` is a working example to copy from.
- How a row ends: a check returns null or the failure text, and runs through `Rec.Run`, which fails the
  test for a violation or a harness error and skips it, with its reasons, for any other outcome but a
  pass; a check that throws ends as a harness error, never a violation, and no seed counts it caught. A check
  says what it saw with `r.Note(NoteKeys.Name, detail)`. The key, a member of `NoteKeys` in `harness/Row.cs`,
  alone decides the outcome. Keys are constants the compiler checks: a new one goes into `NoteKeys` with its
  outcome on one line, and a string, a misspelled member or a key built from text does not compile (decision
  103). A probe outside the namespace `Probes.Shapes` writes `using NoteKeys = Probes.Shapes.NoteKeys;`.
  Never match a failure's text to excuse it. A difference the framework makes on its own is shown by a
  baseline in the same row (the document as ASP.NET writes it without `AddTypeKit()`,
  `Hx.DocRefusedByAspNetAlone`), not by a substring. A row that would fail for an open finding calls
  `r.Skip("F7", why)`, the finding as a string literal: `register` and `publish` refuse a skip whose finding
  is built from text or is not open in the ledger, naming the file and line. The gate refuses while any row
  skips, and the replay fails a skip whose finding the ledger has fixed or dismissed.
- EF probes: SQLite in memory (`Data Source=:memory:` with the connection held open). PostgreSQL needs
  a server and is out of reach.
- Analyzer probes (what compiles and what CMTK reports): in `test/CodoMetis.TypeKit.Analyzers.Tests`,
  a `CSharpCompilation` over `RealTypeKit.References` (`AnalyzerTest.cs`) with the analyzers attached,
  asserting the diagnostics. The type-shape catalogue is the row there, as source text.
- Native AOT is measured only by `./test/consumer-smoke-test.sh` (minutes, needs clang). Probe it only
  when the caller asks for it.

## Seeds: every probe shows it can fail

A probe that passes proves something only if it can fail. Every registered probe names one seeded
product defect ([decision 100](../design/adr/0100-every-probe-is-shown-to-fail-against-a-seeded-defect.md)):
a patch against `src/` that breaks what its claim promises, in `tools/probe/corpus/seeds/<claim>.patch`,
and a sentence saying what it breaks, in its registry entry:

```json
"seed": { "patch": "seeds/L01-json-bytes.patch", "defect": "A dictionary key is written with ToString(), not the wrapped type's key converter and DictionaryKeyPolicy." }
```

- Write the seed with the probe. Make the defect in `src/` of the scratch copy, take `git diff -- <file>`
  as the patch, undo it by inverse edit (`git apply -R <patch>`), and register with `prober register ...
  --seed seeds/<claim>.patch --defect "<sentence>"`. An environment toggle (`"env": {...}`, set for the
  build and every run) is a seed too, where the product reads one; none does today.
- Seed the defect the claim is about, the one a consumer would meet: a facet dropped, a refusal that
  quotes its input, a rule skipped, a key no longer generated. A seed whose build fails proves nothing,
  and the command counts it as a failure.
- `prober seeds [--probe P]` applies each seed in a scratch worktree of its own (`replay --where` names
  it), builds, replays the probe there and requires a row that ends as a violation the unseeded probe
  does not have; a missed seed fails it. It records no evidence and takes no lock. The worktree is per
  checkout, so runs in two checkouts (a linked worktree each) never meet, but two runs at once in one
  checkout share it. One build per seed, so it runs locally, never in CI: after writing a probe or
  changing its harness, and before a release.
- Without a build, the prober tests hold the rest on every run (`SeedsTests`): every registered probe
  has a seed, and its patch touches only `src/` and still applies. A product change that moves the
  patched lines fails there; make the seed again against the new code.
- A seed per obligation ([decision 104](../design/adr/0104-a-claim-states-its-obligations-and-each-has-a-seed.md)).
  A seed that breaks some assertion of a probe shows the probe can fail, not that the claim is checked, so
  a probe whose claim lists `obligations` carries `seeds`, one per obligation it checks, each a seed as
  above with an `obligation` naming it; `seed` may name one too. A seed that names none counts only for a
  claim without `obligations`. Register each with `prober register --probe P --claim C --filter "*P"
  --obligation <id> --seed seeds/<claim>-<id>.patch --defect "<sentence>"`, the command `prober new` prints
  for each obligation: it replaces a seed of the entry that names the same obligation and refuses an id the
  claim does not list. Never edit the staged registry by hand. `prober seeds` runs each seed of a probe on its own and reports it as
  `P/<obligation>`, in its output and in `seeds-last.json`; `prober status` shows per claim its obligations
  and the ones no seed names; and `SeedsTests` refuses, without a build, a seed naming an obligation its
  claim does not list and a listed obligation of a claim with probes that no seed of them names.

## Claim sources

In this order: the root `README.md` ("What TypeKit guarantees" first), `docs/*.md`, the package
READMEs (`src/*/README.md`), then `design/adr/*.md` for intent. Statements in `docs/` and the READMEs
are promises to consumers; a design record explains, and decides what is intended.

## Axes

The axes are data, read by `prober universe` (tools/Prober), which builds each claim's
axes from its surfaces. The prober assigns surfaces to a claim; it does not choose axes.

- **Generated axes** come from `tools/probe/enumerate.cs`, which enumerates by reflection every
  public settable property of `JsonSerializerOptions` (`json.*`), `OpenApiOptions` (`openapi.*`) and
  `MvcOptions` (`mvc.*`) at the versions this repository pins. `prober replay` runs it
  outside the repository (so neither `global.json` nor central package management applies to it), with
  its package versions taken from `Directory.Packages.props`, whenever a dependency input changes, and
  `--all` always does. A setting whose type is not a bool or an enum takes its values from `hand` below.
- Every generated axis is either reached by a surface's pattern or listed under `ignored` with the
  reason. The script reports any other as **unassigned**: a setting a new framework version added,
  which needs a decision here.
- `shape.*` is the type-shape catalogue: the forms a consumer's own type takes. Generate the
  declarations a round needs with an app of `tools/probe/generators/` (extend one, or add one and its row
  in `GeneratedHarnessTests`), once per round, as one pairwise set shared by every claim.
- A claim may drop a surface axis only by quoting the document sentence that limits it (`narrow`).
- A claim may drop pairs no row can hold, or that the documents do not promise, only with the reason
  (`exclude`: a list of `{"reason": "...", "pairs": [["a=1", "b=2"], ...]}`): one sentence, citing the docs
  sentence, the record or the KB fact where one applies. Nothing checks a reason mechanically, so
  `prober universe` prints each one beside the claim's narrowings for a reviewer, and the prober refuses a
  claims.json with any field its schema (`tools/Prober/Schemas/claims.schema.json`) does not have
  ([decision 102](../design/adr/0102-an-exclusion-carries-its-reason.md)).
- `refused` drops a value the framework itself refuses on its own, so no row wastes its pairs on it.
  `shape.refused-kind` holds the declarations the generators refuse; only the generation surface takes
  it, where the refusal is the claim.
- A claim lists what it promises in `obligations`, one entry per promise a probe can check on its own: an
  `id`, the `sentence` quoted verbatim from the claim's source and checked as a narrowing is, and the `axes`
  values it concerns where it is not the whole claim. A claim without the field has one obligation, the
  claim itself, under the id `claim`. Each obligation of a claim with probes has a seed that names it
  ([decision 104](../design/adr/0104-a-claim-states-its-obligations-and-each-has-a-seed.md)).
- What a claim does not promise goes in `not-promised`: an `id`, the `source` (`file#heading`) of the
  document or record that makes the difference intended, its `sentence`, quoted verbatim and checked against
  that source, and the `axes` values it concerns. A note of outcome expected-refusal, a narrowing or an
  applicability condition cites the entry by its id. `prober universe` refuses an obligation or an entry
  whose sentence its document does not hold, and an id listed twice, naming the claim and the id.
- `out-of-reach` names a surface no probe reaches, with the reason: one that needs a server, a toolchain
  or minutes a replay does not have. `prober gaps` prints it beside the settings no surface reaches, the
  settings without values, the refused shapes and values, and every not-promised entry and narrowing, each
  with its recorded reason. It is a report for the release checklist and a feature-research round; it
  decides nothing and runs no model.
- `http.host` has two values without `[ApiController]`, where MVC infers no `[FromBody]` and answers no
  automatic 400. `controller-without-ApiController` is for **runtime claims only**: ASP.NET's API explorer
  leaves such a controller out of the document (docs/aspnet-core.md, "Bind value objects in MVC"), so a
  document check has nothing there to read. A document (OpenAPI) claim lists `http.host` in its own
  `axes` in `claims.json` without it. `controller-without-ApiController-visible` hosts the same
  controllers made visible by an `IApplicationModelConvention` that sets `ApiExplorer.IsVisible = true`
  (`VisibleToApiExplorer` in the harness), so document checks reach them; every http claim takes it.
- `process.*` holds what other code in the same process may have done first (2026-10-15: a wrapped
  type's `TypeConverter` looked up before the value object's parsing broke that parsing).

```json probe-axes
{
  "hand": {
    "json.PropertyNamingPolicy": [
      "CamelCase",
      "SnakeCaseLower",
      "SnakeCaseUpper",
      "KebabCaseLower",
      "KebabCaseUpper",
      "own-upper-case"
    ],
    "json.DictionaryKeyPolicy": [
      "CamelCase",
      "SnakeCaseLower",
      "KebabCaseUpper",
      "own-upper-case"
    ],
    "json.ReferenceHandler": [
      "Preserve",
      "IgnoreCycles"
    ],
    "json.Converters": [
      "own-for-wrapped",
      "own-for-value-object",
      "JsonStringEnumConverter",
      "StoredJsonConverterFactory"
    ],
    "json.TypeInfoResolver": [
      "source-generated-context",
      "default-with-renaming-modifier"
    ],
    "json.TypeInfoResolverChain": [
      "context-then-default"
    ],
    "json.Encoder": [
      "UnsafeRelaxedJsonEscaping"
    ],
    "json.MaxDepth": [
      "3"
    ],
    "json.DefaultBufferSize": [
      "1"
    ],
    "json.IndentCharacter": [
      "tab"
    ],
    "json.IndentSize": [
      "1"
    ],
    "json.NewLine": [
      "lf"
    ],
    "openapi.CreateSchemaReferenceId": [
      "own-prefixing",
      "null-inlines-all"
    ],
    "openapi.ShouldInclude": [
      "default-only"
    ],
    "openapi.transformers": [
      "own-schema-transformer",
      "own-operation-transformer",
      "own-document-transformer"
    ],
    "mvc.InputFormatters": [
      "xml-added"
    ],
    "mvc.OutputFormatters": [
      "xml-added"
    ],
    "mvc.ModelBinderProviders": [
      "own-provider-first"
    ],
    "mvc.Conventions": [
      "own-route-prefix"
    ],
    "mvc.MaxModelBindingCollectionSize": [
      "2"
    ],
    "mvc.MaxModelValidationErrors": [
      "1"
    ],
    "mvc.MaxValidationDepth": [
      "1"
    ]
  },
  "ignored": {
    "json.WriteIndented": "layout only; JSON parity compares under the same options",
    "json.IndentCharacter": "layout only",
    "json.IndentSize": "layout only",
    "json.NewLine": "layout only",
    "json.DefaultBufferSize": "buffering only; DefaultBufferSize 1 is kept in hand for a stress round on request",
    "openapi.ShouldInclude": "selects endpoints, never a schema's shape",
    "mvc.EnableActionInvokers": "invocation pipeline, not binding or formatting",
    "mvc.EnableEndpointRouting": "TypeKit supports endpoint routing only",
    "mvc.RequireHttpsPermanent": "transport",
    "mvc.SslPort": "transport",
    "mvc.CacheProfiles": "caching headers",
    "mvc.SuppressAsyncSuffixInActionNames": "action naming",
    "mvc.SuppressInputFormatterBuffering": "buffering",
    "mvc.SuppressOutputFormatterBuffering": "buffering",
    "mvc.MaxIAsyncEnumerableBufferLimit": "streaming",
    "mvc.MaxModelBindingRecursionDepth": "recursion limit of binding, reached only by self-referencing models",
    "mvc.ModelMetadataDetailsProviders": "covered by own-provider-first on binders",
    "mvc.ModelValidatorProviders": "validation providers; TypeKit validates in Create",
    "mvc.ValueProviderFactories": "value sources; the binding axis covers them"
  },
  "axes": {
    "shape.kind": [
      "record-struct",
      "sealed-record-class",
      "validated-struct",
      "validated-class",
      "closed-set"
    ],
    "shape.site": [
      "top-level",
      "nested-in-class",
      "nested-in-struct",
      "nested-two-levels",
      "in-generic-type"
    ],
    "shape.inheritance": [
      "none",
      "base-depth-1",
      "base-depth-2",
      "base-depth-3",
      "base-explicit-IEquatable",
      "explicit-interface-member"
    ],
    "shape.wrapped": [
      "string",
      "Guid",
      "int",
      "long",
      "byte",
      "ulong",
      "decimal",
      "double",
      "bool",
      "char",
      "DateTime",
      "DateTimeOffset",
      "DateOnly",
      "TimeOnly",
      "TimeSpan",
      "enum",
      "flags-enum",
      "Uri",
      "NodaTime.Instant",
      "own-record-class"
    ],
    "shape.position": [
      "root",
      "property",
      "nullable-property",
      "field",
      "list-element",
      "array-element",
      "immutable-array-element",
      "read-only-list-element",
      "dictionary-key",
      "dictionary-value",
      "record-ctor-parameter",
      "required-init",
      "in-patch",
      "polymorphic-derived-property"
    ],
    "shape.containing": [
      "class",
      "record-class",
      "struct",
      "record-struct",
      "generic-type"
    ],
    "input.json": [
      "valid",
      "refused-by-create",
      "wrong-token",
      "null",
      "empty-string",
      "at-type-limit",
      "quoted-number",
      "escape-sequence",
      "duplicate-property",
      "metadata-property"
    ],
    "http.host": [
      "minimal",
      "mvc",
      "both",
      "route-group",
      "controller-without-ApiController",
      "controller-without-ApiController-visible"
    ],
    "http.binding": [
      "route",
      "query",
      "header",
      "body",
      "form",
      "as-parameters"
    ],
    "http.response": [
      "result-to-http-result",
      "composed-fault",
      "two-faults-same-name",
      "group-with-fault-status",
      "endpoint-without-faults",
      "declared-produces-problem"
    ],
    "ef.query": [
      "where-equals",
      "where-value-compare",
      "order-by",
      "contains",
      "first-or-none",
      "select",
      "group-by",
      "execute-update",
      "compiled-query",
      "as-no-tracking"
    ],
    "ef.mapping": [
      "property",
      "key",
      "integer-key",
      "nullable",
      "owned",
      "complex",
      "closed-set"
    ],
    "text.entry": [
      "Parse",
      "TryParse",
      "TryFrom",
      "type-converter",
      "IParsable",
      "ISpanParsable",
      "ToString-format",
      "TryFormat-short-span"
    ],
    "text.culture": [
      "invariant",
      "de-DE",
      "tr-TR",
      "ar-SA",
      "globalization-invariant"
    ],
    "or.shape": [
      "Option",
      "Result-T-E",
      "Result-E",
      "Task-of-Result",
      "ValueTask-of-Result"
    ],
    "or.operation": [
      "Match",
      "Bind-Map-chain",
      "query-syntax",
      "Sequence-Transpose",
      "Result.Catch",
      "FirstOrNone",
      "default-instance",
      "after-await-elsewhere"
    ],
    "shape.refused-kind": [
      "unsealed-record-class (CMTK1006)",
      "over-value-object (CMTK1005)",
      "generic-wrapped (CMTK1005)",
      "abstract (CMTK1005)"
    ],
    "process.converter-lookup": [
      "none",
      "wrapped-type-converter-looked-up-first"
    ],
    "http.nullability": [
      "as-declared",
      "nullable"
    ]
  },
  "surfaces": {
    "json": {
      "axes": [
        "json.*",
        "shape.kind",
        "shape.wrapped",
        "shape.position",
        "shape.containing",
        "input.json"
      ],
      "watched": [
        "src/CodoMetis.TypeKit.Generators",
        "src/CodoMetis.TypeKit/ValueObjects",
        "src/CodoMetis.TypeKit/DecimalJsonConverters.cs",
        "src/CodoMetis.TypeKit/Patch.cs"
      ]
    },
    "http": {
      "axes": [
        "json.*",
        "openapi.*",
        "mvc.*",
        "http.*",
        "process.*",
        "shape.kind",
        "shape.wrapped",
        "shape.position",
        "shape.containing",
        "input.json"
      ],
      "watched": [
        "src/CodoMetis.TypeKit.AspNetCore",
        "src/CodoMetis.TypeKit/Fault.cs",
        "src/CodoMetis.TypeKit/FaultOfSets.cs",
        "src/CodoMetis.TypeKit.Generators"
      ]
    },
    "ef": {
      "axes": [
        "ef.*",
        "shape.kind",
        "shape.wrapped",
        "shape.site"
      ],
      "watched": [
        "src/CodoMetis.TypeKit.EntityFrameworkCore",
        "src/CodoMetis.TypeKit.Generators"
      ]
    },
    "text": {
      "axes": [
        "text.*",
        "process.*",
        "shape.kind",
        "shape.wrapped"
      ],
      "watched": [
        "src/CodoMetis.TypeKit/StringTextExtensions.cs",
        "src/CodoMetis.TypeKit/TextFault.cs",
        "src/CodoMetis.TypeKit.Generators"
      ]
    },
    "generation": {
      "axes": [
        "shape.*"
      ],
      "watched": [
        "src/CodoMetis.TypeKit.Generators",
        "src/CodoMetis.TypeKit/ValueObjects"
      ]
    },
    "option-result": {
      "axes": [
        "or.*",
        "shape.wrapped"
      ],
      "watched": [
        "src/CodoMetis.TypeKit/Option.cs",
        "src/CodoMetis.TypeKit/OptionExtensions.cs",
        "src/CodoMetis.TypeKit/Result.cs",
        "src/CodoMetis.TypeKit/ResultT.cs",
        "src/CodoMetis.TypeKit/ResultExtensions.cs",
        "src/CodoMetis.TypeKit/SequenceOptionExtensions.cs",
        "src/CodoMetis.TypeKit/ValueTypeOptionExtensions.cs"
      ]
    },
    "analyzers": {
      "axes": [
        "shape.*"
      ],
      "watched": [
        "src/CodoMetis.TypeKit.Analyzers",
        "src/CodoMetis.TypeKit/TypeArgumentConstraints.cs"
      ]
    },
    "logging": {
      "axes": [
        "shape.kind",
        "shape.wrapped",
        "input.json"
      ],
      "watched": [
        "src/CodoMetis.TypeKit.Serilog",
        "src/CodoMetis.TypeKit/Refusals.cs"
      ]
    }
  },
  "out-of-reach": {
    "postgresql": "needs a server; the EF probes run SQLite in memory, and the EF translation claims are checked by translation alone",
    "native-aot": "measured only by the consumer smoke test (tools/ConsumerSmokeTest), minutes and a platform linker; no probe publishes with Native AOT"
  },
  "refused": {
    "shape.site": {
      "in-generic-type": "CMTK1005: the generators refuse a value object nested in a generic type at build (Generators README, Build errors)"
    },
    "json.ReadCommentHandling": {
      "Allow": "System.Text.Json refuses it on the options object"
    },
    "json.DefaultIgnoreCondition": {
      "Always": "System.Text.Json refuses it on the options object"
    }
  },
  "replay": {
    "project": "test/CodoMetis.TypeKit.Samples",
    "probeDir": "test/CodoMetis.TypeKit.Samples/Probes",
    "prepare": [
      [
        "dotnet",
        "build",
        "tools/BuildVerdict",
        "-c",
        "Release",
        "-nologo",
        "-v:q"
      ]
    ],
    "build": [
      "dotnet",
      "tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll",
      "build",
      "{project}"
    ],
    "test": [
      "dotnet",
      "tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll",
      "test",
      "--no-build",
      "--project",
      "{project}",
      "--filter-class",
      "{filter}",
      "--",
      "--results-directory",
      "{results}"
    ],
    "coverage": [
      "--coverage",
      "--coverage-output-format",
      "cobertura",
      "--coverage-output",
      "{cobertura}"
    ],
    "families": {
      "http": [
        "http"
      ],
      "ef": [
        "ef"
      ],
      "json": [
        "json"
      ],
      "text": [
        "text",
        "logging"
      ],
      "build": [
        "generation",
        "analyzers",
        "option-result"
      ]
    },
    "include": [
      "src"
    ],
    "compileTimeFootprint": [
      "src/CodoMetis.TypeKit.Generators",
      "Directory.Packages.props",
      "Directory.Build.props",
      "global.json",
      "test/CodoMetis.TypeKit.Samples/CodoMetis.TypeKit.Samples.csproj"
    ],
    "coverageTargets": "<Project>\n  <ItemGroup Condition=\"'$(MSBuildProjectName)' == 'CodoMetis.TypeKit.Samples'\">\n    <PackageReference Include=\"Microsoft.Testing.Extensions.CodeCoverage\" VersionOverride=\"18.12.0\" />\n  </ItemGroup>\n</Project>\n",
    "toolchain": {
      "dotnet": [
        "dotnet",
        "--version"
      ]
    },
    "enumerate": {
      "script": "tools/probe/enumerate.cs",
      "inputs": [
        "Directory.Packages.props",
        "global.json"
      ]
    }
  }
}
```


## Oracles this repository promises

- **JSON parity (differential).** A shape with a value object and the same shape with the wrapped
  type, serialized under the same options, give the same bytes, and each reads the other's JSON.
  `test/CodoMetis.TypeKit.Generators.Tests/JsonParityTests.cs` shows the pattern.
- **Schema parity (differential).** The OpenAPI schema of a value-object property equals the one
  ASP.NET Core publishes for the wrapped type in the same position, in every document version.
- **Wire and document agree (differential).** The statuses and problem types a fault response sends
  are the ones the document lists for that operation, in both hosting models.
- **Column parity (differential).** The EF model gives a value-object property the wrapped type's
  column type, and a query over `.Value` translates to the same SQL as the query over the wrapped
  property.
- **One set of rules (differential).** `TryFrom`, `Parse`, `TryParse`, the JSON converter, the type
  converter and `FromKnownGood` accept exactly what `Create` accepts, and every refusal names the
  fault `Create` returned.
- **No refusal quotes the input (exception).** Put a sentinel such as `SECRET-7f3a` in refused input;
  it must not appear in any exception message, inner exception, `ToString()`, ProblemDetails body or
  log line. The one exception is a dictionary key in `JsonException.Path`.
- **Off the wire (exception).** `Option` and `Result` throw `NotSupportedException` from
  System.Text.Json in both directions, in every position, unless the application registered its
  own converter.
- **Equality (metamorphic).** Equal value objects have equal hash codes, `CompareTo` is zero exactly
  when `Equals` is true, and both agree with the wrapped type's, through every base in the
  inheritance chain.
- **Round trip (metamorphic).** `Parse(x.ToString())`, a JSON write then read, and a column write
  then read give back an equal value object.
- **Loud, not silent (exception).** A declaration that cannot be generated fails the build with a
  CMTK id; it never compiles into a type with nothing in it.

## Intended: a refusal or difference here is a pass

- A `DateTime` is written as UTC; number handling set by an attribute on the containing type reaches
  no converter (README, "What TypeKit guarantees").
- A struct value object refuses a JSON `null`.
- A rejected dictionary key appears in `JsonException.Path`, and so in MVC's 400.
- Under a source-generated context, a value object over an enum needs the enum listed in the context.
- A validated value object has no `From`; an abstract value object and a generic wrapped type fail
  with CMTK1005.
- A missing `Option` property, and an empty collection or dictionary of `Option` or `Result`, pass
  through System.Text.Json without an exception, since no converter runs (a wording question for the
  README, raised 2026-10-15, not a finding).
- The EF Core package and `StoredJsonConverterFactory` skip validation by design.
- A host whose MVC and minimal-API JSON options describe a type differently has its document refused
  (`DescribedOptionsAgreement`).
- System.Text.Json refusing a combination of its own options with an exception of its own (for
  example `IgnoreNullValues` together with a `DefaultIgnoreCondition`).
- Anything a record in `design/adr/` decides. Search the records before calling a refusal a finding.
- Findings already reported by `code-analyst` are in
  `.claude/agent-memory-local/code-analyst/review-2-1-0-findings.md`. Do not report them again.

## Budget

Twelve claims a round unless the caller says otherwise. One build of the Samples project serves them
all; group the round's probes there.
