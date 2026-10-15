# 0096. CI replays every probe on every push and pull request, and the release runs the gate, advisory

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, the bug-finding review of the 2.1.0 cycle. [The fix history](../fix-history.md) found
that 12 of 133 fixes repaired or finished an earlier fix ("Fixes that repair fixes"). Replaying the probe
corpus after each change catches part of that, but the replay ran only from a `post-commit` hook that is
opt-in, per clone and untracked. AGENTS.md said that `replay` and `gate` "run in CI as anywhere" and that
"CI runs it with `PROBE_STATE_DIR` in its own workspace", while neither `.github/workflows/dotnet.yml` nor
`release.yml` called the prober. Neither command starts a model, so [decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)
allows both in CI.

## Decision

**A `probes` job in `dotnet.yml`**, on `ubuntu-latest`, on every push to `main` or a `release/**` branch
and every pull request into one, beside the test matrix (it needs no other job). It builds `tools/Prober`
in Release, runs `prober replay --all`, then `prober universe` and `prober status`, writes the status to
the job summary, and uploads the state directory as the `probe-state` artifact for triage. Linux only: the
probes check documented claims, not the platform, and the test job's three legs already test the prober.

**`replay --all`, from an empty state directory, and no cache.** The footprint replay was the other
candidate. On an empty state it selects every probe anyway (`replay --dry-run` and `replay --all
--dry-run` both listed 7 of 7), so the real alternative was the footprint replay over a state carried from
run to run in the Actions cache. That was refused:

- A carried state makes CI's answer depend on what an earlier run left behind. A cache entry is chosen by
  key and branch scope, not by the commit the state describes, and a probe the footprint does not select
  is not run. The footprint is the coverage of `src/` plus a fixed list of compile-time paths, so a
  dependency it does not see skips a probe until something it does see changes. A regression that a fix
  brings in elsewhere is exactly that case. The local hook keeps that trade-off, where speed between
  commits matters; the check of record reads the tracked corpus and the commit alone, as decision 92 asks
  of every CI check.
- It saves no wall time. The job runs beside the test matrix, and the test, pack and consumer jobs take
  about 13 minutes from end to end; the cold replay takes under 5.
- On an empty state, every failing probe fails the job. That includes a probe that passed at its
  identity and fails now, which is what the replay must catch, and also a probe that never passed. A
  probe in the corpus is expected to pass; one that fails for a known defect is fixed, retired or
  removed, not left failing. `--all` includes retired probes, as the release gate does.

Revisit this when `replay --all` on a runner takes longer than the test job's path to green.

**The release replays and gates before it packs.** In `release.yml`'s `verify` job, after the test suite
and before `dotnet pack`, `prober replay --all` runs, and a failing probe fails the release, as a failing
test does and as the replay already failed the release pull request. Then `prober gate --since <tag>`
runs against the previous release's tag, `git describe --tags --abbrev=0 --exclude v<version>`. Without
`--since`, the gate reads `git describe` itself, which on a tag push names the tag being released: the
gate compared the release with itself and found nothing touched. **The gate stays advisory** (AGENTS.md,
"Releasing"): exit 1 is a refusal, reported with its reasons in the job summary and a warning, and the
release goes on. Any other exit, a usage error or a stale dll, means the gate did not run, and fails the
job. Once a binding release is named, the refusal fails the job instead, in a new record.

**The state stays in the job.** `PROBE_STATE_DIR` and `PROBE_CACHE_DIR` point under `$RUNNER_TEMP`,
written to `$GITHUB_ENV` by a step. A job's `env:` cannot read the `runner` context: GitHub refused the
workflow ("Unrecognized named-value: 'runner'").

**Held by a test.** `ReleaseWiringTests` in the conventions tests holds both:

- `dotnet.yml` has exactly one job that runs `prober replay`. It sets `PROBE_STATE_DIR`, builds the
  prober in Release first, runs `replay --all` as a bare run line, with no `|| true` and no
  `continue-on-error`, and runs `status` after it.
- `release.yml`'s `verify` job sets `PROBE_STATE_DIR`, builds first, runs `replay --all` the same way,
  then `gate --since`, and only then `dotnet pack`.

## Consequences

- A regression that the corpus catches fails the topic's pull request on its way into the release branch,
  whether or not its author installed the hook.
- The job summary of every run shows the pair coverage of the documented claims, and a failing run leaves
  each probe's evidence in the `probe-state` artifact.
- Each probe added to the corpus adds its replay time to the job, about 7 to 25 s per process variant on a
  runner.
- The first replay on a runner found a defect in `tools/BuildVerdict`. Under GitHub Actions the test
  platform colours its output although it is redirected, and an escape sequence opened the summary's lines
  (`ESC[m  total: 177`). The verdict found no total and reported every passing probe as
  `TESTS DID NOT REPORT`. It now strips terminal control sequences before it parses (a366509).

## Evidence

Measured 2026-10-15 on `ubuntu-latest` with the pinned SDK, at 8dd02d2 (`release/2.2.0` at dac10f0 with
this change), 7 probes and 15 process variants, all passing:

| Replay | Probes | Time |
|---|---|---|
| `replay --all`, empty state, cold build | 7 of 7 | 287 s (the build 73 s) |
| footprint replay right after it, nothing changed | 0 of 7 | 1 s |
| `replay --all` again, warm build | 7 of 7 | 209 s (the build 4 s) |
| footprint replay after a one-line change to `src/CodoMetis.TypeKit/Option.cs`, warm build | 3 of 7 | 113 s (the build 46 s) |

The state directory was 4.2 MB and the replay worktree 128 MB. The whole `probes` job, without the
measurements, takes about 5 minutes. A run of `dotnet.yml` on `release/2.2.0` earlier the same day took
3 to 6.6 minutes for the test legs and 13 minutes to the last job.

On macOS (arm64), the same `replay --all` from an empty state took 229 s, and the footprint replay after
it 5 s.

The convention test was shown to catch each of these, seeded one at a time: the replay step dropped, its
exit swallowed by `|| true`, `continue-on-error` on it, `replay` without `--all`, `status` dropped, the
prober's build dropped, `PROBE_STATE_DIR` unset in either workflow, the release's replay dropped, its gate
dropped, the gate without `--since`, and `dotnet pack` moved before the replay.
