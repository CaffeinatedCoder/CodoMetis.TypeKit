# 0101. The build workflow replays the probes in legs by family, in parallel and under a time limit: every probe on a push, the stale ones on a pull request against the base branch's saved state

Date: 2026-10-16
Status: accepted

Context: 2026-10-16, the probe gate round for 2.2.0 (pull request #20) took the corpus from 11 registered
probes to 43, 68 processes with their variants. [Decision 96](0096-ci-replays-every-probe-and-the-release-runs-the-gate.md)
had chosen `replay --all` from an empty state on every push and pull request, one probe after another, each
with the coverage extension on and a report written, measured at 287 s for 7 probes, and said to "revisit
this when `replay --all` on a runner takes longer than the test job's path to green". On pull request #20
the probe replay took 26 min 50 s on `ubuntu-latest`, against 3.5 to 7.7 min for the test legs and 13 min for
the rest of the run; locally 1,039 s. No `timeout-minutes` was set, so a hung replay would have run to the
six-hour default. The concept of the same day on agentic issue finding (H14) named the four costs: serial,
coverage-instrumented, stateless, unbounded. The coverage is needed only for the footprint, which a run from
an empty state records and nothing reads.

## Decision

**The replay runs its probes in parallel.** `prober replay` starts every variant of every selected probe at
once and runs them `--parallel N` at a time, one per core by default, and records each probe in registry
order once its variants have ended, so the transcript and the state files read as before. The verdict tool's
test mode takes `--no-build` and the replay's test template uses it: the replay builds once, and two builds of
one project at once would race in its `obj` directory.

**The build workflow splits the corpus into legs by family.** The replay configuration of
`.claude/probe-axes.md` gains `families`, a name to the surfaces it holds (`http`, `ef`, `json`, `text` with
`logging`, `build` with `generation`, `analyzers` and `option-result`). A probe's family is its claim's first
surface's. `replay --family F` runs one family and refuses while any probe of the registry has a surface in
no family, so a matrix cannot leave a probe out quietly. The `probes` job of `dotnet.yml` is a matrix over
the families, `fail-fast: false`, and `ReleaseWiringTests` holds the matrix to exactly the families the axes
file names. A `probe-status` job follows the legs always, joins their states with `prober merge --from`,
which copies each leg's evidence, adds its run, footprint and finding lines where the state lacks them (a
line a leg carried over from a restored state is added once), refuses while a registered probe was neither
replayed nor skipped by any leg, and fails when a leg failed; then it reports `prober status` in the job
summary. Both jobs carry `timeout-minutes` (60 a leg, 15 the merge).

**A push replays every probe and saves the state.** On a push to `main` or a `release/**` branch each leg runs
`replay --all` of its family from an empty state, with coverage, so every footprint is fresh; the merged state
goes into the Actions cache under `probe-state-<branch>-<sha>`. A red push saves nothing: the save step runs
only after a merge that passed.

**A pull request replays what it stales, against the base branch's saved state, without a footprint.** Each
leg restores the newest `probe-state-<base branch>-` entry, takes the base branch as its ledger branch
(`PROBE_BRANCH`), and runs `replay` of its family without `--all`: the probes whose identity (the corpus
sources, the rows, the axes file, the build environment, the tool) or product footprint the branch changed,
and `--no-footprint`, which leaves the coverage extension and the report out and records no footprint, since
nothing of that state is kept. With no saved state (the first run, an evicted entry, a base that has not been
green since the corpus changed) every probe runs, as from an empty state. The legs' states are uploaded and
merged as on a push; nothing is saved.

**The release is unchanged.** `release.yml` keeps `replay --all` from an empty state and the gate
([decisions 96](0096-ci-replays-every-probe-and-the-release-runs-the-gate.md) and
[97](0097-2-2-0-is-a-consolidation-release-and-the-gate-binds.md)); the replay now runs its probes in parallel
there too.

**Why a carried state is acceptable now.** Decision 96 refused it because the footprint sees only what ran
under coverage in `src/` and a fixed list of compile-time paths, so a dependency it does not see skips a probe,
and because "the check of record reads the tracked corpus and the commit alone". Both still hold for the
check of record: the push after a merge and the release replay everything from an empty state. The pull
request's replay is the fast check on what the branch touched; what it cannot see, the push catches on the
merge commit and the gate before a release. The roadmap's harness work adds a declared `watched` list per
probe, which widens what stales a probe.

**Held by tests.** `ReplayTests` in the prober tests run the replay against a fake adapter: a family replays
the probes whose claim's surface it holds; a probe whose surface is in no family refuses the replay; an
unknown family or a bad `--parallel` is a usage error; a replay without a footprint records evidence and no
footprint and leaves its probes stale; probes run as many at a time as asked and are recorded in registry
order; `merge` joins the legs, adds a carried line once, refuses a probe no leg saw and fails when a leg did.
`ReleaseWiringTests` holds the workflow: the matrix lists every family of the axes file, the legs and the
merge job have a time limit, the push replay is `--all` of the family leg, the pull request replay is the
family leg without `--all` and with `--no-footprint`, the base state is restored on pull requests only and by
the base branch's prefix, the replay and the merge end their steps with their own exit code, the merge job
follows the legs always, downloads before merging, reports after, and saves the state on a push only.

## Consequences

- A pull request's probe job takes minutes where it took the better part of an hour, and fails on what the
  branch touched. A pull request that does not touch a failing probe's footprint is green while the base
  branch is red; the push after the merge shows the red, as the base already does.
- Every probe's identity changed with this decision (the axes block holds the replay configuration), so the
  first push replays everything, as it would anyway.
- The verdict tool's log, one per git directory, is written by several processes at once during a replay and
  ends as the last writer's; the replay reads each process's verdict from its output, not from the log.
- A replay on a machine with many cores starts as many test hosts; see the measurement below for the limit.

## Evidence

Measured 2026-10-16 on macOS (arm64, 16 cores) at 4b7f3f9 (43 probes, 68 processes), in the warm replay
worktree, while other builds ran on the machine:

| Replay | Build | Probes | Result |
|---|---|---|---|
| serial, with coverage, at 00d3b8f (the gate round's measurement) | 35 s | 1,039 s | 40 passed, 3 failed: BaseProbe, C12, C14 |
| `--parallel 4 --no-footprint` | 3 s | 458 s | the same 40 and 3 |
| `--parallel 4`, with coverage | | PENDING | |
| `--parallel 16`, with coverage | 53 s | 122 s | 1 passed, 42 test hosts exited 7 having run zero tests |

On `ubuntu-latest` (4 cores): pull request #20's serial job, 26 min 50 s; this decision's own pull request,
PENDING.

The seven prober tests and the convention test were each shown to fail against a seeded defect: the family
filter ignored (3 tests fail), a probe in no family not refused, `--no-footprint` still asking for coverage,
`--parallel` ignored, the merge accepting a probe no leg saw, the merge passing with a failed leg, the merge
adding a carried line twice; and in the workflow, the time limit dropped from the legs or moved onto a step,
a family dropped from the matrix, the push replay without `--all`, the pull request replay with coverage, a
replay or a merge that cannot fail its job, the state saved from a pull request, the merge job skipped when a
leg fails, the base state restored on pushes, and the pull request not taking the base branch's state.
