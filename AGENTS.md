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

**Read [design/plan.md](design/plan.md) before changing anything.** It is the map of `design/`: the
[roadmap](design/roadmap.md), the [decision records](design/adr/README.md), one dated file per
decision with its evidence, and a design note per area.

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
| `CodoMetis.TypeKit.AspNetCore` | OpenAPI transformers; a `Result` as an HTTP response |
| `CodoMetis.TypeKit.Serilog` | A destructuring policy: value objects logged as what they print |
| `CodoMetis.TypeKit.Testing` | Test assertions for `Option` and `Result` |

## Structure
- `src/` holds the shipping projects, one per package, and `test/` the test projects.
- `test/CodoMetis.TypeKit.Conventions.Tests` holds repository rules about packaging and
  boundaries, read from restore output (e.g. only `.Generators` may resolve Metalama). Projects
  are discovered by globbing `src/`, so a new package needs no edit there.
- Central package management (`Directory.Packages.props`). The versions of shipping references
  are floors for consumers, so raising one is a release decision. `spikes/` opts out of it and
  pins what each spike measured.
- `test/CodoMetis.TypeKit.Samples` holds every C# block of the READMEs and of the `docs/` pages as
  compiled code, between `// sample: {page}/{name}` and `// end sample`, beside tests that check the
  outputs the blocks state in comments. `{page}` is `README`, a package id, or a docs page's file name
  without `.md`. `ReadmeSampleTests` (conventions) holds each block to exactly one region.
- `spikes/` holds experiments that decided something. Each one has a README with the question,
  the answer and the evidence. Spikes are not in the main solution and are never packed.
- `docs/` holds the topic pages a consumer reads, one per topic, each linked from the root README's
  "Documentation" table; `design/` holds the roadmap, the decision records and the design notes,
  which no test compiles but whose links the conventions check.

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

## Finding code

The codebase-memory MCP server keeps a code graph of this repository, the project
`CodoMetis.TypeKit`. It is the fastest way into the code, and it misses whole kinds of reference
here, so it leads and the source decides.

- **The graph first** to locate a symbol, list a type's implementers, trace callers and callees, and
  size a change's impact (`search_graph`, `trace_path`, `query_graph`, `get_code_snippet`).
- **Then the source and its tests**, read before concluding or editing. An edge is a lead.
- **Text search** (`git grep`, `search_code`) for literals, messages, diagnostic ids,
  configuration, documentation, and what the generators emit.
- **An empty result proves nothing.** Fall back to text search when the graph is unavailable, stale
  or silent, and never conclude "unused", "no other caller" or "safe to change" from it alone.
  Measured on 2026-10-13, the graph:
  - finds types, interfaces, enums and ordinary methods, a type's implementers through generic
    interfaces (84 of the Samples' 88 value objects), and the callers of an ordinary method across
    files;
  - has no node for a member of a C# 14 `extension(...)` block, which holds most of the `Option` and
    `Result` extensions (`Choose`, `FirstOrNone`, `Transpose`, `Sequence`, the continuations of a
    `Task<Result<...>>`) and `AsText`, and puts the calls inside one on the file;
  - has nothing Metalama generates: none of the tests' 222 `.From(` calls has an edge, nor has a call
    to `TryFrom`, `FromKnownGood`, `Parse` or a converter, and a template an aspect introduces by
    `nameof` looks unreferenced. Read woven code as the Generators README's "Reading the generated
    code" says;
  - misses a reference that is not a call: a DI registration, a type argument, `typeof`, an attribute;
  - resolves a call on a generic instance poorly (`Option<T>.Match` has one resolved caller, where
    the repository writes `.Match(` about 200 times across its types) and guesses wrong callers: a low-confidence `suffix_match` edge, or a same-named method of another API
    (Metalama's `Diagnostics.Report` listed as calling `GeneratedCode.Report`). Overloads share one
    node.
- **Refresh it**, since it does not follow the working tree: `index_repository` with the repository
  root as `repo_path` and `name: "CodoMetis.TypeKit"`, at the start of a session and after a branch
  switch, a rebase, a merge or a pull (about 6 s). Without `name` it starts a second project under a
  derived name. `index_status` reports the checkout's HEAD, not the commit the graph was built from,
  so refresh rather than check.
- It indexes `spikes/` and leaves out `.claude/`, `bin` and `obj`, so an agent in a worktree under
  `.claude/worktrees/` reads the main checkout's graph, not its own: index the worktree under a name
  of its own and delete that project when done, or search the source. `.codebase-memory/`, the
  tool's local artifact, is ignored by git.

The way into the docs is docs-query (`dotnet tools/DocsQuery/bin/Release/net10.0/docs-query.dll`,
[its README](tools/DocsQuery/README.md)): `entry`, `cites`, `cited-by`, `list`, `claims` and `check`
answer exactly, and `find` ranks. It answers with file, heading and line, so read that section, not
the whole page. A ranked miss proves nothing.

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
  the measurement ([design/native-aot.md](design/native-aot.md)).
- **Aspects stay internal.** Their public surface is what they generate, and nothing else.
- **A value object's JSON is its wrapped type's, byte for byte**, under whatever options the
  application uses, values and dictionary keys alike (`JsonParityTests`). Never write or read the
  wrapped value by a rule of your own; go through `GeneratedJsonPlan<T>`.
- **No refusal quotes the input.** A generated `Parse`, type converter or JSON read replaces the
  wrapped type's exception, whose message quotes the text, with one naming the value object and the
  wrapped type, and no inner exception (`UnreadableInputTests`).
- **Seams are declared, everything else refused.** `TryFrom`, `FromKnownGood`, `Revalidate`,
  `CompareTo(TSelf)` and `ToString()` written by hand are kept; any other member the generators
  introduce is CMTK1011. A new generated member joins that check, or a hand-written one is kept silently.
- **Diagnostic ids (`CMTK`) are public contract.** Never renumber a shipped rule, and never ship a
  new rule at Error severity in a minor version.
- **A README's or a docs page's C# block is compiled code.** Change it and its region in
  `test/CodoMetis.TypeKit.Samples` together, or `ReadmeSampleTests` fails; the region's scaffolding
  (the types and variables it assumes) stays outside the markers. An output a block states in a comment
  (SQL, JSON, a string) is read from the README by the test beside the region, so correct the comment,
  never the test. A block that cannot compile on purpose says `<!-- not compiled: {reason} -->` above
  its fence; an analyzer diagnostic in a comment is no reason, the sample suppresses it around the region.
  A page in `docs/` is compiled by being there and must be linked from the root README's
  "Documentation" table; a package README is the reference for its package, a docs page the guide for
  a topic, and the same code block is never in both.
- **Consumer docs are written in a plain, friendly voice.** The root README, the package READMEs and
  `docs/` follow `.claude/skills/plain-docs`: written to "you", example first, plain words, edge
  cases last. Design records and this file keep their own register.
- **Silent is worse than broken.** A value object that compiles without being generated, an
  analyzer that stops firing, or a converter that materialises `default` are this repo's
  characteristic failures. Prefer a diagnostic or an exception to a plausible result.

## Workflow
1. Read `design/plan.md`, the roadmap, the decision records and the design note for the area you
   touch, and the relevant spike README, before starting. A decision is changed by a new record.
2. Run `dotnet test` after each change.
3. **Prove every fix by reverting it.** See `.claude/skills/verify-the-guard`. The same applies to a
   new analyzer test or a convention test: seed the defect it claims to catch.
4. A change to an aspect, the fabric, the analyzer or a satellite gets a generation review
   (`.claude/skills/weaving-review`).
5. Commit with conventional format: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `build:`,
   `spike:`.
