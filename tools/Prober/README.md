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

A pair of a claim's axes counts as covered only by evidence a replay recorded: a row that was expected,
executed and ended as a pass, for the settings that actually applied, while the result's identity holds
(the probe, every compiled corpus source, the rows, the axes file, the build environment, this tool's
version and the schema version) and the product files the probe ran are unchanged.

## Seeds

Each probe's registry entry names a seeded product defect: a patch under `seeds/` against the product
files (the replay's `include`), an environment toggle, or both, and the sentence `defect`. `seeds` applies
each in the `seeds` worktree beside the replay's, builds, runs the probe and counts the seed caught only for
a row that ends as a violation and does not in the unseeded probe. The unseeded rows come from the ledger
where the probe's evidence is current, else from an unseeded run first. Nothing it runs is evidence; it
writes `seeds-last.json` (`Schemas/seeds-run.schema.json`). A seed is not part of a probe's identity.

## The adapter

Building the probes, running one probe in one process, and deriving the generated axes go through
`IProbeAdapter` (`ProbeAdapter.cs`), configured by the `replay` object of `.claude/probe-axes.md`. The .NET
adapter is the only one; the core holds nothing of .NET's, so another stack can plug in its own.

## Freshness

The dll hashes its own sources, the schemas among them, at build time and checks them at startup, as every
tool does (`tools/Directory.Build.targets`): a changed, added or removed source makes it refuse with exit 3
and name the command that rebuilds it, `dotnet build tools/Prober -c Release`. Content hashes, never times.
