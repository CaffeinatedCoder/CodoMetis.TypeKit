# prober

The probing workflow's core: the ledger of what the documents claim and what the probes have shown, the
replay of the probe corpus, the release gate, and the capped exploration round. Repository tooling, not
a package. It references the base class library only and is built with the solution.

```text
dotnet tools/Prober/bin/Release/net10.0/prober.dll help
```

## What runs where

| Command | Model | Where |
|---|---|---|
| `replay [--all] [--family F] [--parallel N] [--no-footprint]` | none | anywhere, CI included: the probes whose identity or footprint changed, or all of them; one family of the replay configuration; N probe processes at a time (the cores by default); without coverage or a footprint, for a state nobody keeps (decision 101) |
| `merge --from DIR...` | none | anywhere, CI included: joins the states of a replay split by family, and refuses while a registered probe was in no leg |
| `gate [--since REF]` | none | anywhere, CI included: needs only the tracked corpus and a replay |
| `status`, `stale`, `universe`, `rows`, … | none | anywhere |
| `gaps [--base REF]` | none | anywhere: what the corpus leaves out, each with its recorded reason; a report that decides nothing (decision 104) |
| `new --claim C [--probe P] [--family F]` | none | anywhere: stages a probe's skeleton, its claim's rows and its registry entry with no seed yet, and prints what the writer still owes |
| `try --probe P [--rows i,j,...] [--no-build]` | none | locally: builds once in the replay's worktree and runs that one probe, every row or the rows given; records nothing and takes no lock |
| `round explore\|full` | the `prober` agent | locally only, under caps it cannot overrun |
| `seeds [--probe P]` | none | locally only: one build per seed, in a scratch worktree (decision 100) |

No command in the gate's path needs a login, an API key or a Claude executable. A gate obligation is met
by a probe in the corpus, written by hand or by a round, or by a reviewed waiver in
`tools/probe/corpus/waivers.json`.

## Files

The corpus is tracked under `tools/probe/corpus/`: claims, the probe registry, harness and probe sources,
rows (gzipped), waivers and dismissed findings. State goes under `PROBE_STATE_DIR` when set (CI sets it),
else the main checkout's `.claude/agent-memory-local/prober/`. Every file's shape is in `Schemas/`, versioned;
a file or line without `schemaVersion` is version 1. `claims.json` is checked against its schema wherever it is
read, and a field the schema does not have is refused with the claim and the field named; each `exclude` entry
is a reason and its pairs, which `universe` prints beside the claim's narrowings. The JSON files the prober
writes keep `+`, backticks and `<` literal, so a `publish` that changes nothing in a tracked file leaves it as
it is (decision 102).

A claim lists what it promises in `obligations`, each an id and a sentence quoted verbatim from the claim's
source, and what it does not promise in `not-promised`, each an id and the sentence of the document or record
that makes the difference intended, quoted from that source. `universe` checks every quote as it checks a
narrowing's and refuses a sentence its document does not hold, or an id listed twice. A claim without
`obligations` has one, the claim itself, under the id `claim`. `status` shows per claim its obligations and the
ones no seed names, and `gaps` prints every not-promised entry and narrowing beside the settings no surface
reaches, the refused shapes and values, and the axes file's `out-of-reach` surfaces (decision 104).

A pair of a claim's axes counts as covered only by evidence a replay recorded: a row that was expected,
executed and ended as a pass, for the settings that actually applied, while the result's identity holds
(the probe, every compiled corpus source, the rows, the axes file, the build environment, this tool's
version and the schema version) and the product files the probe ran are unchanged.

## Seeds

Each probe's registry entry names a seeded product defect: a patch under `seeds/` against the product
files (the replay's `include`), an environment toggle, or both, and the sentence `defect`. Where its claim
lists obligations, the entry's `seeds` holds one such seed per obligation the probe checks, each naming it in
`obligation`, and every listed obligation of a claim with probes has a seed in one of them (decision 104).
`seeds` applies each seed on its own in the `seeds` worktree beside the replay's, builds, runs the probe
and counts the seed caught only for a row that ends as a violation, raised no harness error and does not
violate in the unseeded probe (decision 103). The unseeded rows come from the ledger where the probe's
evidence is current, else from an unseeded run first. Nothing it runs is evidence; it
writes `seeds-last.json` (`Schemas/seeds-run.schema.json`), one outcome per seed, reported as
`P/<obligation>` where the seed names one. `register --obligation O --seed PATCH --defect TEXT` (or
`--seed-env NAME=VALUE...` for a toggle) writes a seed into `seeds`, in place of any seed of the entry that
names O, `seed` included, and refuses an O that the claim's entry in `claims.json` does not list; without
`--obligation`, `--seed` and `--seed-env` set the one `seed`. No seed is written into the registry by hand.
A seed is not part of a probe's identity, neither `seed` nor `seeds`. Without a build,
`Seeds.Problems`, which the prober tests run over the repository, refuses a probe without a seed, a seed that
does not apply, a seed naming an obligation its claim does not list, and a listed obligation no seed names.

## Writing a probe

`new --claim C` starts one in the branch's staging, whose path it prints. It writes `probes/P.cs` in the form
the corpus's probes have (`Rows.Data`, `[Theory, MemberData(nameof(Data))]`, `Rec.Run`), headed by the claim's
source and each obligation's id and sentence, with one check per obligation that throws until it is written:
the probe builds beside the others, and every row of it ends as a harness error, which fails the row, earns
nothing and is never a seed's catch. It stages the claim's rows, the ones it has at their indices and then a
covering array over its pairs not yet covered, as `rows` prints it, and the registry entry with an empty
`seeds`, so `seeds` and `Seeds.Problems` name the seed each obligation owes. P defaults to the claim id's
prefix and words (`C20-testing-shouldhold` is `C20TestingShouldholdProbe`); it refuses a claim that is not in
the claims, a name a probe has, and a claim whose first surface is in no family, or not in `--family`'s.

Then fill each check and run `try --probe P` until each row ends as it should. It moves the replay's worktree
to HEAD, overlays the corpus and the staging as the replay does, builds once through the adapter (not with
`--no-build`), and runs P alone in each of its registry variants, or only the rows `--rows` names, through the
harness's `PROBE_ROWS`, within the rows each variant's `PROBE_ONLY` selects. It prints each run's verdict line
and how each row ended, and exits with the probe's code (1 when the build fails). It records no evidence,
footprint or run and takes no lock, so a replay at the same time shares its worktree. Then write a seed per
obligation and register it with the command `new` printed for that obligation (`register --obligation O`,
under "Seeds"), show each caught with `seeds --probe P`, and `publish`. `register` lints the staged sources
first (below). A writer needs no build or sync script of their own.

## Register and publish

`register` and `publish` read the staged harness and probe sources first and refuse, by file and line, a
`Note` whose first argument is not a `NoteKeys` member the corpus declares, and a harness `Skip` whose finding
is not a string literal naming a finding this ledger holds open (decision 103). The scan blanks out comments
and string literals and reads text, since this tool references the base class library only.

## The adapter

Building the probes, running one probe in one process, and deriving the generated axes go through
`IProbeAdapter` (`ProbeAdapter.cs`), configured by the `replay` object of `.claude/probe-axes.md`. The .NET
adapter is the only one; the core holds nothing of .NET's, so another stack can plug in its own.

## Freshness

The dll hashes its own sources, the schemas among them, at build time and checks them at startup, as every
tool does (`tools/Directory.Build.targets`): a changed, added or removed source makes it refuse with exit 3
and name the command that rebuilds it, `dotnet build tools/Prober -c Release`. Content hashes, never times.
