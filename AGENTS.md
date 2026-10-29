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
dotnet tools/ConsumerSmokeTest/bin/Release/net10.0/consumer-smoke-test.dll   # the packages, installed into throwaway consumers, one published with Native AOT (needs the platform linker); build it with -c Release first
dotnet tools/BuildVerdict/bin/Release/net10.0/build-verdict.dll test [--project P] [--filter-class C] [--filter-method M] [-- flags]   # an agent's build and test: a verdict, not the transcript
dotnet tools/PrHygiene/bin/Release/net10.0/pr-hygiene.dll event.json   # a PR's labels, milestone, branch and issue link, as CI's PR hygiene check reads them from the payload
dotnet tools/CiLanes/bin/Release/net10.0/ci-lanes.dll classify <path>...   # the CI lanes a changed path selects (Linux, Windows, the pack; macOS only on the release's runs), as CI's changes job decides them
```

The oracle kit's local tool, oracles (`tools/Oracles`, started as its built dll like docs-query), which needs no
model and never runs in CI: `test/CodoMetis.TypeKit.Oracles.Tests` holds the catalogue there without building
anything else ([decision 112](design/adr/0112-the-prober-gives-way-to-valueranges-oracle-kit.md),
[test/oracles/README.md](test/oracles/README.md)):

```bash
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll status          # the worklist: unheld claims, unread waivers, unseeded oracles, candidates
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds [filter]  # each seed: a copy of the tree, the patch, one project built, one class run
```

An agent runs builds and tests through `tools/BuildVerdict`, run as its built dll (`build
[<project|slnx>]` or `test`). It prints the exit code named, each diagnostic code once with its remedy
from `tools/BuildVerdict/build-verdict-remedies.tsv`, and the failing tests with their assertion; the
full log is `$GIT_DIR/build-verdict-last.log`. A code it prints as UNMAPPED gets a row once its fix is
known. In an agent's Bash tool, `tools/BashGuard` (registered in `.claude/settings.json`) refuses
`dotnet build` of anything but a tool, `dotnet test` and `dotnet msbuild` (a `-getProperty`/`-getItem` query
without a target excepted), and the verdict piped into
`head` or `tail`, and prints the verdict's form of the command. The raw commands above are for a
terminal.

Both are tool projects under `tools/` (design/adr/0092): BCL only, in the solution, each with its
tests (`tools/BuildVerdict.Tests`, `tools/BashGuard.Tests`), and run from `bin/Release`, which
`.claude/settings.json` builds at session start: the guard in the main checkout, every other tool
(`tools/AgentTools.slnf`) in the session's own, a worktree included, with the SDK installer's `dotnet`
when the one on PATH lacks the pinned SDK; a session start that builds nothing says so in your context
(design/adr/0105). Each records the sources it was built from and refuses to run from others, naming
the command that rebuilds it: `dotnet build tools/<Name> -c Release`, which the guard lets through.

The SDK is pinned in `global.json` (10.0.4xx band, `latestPatch`). Tests run on Microsoft Testing
Platform (xunit.v3 4.x), so `dotnet test` takes `--solution`/`--project`, and TRX output is
`--report-xunit-trx`. The build treats warnings as errors, including CS1591 on shipping projects, and
runs the CMTK analyzers on the shipping projects' own code (`src/Directory.Build.targets`), so a library
generic that breaks its own constraint attributes fails here, not in a consumer.

## Finding code

The codebase-memory MCP server keeps a code graph of this repository, the project
`CodoMetis.TypeKit`. It is the fastest way into the code, and it misses whole kinds of reference
here, so it leads and the source decides.

- **The graph first** to locate a symbol, list a type's implementers, trace callers and callees, and
  size a change's impact (`search_graph`, `trace_path`, `query_graph`, `get_code_snippet`).
- **Then the source and its tests**, read before concluding or editing. An edge is a lead.
- **Text search** (`git grep`, `search_code`) for literals, messages, diagnostic ids,
  configuration, documentation, and what the generators emit.
- **A framework question goes to the verified facts first**: `docs-query entry KB-NNN` or `docs-query find`
  before researching what ASP.NET, System.Text.Json, EF Core, the runtime or the platform do, and a new
  `design/knowledge/` entry when research settles one that something here turned on
  ([decision 93](design/adr/0093-verified-facts-are-pinned-dated-and-executable-where-they-can-be.md)).
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

## Issues
Work is tracked as GitHub issues, not as lists in docs or memory
([decision 109](design/adr/0109-work-is-tracked-as-labelled-issues-and-ci-checks-each-pull-request.md)).
Open the issue before starting the work: a bug, an enhancement, a follow-up, a finding from a review or a
probe.
- **Labels, on issues and PRs alike** (ValueRanges' scheme; the legend is CONTRIBUTING's "Issues and
  labels", and `gh label list` the source): exactly one type (`bug`, `enhancement`, `documentation`,
  `tests`, `chore`), exactly one `severity/*` on a bug, the `package/*` and `area/*` it touches, and
  `silent` (a bug that does less than it appears to), `breaking`, `found-by/prober` or `stacked` where they
  apply. Never invent a label; if none fits, say so.
- **Epics:** a release is an `epic` issue with a milestone of its version (`2.3.0`), its work attached as
  native sub-issues with the same milestone (`gh issue create --parent N --milestone M`). A theme that spans
  releases is an `epic` without a milestone; a finding belongs to the release it ships in. The plan stays in
  `design/roadmap.md`; there is no roadmap issue.
- **PRs** carry their issue's labels and milestone, open with `Closes #n`, and follow
  `.github/pull_request_template.md` (Problem, Change, Tests). `pr-hygiene.yml` runs `tools/PrHygiene` on
  every PR into `main` or a release branch and fails a missing type, severity or closing reference, a branch
  whose `<type>/` disagrees with its type label, a PR into `release/<version>` without that milestone, and
  `breaking` into anything but a major. A topic merges into its release branch, where `Closes` closes
  nothing: close its issue by hand on merge, with a comment linking the PR.
- When the work changes what an issue says (its scope, its state, a measured fact), edit the issue.

## Workflow
1. Read `design/plan.md`, the roadmap, the decision records and the design note for the area you
   touch, and the relevant spike README, before starting. A decision is changed by a new record.
2. Run `dotnet test` after each change.
3. **Prove every fix by reverting it.** See `.claude/skills/verify-the-guard`. The same applies to a
   new analyzer test or a convention test: seed the defect it claims to catch.
4. **Test every fix across its input class.** Before a fix is done, name the input class its defect
   lives in and run the fix's test over that class's variant catalogue: `TypeArgumentSites` and
   `TypeArgumentKinds` (`test/CodoMetis.TypeKit.Analyzers.Tests/Variants/`) for a type argument, where
   it is written and what it is, `KeyShapes` and `QueryShapes`
   (`test/CodoMetis.TypeKit.EntityFrameworkCore.Tests/Variants/`) for an EF key or query, the `shape.*`
   axes for a generated type, the settings axes for a framework option. Fix the siblings it finds, add a missing
   shape to the catalogue, and say so in the commit body; a sibling found this way is `Found-By: VA`
   ([decision 95](design/adr/0095-a-fix-is-tested-across-its-input-class.md), the skill's "The variants").
5. A change to an aspect, the fabric, the analyzer or a satellite gets a generation review
   (`.claude/skills/weaving-review`).
6. Commit with conventional format: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `build:`,
   `spike:`. A `fix:` ends with the trailers `Defect-Class:` and `Found-By:`, one of each, from the
   tables of [decision 94](design/adr/0094-topic-branches-into-a-release-branch-and-fix-trailers.md)
   (`CommitTrailerTests`). Work happens on a `<type>/<slug>` branch from `release/<version>`, reaches it
   by a PR, and every merge is a merge commit: never squash or rebase a pushed branch.
7. **Hold what the documents promise.** `test/oracles/` is the catalogue
   ([decision 112](design/adr/0112-the-prober-gives-way-to-valueranges-oracle-kit.md)): each sentence a consumer
   document promises is a claim in `claims.json`, quoted verbatim and naming the test classes that hold it, and
   every public member of the run-time libraries is claimed or waived in `surface-waivers.json`.
   - A new public member, or a new promise in `docs/` or a README, gets its claim, with the test that holds it,
     or a waiver that says why it needs none. `OracleSurfaceCoverageTests` fails on a member neither names, and
     `OracleCatalogueTests` on a quote its document no longer holds: reword the claim with the document.
   - An oracle is an ordinary test class that checks the library against an independent answer over many cases:
     a differential, a sweep over a variant catalogue, a completeness test by reflection. Register it in
     `oracles.json` with a seed, a defect in `src/` kept as `seeds/<defect>.patch` (`git diff` of the edit, then
     undo it by the inverse edit), and show it caught with `oracles seeds <defect>`.
   - A disagreement an oracle finds and nobody fixes yet goes to `candidates.json` with its issue, never into an
     assertion that hides it.
   - `axes.md` lists what a consumer varies and the pairs no test varies yet; a combination a finding names
     joins it.

## Releasing

Before `git tag`, show every seed caught, locally, a build of one project per seed:

```bash
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds
```

The release workflow runs the suite, which holds the catalogue, before it packs; there is no gate beside it
(decision 112 withdrew decision 97's). A seed that is not caught is answered by a stronger oracle or a better
seed, never by deleting the seed.
