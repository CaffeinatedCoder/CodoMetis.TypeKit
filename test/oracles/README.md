# Oracles

How bug-finding works here as a system rather than one sweep per bug (design/adr/0112): a catalogue of what the
documents promise, an enumeration of what the API offers, oracle tests that check the library against an
independent answer, seeds that prove each oracle can fail, and a registry of the disagreements found but not yet
fixed. It is CodoMetis.ValueRanges' kit, kept to what pays for itself: no covering arrays, no ledger, no replay by
footprint, no agent. The oracles are ordinary tests in the projects that already have their harness, and run with
the suite.

| File | What it holds | Held by |
|---|---|---|
| `claims.json` | One entry per sentence a consumer-facing document promises: its `source` heading, the `quote` verbatim, the `oracle` kind, the `surfaces` (public members, `TypeName.Member`, `*` a wildcard) and `heldBy`, the test classes that assert it. An empty `heldBy` is the worklist. | `OracleCatalogueTests` |
| `surface-waivers.json` | The public members no claim names, each with why it needs no oracle. A reason that starts `unread:` names a document not yet read for claims, and the issue that reads it. | `OracleSurfaceCoverageTests` |
| `oracles.json` | The test classes that are oracles: checks against an independent answer over many cases, not worked examples. | `OracleSeedTests` |
| `seeds/*.patch` | A defect in `src/` with a `Seed:` line, the `Oracles:` it fails and what `Fails:`. Every oracle has one. | `OracleSeedTests`, and `oracles seeds` |
| `candidates.json` | Disagreements an oracle found that are not fixed yet, each tied to an issue (`Candidates.Verdict`). | `OracleCandidateTests` |
| `axes.md` | What a consumer varies, which tests vary it, and the pairs none varies yet. | read by hand |

`test/CodoMetis.TypeKit.Oracles.Tests` holds all of it in CI without building anything but itself: a quote its
document no longer holds, a surface or a test class that does not exist, a public member neither claimed nor
waived, an oracle without a seed, and a seed that no longer applies all fail there.

Whether a seed is still **caught** takes a build, so it is a local tool, run before a release and after changing an
oracle or the code a seed patches:

```bash
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll seeds [name filter]   # each seed: a copy, the patch, one project built, one class run
dotnet tools/Oracles/bin/Release/net10.0/oracles.dll status                # the worklist: unheld claims, unread waivers, unseeded oracles, candidates
```

`seeds` copies the working tree (without `bin/`, `obj/` and `.git/`, so uncommitted work is included), builds each
oracle the selected seeds name and runs that one class: the baseline, with no seed. Then, for each seed, it copies
the tree again, applies the patch, and builds and runs the same classes with the same filter. A seed is caught when
its oracle passes without it and fails with it, in a test of its own: a failed test with a row that is not a known
candidate (`SeedVerdict`, design/adr/0117). It prints one `BASELINE` line per oracle, then one line per seed and
oracle:

| Verdict | Meaning | Exit code |
|---|---|---|
| `CAUGHT` | The baseline passes, and with the seed a test fails on its own assertion or on what the code it calls throws. | 0 when every pair is caught |
| `MISSED` | The baseline passes, and with the seed every test passes, or fails only on known candidates. | 1 |
| `ERROR` | A run never reached the tests' own code, or the baseline fails. | 4 |

The TRX decides what reached the tests. A fixture that fails to construct or initialize fails every test with
xunit's own message (`Collection fixture type 'SqlServer' threw in InitializeAsync`). A fixture that throws in
`Dispose` shows up only as an `error` in the TRX's counters. A crashed host writes no TRX and exits with something
other than 0 or 2. A run whose every test was skipped ran nothing. Each of these is `ERROR`, with or without the
seed: a dead fixture fails every test either way, so its failures prove nothing about the seed. On an arm64 Mac,
run it with `TYPEKIT_MSSQL_IMAGE=mcr.microsoft.com/azure-sql-edge:latest`, as the suite runs (CONTRIBUTING's Docker
bullet), or the SQL Server oracles are `ERROR`.

To add an oracle: write the test class, register it in `oracles.json`, write a seed by planting the defect it
guards against, `git diff` it into `seeds/<defect>.patch` under its header lines, undo the edit, and run
`oracles seeds <defect>`.
