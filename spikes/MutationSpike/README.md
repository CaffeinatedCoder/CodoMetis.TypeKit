# Spike: does Stryker.NET run against this repository's tests, and what does it find?

**Status: run 2026-10-16, answered.** The bounded first trial of WS4 (mutation as the measure of
detection gaps) in the agentic issue-finding programme, on one satellite: `CodoMetis.TypeKit.Serilog`
with `test/CodoMetis.TypeKit.Serilog.Tests`. It decides nothing yet; it tells WS4 whether planning on
Stryker is possible. The survivors and their reading are in
[design/mutation-2026-10-16.md](../../design/mutation-2026-10-16.md).

**Question.** Stryker.NET's own integration test for its Microsoft Testing Platform runner pins
xunit.v3 3.2.2 with Microsoft.NET.Test.Sdk. This repository pins xunit.v3 4.0.2, which is Testing
Platform v2 with no Test.Sdk, and treats warnings as errors. Does Stryker's current release run here
with its `mtp` runner, and if it does, what are the counts and what do the survivors say about the
tests?

**Answer: yes, it runs, and its score undercounts.** Stryker.NET 5.0.0 (`dotnet-stryker`, the
current release on nuget.org) with `"test-runner": "mtp"` built the test project, found its 10 tests,
captured per-test coverage and tested every placeable mutant against xunit.v3 4.0.2 on
Microsoft.Testing.Platform 2.5.1, with no change to `src/` or `test/`. Of 15 mutants: 3 killed,
3 survived, 0 timed out, 0 without coverage, 7 compile errors, 2 ignored; score 50.00 %. The run took
17.7 s cold and 8.4 s warm. Two limits matter more than the score:

1. **A declaration pattern costs its whole expression.** Stryker places every mutant in one
   compilation by wrapping the mutated expression in a ternary over the original (mutant schemata).
   Both arms then declare the same pattern variable (`is { } valueObject`), which is CS0128, and
   Stryker rolls back every mutant whose span holds the error. 4 of the 7 compile errors are mutants
   that cannot compile anyway (`is { } v || v.X` reads an unassigned `v`, CS0165). The other 3 are
   valid mutants Stryker lost: `==` to `!=` on each attribute's type check and the outer `||` to `&&`.
   Seeded by hand into `src/` and run through the ordinary MSBuild build, each was killed (4, 3 and 7
   of the 10 tests failed). They are the policy's core logic, and the score never saw them. Lines
   with a declaration pattern outside comments, a rough count: base package 48, Analyzers 128,
   AspNetCore 78, EntityFrameworkCore 22, Testing 1, Serilog 2. How many mutants each costs is
   measured only by a run.
2. **The `mtp` runner is preview.** Stryker says so on every run ("The Microsoft Test Platform
   testrunner is currently in preview. Results should be verified"). Here they were: each of the 3
   survivors, seeded by hand, also passes all 10 tests under the ordinary build, and each of the 3
   lost mutants fails it, so Stryker's verdicts agreed with MSBuild on all 6 checked.

Of the 3 survivors, 1 is a missing assertion (the null guard of `WithTypeKit`) and 2 are equivalent
mutants (`inherit: false` to `true` on attributes declared `Inherited = false`). None is a product
defect.

## Evidence

| What | Value |
|---|---|
| Stryker.NET | `dotnet-stryker` 5.0.0, local tool in [dotnet-tools.json](dotnet-tools.json) (`rollForward: false`) |
| Test framework | xunit.v3 4.0.2 (`xunit.v3.core.mtp-v2` 4.0.2), Microsoft.Testing.Platform 2.5.1, Shouldly 4.3.0, no Microsoft.NET.Test.Sdk |
| Library under test | Serilog 4.0.0, the satellite's floor |
| SDK | 10.0.402 (`global.json`), macOS 27.0.1 arm64, Apple M4 Max, 16 cores; Stryker's default concurrency 8 |
| Mutation level | `Complete`; coverage analysis `perTest` |
| Tests found | 10, all passing before the run (the verdict tool: `TESTS OK: 10 passed`) |
| Mutants | 15: 3 killed, 3 survived, 0 timeout, 0 no coverage, 7 compile error, 2 ignored ("block already covered") |
| Score | 50.00 % (killed / (killed + survived)) |
| Run time | 17.7 s first run, 8.4 s second, same counts both times |

Every mutant, its status and the reading of each is in the report under `design/`.

The compile errors, from the trace log (`-L`), with the code the German-locale message names:

| Attempt | Errors | Rolled back |
|---|---|---|
| 1 | CS0411 `MutantContext.TrackValue<T>` cannot infer `T`; CS0128 `valueObject` and `set` already defined (3 each); CS0165 unassigned `valueObject`, `set` | mutants 7, 8, 11, 12 |
| 2 | CS0128 `valueObject`, `set`; CS0165 `valueObject`, `set` | mutants 6, 10, 14 |
| 3 | none | |

The test project's `bin/Debug/.../CodoMetis.TypeKit.Serilog.dll` held no Stryker code after either
run (no `Stryker` string in it, the size of the ordinary build), and the next ordinary test run
passed, so a run leaves the build as it found it.

## Running it again

Stryker runs as a local tool from this folder; the manifest pins the version measured. Nothing runs
in CI ([decision 92](../../design/adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md): CI
needs no model, and WS4 is local and nightly).

```bash
cd spikes/MutationSpike
dotnet tool restore
dotnet tool run dotnet-stryker -- --skip-version-check
```

Add `-L` for the trace log, which names each compile error and each rollback. The report is written
to `spikes/MutationSpike/StrykerOutput/<yyyy-MM-dd.HH-mm-ss>/reports/mutation-report.html` and
`mutation-report.json` beside it, with the logs under `logs/`; the folder is git-ignored, and the
2026-10-16 runs were `2026-10-16.19-51-01` and `2026-10-16.19-53-13`.
[stryker-config.json](stryker-config.json) names the source project and the test project; another
satellite is a copy of it with its own `project` and `test-projects`.

## What it leaves for WS4

- Stryker is usable here as it ships; no Test.Sdk, no VSTest, no pinned-down xunit.
- Its score is not the measure on this code: a mutant lost to a compile error is invisible in the
  score, and this repository writes declaration patterns throughout. A WS4 run reports the
  compile-error count beside the score, per package, and reads the trace log for valid mutants that
  were rolled back.
- The base package, EntityFrameworkCore, AspNetCore, Testing and the Analyzers are the next
  candidates; the Generators package stays with decision 100's seeds, since Stryker compiles the
  source project with Roslyn and Metalama does not run in that compilation.
