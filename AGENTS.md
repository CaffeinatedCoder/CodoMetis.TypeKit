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

The oracle kit's local tool, which needs no model and never runs in CI
([test/oracles/README.md](test/oracles/README.md)):

```bash
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll status          # the worklist: unheld claims, unread waivers, unseeded oracles, candidates
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds [filter]  # each seed: a copy of the tree, the patch, one project built, one class run
```

An agent builds and tests through `tools/BuildVerdict` (`build [<project|slnx>]` or `test`). It prints a
verdict: the exit code named, each diagnostic once with its remedy from
`tools/BuildVerdict/build-verdict-remedies.tsv` (give an UNMAPPED code a row once its fix is known), and the
failing tests; the full log is `$GIT_DIR/build-verdict-last.log`. In an agent's Bash tool, `tools/BashGuard`
refuses the raw `dotnet build`, `dotnet test` and `dotnet msbuild` and prints the verdict's form of the command,
and refuses `wt merge` and `wt step squash|rebase|push`; the raw commands above are for a terminal. The tools run from `bin/Release`, built at session start in the
session's own checkout ([decision 105](design/adr/0105-every-tool-is-built-in-the-session-s-checkout.md)).
A tool built from other sources refuses to run and names the command that rebuilds it,
`dotnet build tools/<Name> -c Release`.

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
- **An empty result proves nothing.** The graph has no node for a member of a C# 14 `extension(...)`
  block (most of the `Option` and `Result` extensions), nothing Metalama generates (no `.From(` or
  `TryFrom` call has an edge), and no reference that is not a call; it resolves calls on a generic
  instance poorly and guesses wrong callers ([design/code-graph.md](design/code-graph.md) has the
  measurements). Fall back to text search, and never conclude "unused", "no other caller" or "safe to
  change" from it alone.
- **Refresh it**, since it does not follow the working tree: `index_repository` with the repository root
  as `repo_path` and `name: "CodoMetis.TypeKit"`, at the start of a session and after a branch switch, a
  rebase, a merge or a pull. A worktree under `.claude/worktrees/` or `../CodoMetis.TypeKit-worktrees/` isn't
  indexed: index it under a name of its own and delete that project when done, or search the source.

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
  introduce is CMTK1011. A new named generated member is introduced through its `GeneratedMembers` entry, which
  that check reads; one introduced past it carries no `[GeneratedMember]`, which `GeneratedMemberAttributeTests` refuses.
- **Diagnostic ids (`CMTK`) are public contract.** Never renumber a shipped rule, and never ship a
  new rule at Error severity in a minor version.
- **A package contains this repository's code and nothing else.** Third-party code copied into `src/` or
  bundled into a package brings a `THIRD-PARTY-NOTICES.txt` into that package, with the license texts; a
  package reference must be under an allowed license ([decision 125](design/adr/0125-a-package-documents-the-third-party-code-it-contains.md)).
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
([decision 109](design/adr/0109-work-is-tracked-as-labelled-issues-and-ci-checks-each-pull-request.md)):
open the issue before starting the work. The labels, the epics and what a PR carries are CONTRIBUTING's
"Issues and labels" and "Pull requests"; `gh label list` is the source, and a label is never invented.
What trips agents here:
- A release's work is a native sub-issue of its `epic`, with the version's milestone:
  `gh issue create --parent N --milestone M`. A finding belongs to the release it ships in.
- Name the branch from the type label before the first push (`feat/`, `fix/`, `docs/`, `test/`, `build/`,
  never `chore/`): PR hygiene fails a `<type>/` that disagrees, and renaming a pushed head closes its PR.
- A topic merges into its release branch, where `Closes` closes nothing: close its issue by hand on merge,
  with a comment linking the PR.
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
   by a PR, and every merge is a merge commit: never squash or rebase a pushed branch. Parallel work gets
   a worktree each, made by `wt switch -c <type>/<slug> --no-cd` beside the repository, with the session
   started in it ([design/worktrees.md](design/worktrees.md), [decision 126](design/adr/0126-parallel-work-runs-in-worktrunk-worktrees-beside-the-repository.md)).
7. **Hold what the documents promise** ([decision 112](design/adr/0112-the-prober-gives-way-to-valueranges-oracle-kit.md),
   [test/oracles/README.md](test/oracles/README.md) for the files and how to add an oracle). A new public member,
   or a new promise in `docs/` or a README, gets a claim in `test/oracles/claims.json`, quoted verbatim and naming
   the test that holds it, or a waiver in `surface-waivers.json` that says why it needs none; a claim is reworded
   with its document. A new oracle is registered in `oracles.json` with a seed, shown caught by
   `oracles seeds <defect>`. A disagreement nobody fixes yet goes to `candidates.json` with its issue, never into
   an assertion that hides it, and a combination a finding names joins `axes.md`.

## Releasing

Follow CONTRIBUTING's "Releasing". Its step 1 runs locally, before `git tag`, and is the one easy to skip:
`oracles seeds` shows every seed caught, and `docs-query due --nuget` lists the competitor facts to re-read. On an
arm64 Mac set `TYPEKIT_MSSQL_IMAGE=mcr.microsoft.com/azure-sql-edge:latest`, or the SQL Server oracles are `ERROR`
([decision 117](design/adr/0117-a-seed-is-caught-against-a-passing-baseline.md)). A seed that is not caught is
answered by a stronger oracle or a better seed, never by deleting it, and an `ERROR` by fixing the environment or
the baseline, never by reading it as caught.
