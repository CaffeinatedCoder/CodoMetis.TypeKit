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
| `surface-waivers.json` | The public members no claim names, each with why it needs no oracle. A reason that starts `unread:` names a document not yet read for claims (#98). | `OracleSurfaceCoverageTests` |
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

`seeds` copies the working tree (without `bin/`, `obj/` and `.git/`, so uncommitted work is included), applies the
patch, builds each named oracle's project and runs that one class. A seed is caught when one of the class's tests
fails with a row that is not a known candidate (`SeedVerdict`). One line per seed and oracle, `CAUGHT`, `MISSED` or
`ERROR`, and exit code 1 unless every pair is caught.

To add an oracle: write the test class, register it in `oracles.json`, write a seed by planting the defect it
guards against, `git diff` it into `seeds/<defect>.patch` under its header lines, undo the edit, and run
`oracles seeds <defect>`.
