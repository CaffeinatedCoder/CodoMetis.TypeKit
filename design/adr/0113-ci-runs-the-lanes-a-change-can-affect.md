# 0113. CI runs the lanes a change can affect, a gate job decides the merge, and the release runs every lane

Date: 2026-10-29
Status: accepted

Context: 2026-10-29, issue #118, with #70, #71, #73, #74 and #117 as its parts. Since the prober left CI
([decision 112](0112-the-prober-gives-way-to-valueranges-oracle-kit.md)), the build workflow had five jobs, and
every pull request into a release branch ran all of them whatever it changed. Its `changes` job skipped Pack and
Consumer when no listed path changed, but it failed open, missed a rename's source and missed two inputs (#70).
GitHub reads a skipped job as a passing required check, so nothing told a deliberate skip from an accidental one
(#71). Pack and Consumer waited for the whole test matrix (#117). The release ran neither the Windows and macOS
legs nor the smoke test's own-feed mode, and nothing ran CI unless something was pushed (#74). The maintainer's
view: the three-platform build, packing and the consumer smoke test are mostly release work, and a pull request
that changes only prose should not pay for them. Over the 150 runs before this decision, a Windows or macOS leg
failed with Linux passing three times. Two of those were prober tooling. One was a defect in `src/`: the analyzer's
code fix wrote CRLF beside LF in a Windows checkout (f9f64e5). macOS never failed alone.

## Decision

**Four lanes.** Every run has the Linux test leg, which runs the whole suite (a prose-only change, the prose suite
below): the conventions, the compiled docs samples, the oracle catalogue and the PostgreSQL round trips. The
`windows` lane is the Windows test leg. The `macos` lane is the macOS test leg, which only the runs that select
nothing get. The `pack` lane is the Pack job (pack, the smoke test against the packed feed, the SBOMs) and the
Consumer job (the smoke test packing its own feed). The Windows and macOS legs test the repository's own handling
of paths, line endings and child processes, not the .NET runtime, so Windows runs where code does that itself, and
macOS, which adds a case-insensitive file system and caught nothing alone, waits for the release's checks.

**A changed path selects lanes by its class.** The first class whose pattern matches decides. A path that no
class names selects every lane a selection can (all but macOS), so a new kind of file costs time rather than
coverage:

| Class | Paths | Lanes |
|---|---|---|
| changelog | `CHANGELOG.md` | Linux, pack: the packages' release notes are its section (#75), read by a property function that no evaluation lists |
| source text | `src/CodoMetis.TypeKit.Analyzers/**`, `src/CodoMetis.TypeKit.Generators/**` | Linux, Windows, pack: the packages that write or edit source text, whose line endings a Windows checkout changes |
| package | `src/**` | Linux, pack: the run-time packages, which use the runtime's own paths and text |
| smoke test | `tools/ConsumerSmokeTest/**`, `tools/Shared/**`, `tools/Directory.Build.*`, `tools/CiLanes/**` | Linux, Windows, pack: the smoke test's own sources and what it compiles in, and the selection itself |
| prose | `docs/**`, `design/**`, `.github/ISSUE_TEMPLATE/**`, `**/*.md` | Linux, with the prose suite when every path is prose |
| code no package reads | `test/**`, `spikes/**`, `tools/**` | Linux, Windows: they handle paths, line endings and child processes themselves |
| anything else | the build files (`Directory.*.props`/`targets`, `Directory.Packages.props`, `global.json`), `.config/`, the workflows, `benchmarks/`, `.claude/`, any path not listed | Linux, Windows, pack |

**Which runs select.** A pull request into `release/**` and a push to `release/**` are selected by their diff,
`git diff --name-only --no-renames -z`: a pull request against `origin/<base>...HEAD`, a push against the commit
it replaced (`github.event.before`). A push that creates the branch, or whose previous commit the clone does not
have, runs every lane, macOS included. So does every other run: a pull request into `main` (the release's), a push
to `main`, a dispatch, the nightly run and the call from `release.yml`. A diff that names no path also runs every
lane.

**A prose-only change runs the prose suite.** When every changed path is prose, the Linux leg restores, builds and
tests `test/Prose.slnf` instead of the solution: the conventions tests, the samples, the docs-query tests and the
oracle tests, the four projects that read prose, and the woven probes the conventions tests read. Measured on the
first docs-only run, the whole suite's test step took 3.6 min, set by projects that read no prose: EF Core 2m44,
conventions 1m50, analyzers 1m40, ASP.NET Core 1m32. `ProseReaderTests` holds the set: a test project outside it
whose sources name `docs`, `design`, `.github`, a path under one of them, or a Markdown file outside `src/` fails it,
unless the file is waived with the reason it reads nothing. A package's README is classed with its package, so the
analyzer, generator, core and ASP.NET Core tests that read theirs stay outside the set.

**The selection is `tools/CiLanes`**, a tool project under [decision
92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md). Its `select` command answers `legs` (the test
matrix, a JSON array of one, two or three runners), `pack` (`true` or `false`) and `suite` (`prose` or `full`),
and writes each path's class to the job summary. Its `classify` command answers the same table for paths given on
the command line. A conventions test runs `classify` over every file MSBuild evaluates for each shipping project
and the smoke test, items and imports both, and fails a file that does not select `pack`.

**A gate job, `CI`, decides the merge.** It needs every other job, runs under `always()`, and runs `ci-lanes gate`
on `toJSON(needs)`. It passes only when `changes` succeeded, its `legs` is exactly one of the three matrices it
writes (Linux; Linux and Windows; all three), its `pack` exactly `true` or `false`, its `suite` exactly `prose` or
`full`, and `prose` only beside the Linux leg alone and no pack, the test matrix succeeded, and Pack and Consumer
succeeded when selected and were skipped when not. A failed selection, a cancelled leg, a misspelled output and a
selected job that was skipped each fail it. A job the gate's table does not know also fails it, and a conventions
test holds `CI`'s `needs` to every other job of the workflow. Once `CI` reports on `release/2.3.0` and `main`, the
required checks on `main` become `CI` and `Labels and issue link`, and the release-branch ruleset adds `CI`. Every
selected leg blocks.

**Pack and Consumer need only `changes`.** They start beside the test legs and no longer wait for them; the gate
still requires both (#117).

**Before the Windows and macOS legs may skip, a Linux conventions test refuses non-portable file names** (#73). It
fails a character Windows refuses (`<>:"|?*\` and controls), a segment ending in a dot or a space, a reserved
device name, two paths equal under case folding and Unicode normalization, and a path longer than 160 characters,
so a prose-only change cannot break the Windows checkout unseen.

**The release runs every lane before it verifies.** `dotnet.yml` is also `on: workflow_call`, and `release.yml`'s
`ci` job calls it; `verify` needs `ci`. A called workflow takes its caller's `github.workflow`, and concurrency
groups ignore case (KB-043), so `dotnet.yml`'s group is `ci-${{ github.workflow }}-${{ github.ref }}`. Without the
prefix the call would share the group `release-<tag>` with its caller and wait on it.

**A nightly run.** `nightly.yml` runs on a schedule and on dispatch. It dispatches `dotnet.yml` on `main` and on
every `release/*` branch, each a full run. A schedule runs only from the default branch, so it starts once 2.3.0
merges into `main`. GitHub turns a public repository's schedules off after 60 days without activity, so nothing
depends on it: the release calls the workflow itself.

## Consequences

- A prose-only pull request runs Changes, the Linux leg and the gate. A change to a run-time package adds the pack,
  a test or tool change adds Windows, and a change to the analyzers, the generators, the smoke test, the build or
  the workflows runs Linux, Windows and the pack.
- No selected run has the macOS leg. A defect only macOS shows (a case collision the file-name test misses, a
  symlinked temporary directory) surfaces on the release's pull request into `main`, the nightly run or the
  release.
- A line-ending or path defect in a run-time package other than the analyzers and generators is tested on Windows
  only by those same runs. None has been seen.
- Merging a topic into its release branch selects again by the merge's own diff, so a docs merge is cheap on push
  as well.
- Prose read by a test on Windows or macOS (a README whose stated outputs a sample test reads) is tested there
  only by the release's pull request into `main`, the nightly run and the release. The repository has no
  `.gitattributes`, so Windows checks such files out with CRLF. A prose change that breaks a test only under CRLF
  surfaces on the release pull request, not on its own.
- Every run builds `tools/CiLanes` twice, in Changes and in the gate. Measured on the first runs, the Changes job
  took 0.3 min and the gate 0.2. That is the cost of selecting in C# under decision 92 rather than in a shell `case`.
- A prose-only change compiles none of the tests that read no prose, so a document that breaks one of them (by a
  read nobody declared, which `ProseReaderTests` looks for in the sources' literals) surfaces on the next run that is
  not prose-only.
- A path class is changed here, by a new record, and in `tools/CiLanes` together. The conventions test reads the
  build, not this table.

## Evidence

The 11 successful runs of `dotnet.yml` since the prober left (2026-10-28 21:08 to 2026-10-29 08:21 UTC), from
`gh run view`'s job timings:

| Job | Median | Max |
|---|---|---|
| Changes | 0.1 min | 0.1 |
| Test (ubuntu-latest) | 6.3 min | 7.0 |
| Test (windows-latest) | 8.5 min | 9.3 |
| Test (macos-latest) | 7.2 min | 15.2 |
| Pack | 6.2 min | 6.6 |
| Consumer smoke test | 5.7 min | 6.3 |

Wall clock was 15.0 min median (12.6 to 21.4) and 33 runner-minutes. Over the last 150 runs, those where a
Windows or macOS leg failed and the Linux leg passed were 38927646009 and 38929696748 (prober tooling, both
Windows) and 40827936927 (the analyzer's code fix, Windows, fixed by f9f64e5). The critical path was the slowest test leg
followed by Pack. Within a leg, the build took 1.2 to 2.1 min and the tests 3.1 to 5.5 min. Pack's smoke test took
3.0 to 3.7 min, and Consumer's 3.2 to 3.6. The repository is public, so the minutes cost nothing, and the target is
wall clock.

The before and after runs of a docs-only change and of a `src/` change are recorded on #118 and its pull request.
