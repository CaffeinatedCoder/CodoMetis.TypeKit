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
| `replay [--all]` | none | anywhere, CI included: the probes whose identity or footprint changed, or all of them |
| `gate [--since REF]` | none | anywhere, CI included: needs only the tracked corpus and a replay |
| `status`, `stale`, `universe`, `rows`, … | none | anywhere |
| `round explore\|full` | the `prober` agent | locally only, under caps it cannot overrun |

No command in the gate's path needs a login, an API key or a Claude executable. A gate obligation is met
by a probe in the corpus, written by hand or by a round, or by a reviewed waiver in
`tools/probe/corpus/waivers.json`.

## Files

The corpus is tracked under `tools/probe/corpus/`: claims, the probe registry, harness and probe sources,
rows (gzipped), waivers and dismissed findings. State goes under `PROBE_STATE_DIR` when set (CI sets it),
else the main checkout's `.claude/agent-memory-local/prober/`. Every file's shape is in `Schemas/`, versioned;
a file or line without `schemaVersion` is version 1.

A pair of a claim's axes counts as covered only by evidence a replay recorded: a row that was expected,
executed and ended as a pass, for the settings that actually applied, while the result's identity holds
(the probe, every compiled corpus source, the rows, the axes file, the build environment, this tool's
version and the schema version) and the product files the probe ran are unchanged.

## The adapter

Building the probes, running one probe in one process, and deriving the generated axes go through
`IProbeAdapter` (`ProbeAdapter.cs`), configured by the `replay` object of `.claude/probe-axes.md`. The .NET
adapter is the only one; the core holds nothing of .NET's, so another stack can plug in its own.

## Freshness

The dll hashes its own sources at build time and checks them at startup: a changed source makes it rebuild
itself before answering, or, under `PROBER_NO_REBUILD=1`, refuse with exit 3. Content hashes, never times.
