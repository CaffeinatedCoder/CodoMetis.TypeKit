# CodoMetis.TypeKit

Strong types for .NET 10: `Option` and `Result` types that cannot be misused, and value objects
that are written as one line and generated at compile time by Metalama.

- **`Option<T>` / `Result<T, TError>`** have no public `.Value`. You reach the content through
  `Match`, `TryGetValue` and friends, and `default` does not compile.
- **Value objects.** `readonly partial record struct OrderId : IValue<Guid>;` gets its field,
  constructor, `Value`, `From`, JSON converter, parsing, formatting, comparison and type converter
  generated. An `IValidatedValue<TSelf, T, TFault>` also gets `TryFrom` (an `Option`) and
  `FromKnownGood`, both derived from the one hand-written `Create` (a `Result`).
- **Satellites** map value objects to EF Core columns, where `.Value` works in LINQ as it does in
  memory, and to OpenAPI schemas.

The packages are being built phase by phase: **read [docs/plan.md](docs/plan.md) before changing
anything.** It holds the package map, the phase order and the decisions.

This file is the canonical agent guide. There is no CLAUDE.md, on purpose.

## Stack
.NET 10 · C# 14 · Metalama 2026.1 (compile-time generation; no 2027.0-only API such as
`[Durable]`) · Roslyn analyzers (netstandard2.0) · EF Core 10 · Microsoft.AspNetCore.OpenApi 10

## Packages
| Package | Role |
|---|---|
| `CodoMetis.TypeKit` | `Option`, `Result`, value-object contracts. **Metalama-free** |
| `CodoMetis.TypeKit.Analyzers` | Analyzers and code fixes; reaches consumers through `CodoMetis.TypeKit` |
| `CodoMetis.TypeKit.Generators` | Metalama aspects and the transitive fabric |
| `CodoMetis.TypeKit.EntityFrameworkCore` | Converter, convention, `.Value` translation |
| `CodoMetis.TypeKit.AspNetCore` | OpenAPI transformers |

## Structure
- `src/` holds the five shipping projects, one per package, and `test/` the test projects.
- `test/CodoMetis.TypeKit.Conventions.Tests` holds repository rules about packaging and
  boundaries, read from restore output (e.g. only `.Generators` may resolve Metalama). Projects
  are discovered by globbing `src/`, so a new package needs no edit there.
- Central package management (`Directory.Packages.props`). The versions of shipping references
  are floors for consumers, so raising one is a release decision. `spikes/` opts out of it and
  pins what each spike measured.
- `spikes/` holds experiments that decided something. Each one has a README with the question,
  the answer and the evidence. Spikes are not in the main solution and are never packed.
- `docs/` holds the plan and the design notes.

Never hard-code a positional path (`../../`) to reach the repo root. Walk up to the
`CodoMetis.TypeKit.slnx` marker instead.

## Commands
```bash
dotnet build CodoMetis.TypeKit.slnx
dotnet test --solution CodoMetis.TypeKit.slnx
dotnet test --project test/CodoMetis.TypeKit.Conventions.Tests --filter-method "*Metalama*"
dotnet build spikes/FabricSpike/FabricSpike.slnx      # the fabric spike, standalone
./test/consumer-smoke-test.sh                          # the packages, installed into throwaway consumers, one published with Native AOT (needs clang)
```

The SDK is pinned in `global.json` (10.0.4xx band, `latestPatch`). Tests run on Microsoft Testing
Platform (xunit.v3 4.x), so `dotnet test` takes `--solution`/`--project`, and TRX output is
`--report-xunit-trx`. The build treats warnings as errors, including CS1591 on shipping projects.

## Rules that are easy to break

- **`CodoMetis.TypeKit` stays Metalama-free.** Nothing in the base package may reference
  Metalama, directly or transitively. That is why the aspects are applied by a
  `TransitiveProjectFabric` in `.Generators` and not by `[Inheritable]` on the interfaces.
- **Discovery is by interface, never by name.** No namespace strings, assembly-name prefixes or
  type-name lists. The analyzer resolves the interface symbols from the compilation. Run-time code
  that holds only a `Type` reads `GeneratedValueObjectAttribute<TValueObject, T>`, whose type arguments are
  constrained to the interfaces, and **never calls `GetInterfaces()`**: trimming removes an interface
  nothing uses, and under Native AOT every value object then looked like no value object at all. A
  name-based check passes in tests and breaks for the first consumer who renames something. The
  only names the analyzer knows are this package's own: the metadata names of its contracts and
  the exact identity of the `.Generators` assembly, each tied to the real assembly by a test.
- **No public `.Value` on `Option`/`Result`, and never positional records.** Positional
  parameters become public properties and reach `ToString`.
- **`Option`/`Result` refuse System.Text.Json.** Every exported struct of the base package carries
  `[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]`, which throws in both directions;
  without it a `Some` is written as `{}` and read back as `None`. The completeness test in
  `NotWireTypeTests` fails for a struct without it.
- **The materializer skips validation.** `IValueObjectMaterializer<,>.Materialize` exists for
  values the application wrote itself (a database column). Never expose it to input, and never
  call it outside the EF satellite (CMTK0004). `ValueObjectConverter<,>.Materialize` is public only
  because EF's compiled model calls it from generated code in the application's assembly.
- **Native AOT: rewrite, never suppress.** The run-time packages build with `IsAotCompatible` and
  warnings are errors. No `MakeGenericType`, no reflection over members the trimmer may remove, no
  `JsonSerializer` call that takes only options, in the packages or in woven code: a typed call
  (`GeneratedValueObjectAttribute.Accept`, `GeneratedJson.TypeInfo`) or an interface instead. An
  `[UnconditionalSuppressMessage]` needs a measured reason, and the smoke test's `aot` consumer is
  the measurement (docs/plan.md §11).
- **Aspects stay internal.** Their public surface is what they generate, and nothing else.
- **Diagnostic ids (`CMTK`) are public contract.** Never renumber a shipped rule, and never ship a
  new rule at Error severity in a minor version.
- **Silent is worse than broken.** A value object that compiles without being generated, an
  analyzer that stops firing, or a converter that materialises `default` are this repo's
  characteristic failures. Prefer a diagnostic or an exception to a plausible result.

## Workflow
1. Read `docs/plan.md` and the relevant spike README before starting.
2. Run `dotnet test` after each change.
3. **Prove every fix by reverting it.** See `.claude/skills/verify-the-guard`. The same applies to a
   new analyzer test or a convention test: seed the defect it claims to catch.
4. A change to an aspect, the fabric, the analyzer or a satellite gets a generation review
   (`.claude/skills/weaving-review`).
5. Commit with conventional format: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `build:`,
   `spike:`.
