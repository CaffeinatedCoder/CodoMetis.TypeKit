# 0127. A change to tools or tests runs the test projects it reaches, and the ignore list has a class

Date: 2026-10-31
Status: accepted

Context: 2026-10-31, issue #268, amending the selection of [decision 113](0113-ci-runs-the-lanes-a-change-can-affect.md).
Under 113 a lane builds and tests the whole solution unless every changed path is prose. #266 changed one tool,
`tools/BashGuard`, its tests, docs and one line of `.gitignore`. Its first run, 41215127177, took 12m55s on Linux and
13m32s on Windows, and packed and smoke-tested too, because `.gitignore` was unlisted. The tests that could notice
the change were `CodoMetis.TypeKit.BashGuard.Tests` (112 tests, 5 s locally) and the conventions. A docs-only PR
(#267) runs `test/Prose.slnf` alone, in 1m49s.

## Decision

**A third suite, `reach`.** When every changed path is prose, the ignore list, or a path inside a project of the
solution under `tools/` or `test/`, and at least one is not prose, the legs build and test the projects the change
reaches, not the solution. Any other path runs the whole solution as before: `src/**`, `tools/Shared/**`,
`tools/Directory.Build.*`, `tools/CiLanes/**` and `tools/ConsumerSmokeTest/**` (the smoke test class, which keeps
the pack), `spikes/**`, `test/Directory.Build.props`, `test/oracles/**`, a solution filter, a project the solution
does not list, the build files and every unlisted path. The lanes are 113's: a reached suite runs where the paths'
classes select, so a tool or test change still runs on Windows, with the conventions and the changed tool's tests.

**What a change reaches.** A changed path reaches a solution project under `tools/` or `test/` when the project reads
it. A project reads its own directory; every path an item or an import of its project file names outside that
directory by a relative path (a linked source such as `../CodoMetis.TypeKit.BuildVerdict.Tests/ToolSandbox.cs`, a
`ProjectReference`); and, through each `ProjectReference`, whatever the referenced project reads, so a change to
`test/CodoMetis.TypeKit.Generators.Probes` reaches the four test projects that reference it. A prose path adds the
projects of `test/Prose.slnf`. The suite is every project reached, test projects and changed tools alike, so the
changed tool is built even when no test references it (`tools/Oracles`).

**A read by path is declared.** A test that reads another project at run time, by path rather than by reference,
declares it in its project file with an item ci-lanes reads like any other: `<ReadsByPath Include="../" />`. Two
projects read the repository that way. The conventions read every file (its index, every project's sources and
evaluation) and declare `../../`, the root, so every reached suite runs them. The oracle tests find the test that
holds each claim among every test project's sources and declare `../`, `test/`. No other test project reads another
by path: the tools' tests read their own tool, which they reference, and `tools/Shared`, which runs the whole
solution.

**`TestReachTests` holds the declarations** (conventions). It evaluates every project under `tools/` and `test/` with
MSBuild (items and imports, as `CiLaneTests` does for the packages) and fails a file a test project builds from,
outside its own directory, whose change alone would not run it: the evaluation, not ci-lanes' reading of the project
file, so an include ci-lanes cannot read (one through a property) is found. It reads every test project's string
literals and fails a project named by path (`"tools/PrHygiene/Program.cs"`, or `"PrHygiene"` beside `"tools"`) or a
whole directory scanned (`Path.Combine(root, "test")` in a file that enumerates) that the reader does not reach,
unless the file is waived with the reason it reads nothing. It fails a narrowed suite without the conventions.

**The ignore list is its own class**, `ignore list`, Linux alone, which narrows to the projects that declare they
read the root, the conventions. Measured: no test reads `.gitignore`. The conventions list the index
(`git ls-files`), which the ignore list does not change, and a fresh checkout has nothing to ignore; `dotnet pack`
does not read it. `.gitattributes` stays unlisted: it sets the line endings of every file a Windows checkout holds
and a package packs.

**The selection answers `projects`.** `ci-lanes select` writes `suite=prose|reach|full` and `projects`, a JSON array
of project files, empty unless the suite is `reach`. The test legs write it as `test/Reach.slnf`, a filter of the
solution beside `test/Prose.slnf` (ignored by git), and restore, build and test that. `ci-lanes suite <path>...`
answers the suite each path alone runs, for a contributor and for `TestReachTests`. The gate passes a `reach` only
beside no pack and no macOS leg, with one or more distinct project files under `tools/` or `test/`, since a filter
of nothing builds and tests nothing and passes; it fails `projects` beside the other suites, and a missing or
malformed `projects`. A cancelled or skipped leg of a reached suite fails it as any selected job does.

**Anything ci-lanes cannot read runs the whole solution:** the solution file, a project file, a referenced project
that does not exist, `test/Prose.slnf`, or a path no project reaches.

## Consequences

- A change shaped like #266 runs the prose suite, `tools/BashGuard` and its tests on Linux and Windows, with no pack.
- A change to a test project runs it, the conventions and the oracle tests; a change to a tool, the tool, its tests,
  what references them, and the conventions.
- A reach the declarations miss is a test that stops running for the change that breaks it, the failure this
  repository calls silent. `TestReachTests` sees the evaluation and the literals; a read built from neither (a path
  read from a file, or computed) is found only by review, and the whole solution still runs on the release's pull
  request into `main`, the nightly and the release.
- Windows runs a narrowed suite as Linux does: a Windows-only defect in a test project the change does not reach
  surfaces on those same runs.
- A path class is changed here, by a new record, and in `tools/CiLanes` together, as under 113.

## Evidence

Measured locally on 2026-10-31 against the checkout: `ci-lanes suite` over #266's eleven paths answers the five
projects of `test/Prose.slnf`, `tools/BashGuard` and `CodoMetis.TypeKit.BashGuard.Tests`, and `test/Reach.slnf`
written from them builds (17 projects with the shipping projects they reference). Each new `SelectionTests` and
`GateTests` row failed against 113's selection first; `TestReachTests` failed with the oracle tests' declaration
removed, the conventions' removed, a read of `tools/PrHygiene` seeded into the BashGuard tests, and ci-lanes made to
skip linked sources and references. The CI timings before and after are recorded on #268.
