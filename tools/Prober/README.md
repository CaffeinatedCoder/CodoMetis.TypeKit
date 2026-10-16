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
| `status`, `stale`, `universe`, `rows [--claim C \| --probe P]`, … | none | anywhere |
| `gaps [--base REF]` | none | anywhere: what the corpus leaves out, each with its recorded reason; a report that decides nothing (decision 104) |
| `new --claim C [--probe P] [--family F]` | none | anywhere: stages a probe's skeleton, its own rows and its registry entry with no seed yet, and prints what the writer still owes |
| `try --probe P [--rows i,j,...] [--no-build]` | none | locally: builds once in the replay's worktree and runs that one probe, every row or the rows given; records nothing, and holds the worktree from a replay while it runs |
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
it is (decision 102). The harness's generated declarations, `harness/*.g.cs`, are written by the file-based
apps of `tools/probe/generators/` from the rows and the axes file, and `GeneratedHarnessTests` in the
conventions tests holds each to its app's output (that directory's README).

Rows are per probe: `rows/<probe>.json.gz`, one file for each registered probe, its claim inside (`claim`), so
two probes of one claim (C07's `BaseProbe` and `C07BlanketMappingProbe`) each run rows of their own, and a
probe's identity reads its own file only: a sibling's rows changing leaves it as it was. The probe's
`Rows.Data("P")` names the file, and the replay, `try` and the identity read it. `rows --probe P` writes P's file
whole into the staging, never appended: a covering array over its claim's pairs less those the claim's other
probes have credited (P's own credit is not subtracted, since it came from the rows the file replaces), and
`publish` moves it into the corpus by probe; `rows --claim C` only prints such an array. The stored rows are
definitions, the evidence is in the state. `ProbeRowsTests` in the conventions tests refuses two rows of one
file with the same axis map, a file whose `claim` is not in `claims.json` or is not its probe's, a file that
names no registered probe, and a registered probe without a file.

A registry entry may list `watched`: paths, files or directories, that the probe's result depends on and its
footprint cannot see, since the footprint is what ran in the probe's own process under `src/` beside the
`compileTimeFootprint` paths. A variant catalogue under `test/` compiled into the probes is one (C19 builds
over the analyzer tests' `TypeArgumentSites` and `TypeArgumentKinds`), a build input a child process reads is
another (C18's consumer build compiles against the base package and evaluates `src/Directory.Build.*`). The
replay records each with its object id in the footprint line, a change to one stales the footprint like a path
the probe ran, and the gate counts it as touching the probe's claim.

The round lock, `round.lock` and its mutex, sits in the branch's directory (`branches/<branch>/`), beside
everything a round or a replay writes, so rounds and replays of two branches run at once and two of one branch
never do. The scratch worktrees, `wt` (the replay's), `seeds`, `explore` (the `prober` agent's) and `enumerate`,
are per checkout: under `PROBE_CACHE_DIR` when set, else `~/.cache/probe-replay/<checkout>-<hash of its path>`,
and `replay --where` prints them. So a linked worktree (`git worktree add`, as each session under
`.claude/worktrees/` is) builds a warm copy of its own, about a gigabyte of disk, and two sessions never reset
each other's copy; the main checkout keeps the directory it had before. Since the lock is per branch and the
worktree per checkout, a replay also holds `replay.lock` beside them (an OS lock, gone with its process), and a
replay of another branch in the same checkout, after a branch switch, is skipped while one runs. Removing a
linked worktree leaves its cache behind: before you remove it, run there the `remove:` line that `replay --where`
prints, which removes the cache's worktrees (`git worktree remove`) and then its directory, every path written out.

A claim lists what it promises in `obligations`, each an id and a sentence quoted verbatim from the claim's
source, and what it does not promise in `not-promised`, each an id and the sentence of the document or record
that makes the difference intended, quoted from that source. `universe` checks every quote as it checks a
narrowing's and refuses a sentence its document does not hold, or an id listed twice. A claim without
`obligations` has one, the claim itself, under the id `claim`. `status` shows per claim its obligations and the
ones no seed names, and `gaps` prints every not-promised entry and narrowing beside the settings no surface
reaches, the refused shapes and values, and the axes file's `out-of-reach` surfaces (decision 104).

A pair of a claim's axes counts as covered only by evidence a replay recorded: a check that ended as a pass, in a
row that was expected and executed, for the settings that actually applied, while the result's identity holds
(the probe, every compiled corpus source, the rows, the axes file, the build environment, this tool's
version and the schema version) and the product files the probe ran are unchanged. A row's checks
(`Row.Check` in the harness) each name an obligation and a baseline and record the axes they read; a check is
credited with those and the row's own (read outside any check), and a pair is never put together from two
checks' axes. A row without checks is one check of the obligation `claim` with the row's category. Evidence is
version 2 (`Schemas/evidence.schema.json`): the replay copies each row's `checks` beside its `applied` and
`reasons`. `status --json` gives each obligation `current`, the pairs current checks naming it earned, and
`passed`; the text line counts rows and checks by outcome and ends with the obligations no passing check named
at the last replay (`K uncredited`).

## Seeds

Each probe's registry entry names a seeded product defect: a patch under `seeds/` against the product
files (the replay's `include`), an environment toggle, or both, and the sentence `defect`. Where its claim
lists obligations, the entry's `seeds` holds one such seed per obligation the probe checks, each naming it in
`obligation`, and every listed obligation of a claim with probes has a seed in one of them (decision 104).
`seeds` applies each seed on its own in the `seeds` worktree beside the replay's, builds, runs the probe
and counts the seed caught only for a check that ends as a violation, with no harness error in it or in its
row outside its checks (decision 103), in a row whose unseeded run had no violation of the same obligation; a
seed naming an obligation is caught only by a check naming it, so a probe whose rows run no checks (each the
one check `claim`) catches only seeds that name none. The unseeded rows come from the ledger where the probe's
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
nothing and is never a seed's catch. It stages the probe's own rows, a covering array over its claim's pairs the
claim's other probes have not covered, as `rows --probe P` writes them, and the registry entry with an empty
`seeds`, so `seeds` and `Seeds.Problems` name the seed each obligation owes. P defaults to the claim id's
prefix and words (`C20-testing-shouldhold` is `C20TestingShouldholdProbe`); it refuses a claim that is not in
the claims, a name a probe has, and a claim whose first surface is in no family, or not in `--family`'s.

Then fill each check and run `try --probe P` until each row ends as it should. It moves the replay's worktree
to HEAD, overlays the corpus and the staging as the replay does, builds once through the adapter (not with
`--no-build`), and runs P alone in each of its registry variants, or only the rows `--rows` names, through the
harness's `PROBE_ROWS`, within the rows each variant's `PROBE_ONLY` selects. It prints each run's verdict line
and how each row ended, and exits with the probe's code (1 when the build fails). It records no evidence,
footprint or run and takes no round lock, but holds the replay's worktree (`replay.lock`) while it runs, so a try
refuses while a replay of the checkout runs, and a replay skips while a try does. Then write a seed per
obligation and register it with the command `new` printed for that obligation (`register --obligation O`,
under "Seeds"), show each caught with `seeds --probe P`, and `publish`. `register` lints the staged sources
first (below). A writer needs no build or sync script of their own.

## Register and publish

`register` and `publish` read the staged harness and probe sources first and refuse, by file and line, a
`Note` whose first argument is not a `NoteKeys` member the corpus declares, a harness `Skip` whose finding
is not a string literal naming a finding this ledger holds open (decision 103), and a check's `Inapplicable` or
`Refused` whose first argument is not a `Cite` factory with a string literal, or names what does not exist: a
not-promised entry the probe's claim does not list (any claim's, for a harness source no one probe owns), a docs
file, a knowledge entry under `design/knowledge/`, a record under `design/adr/`. `ProbeCiteTests` in the
conventions tests holds the same over the tracked corpus. The scan blanks out comments
and string literals and reads text, since this tool references the base class library only.

## The adapter

Building the probes, running one probe in one process, and deriving the generated axes go through
`IProbeAdapter` (`ProbeAdapter.cs`), configured by the `replay` object of `.claude/probe-axes.md`. The .NET
adapter is the only one; the core holds nothing of .NET's, so another stack can plug in its own.

## Freshness

The dll hashes its own sources, the schemas among them, at build time and checks them at startup, as every
tool does (`tools/Directory.Build.targets`): a changed, added or removed source makes it refuse with exit 3
and name the command that rebuilds it, `dotnet build tools/Prober -c Release`. Content hashes, never times.
