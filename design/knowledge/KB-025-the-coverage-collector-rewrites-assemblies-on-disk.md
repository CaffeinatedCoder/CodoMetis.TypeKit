---
verified: 2026-10-16
pins:
  - external Microsoft.Testing.Extensions.CodeCoverage 18.12.0
  - sdk 10.0.4xx
source: the probe replay on macOS (arm64), 43 probes run four and sixteen at a time from one output directory with --coverage, the kept logs of the failed hosts, and the collector's runsettings as the Microsoft.Testing.Platform repository documents them, measured 2026-10-16
turned-on: decision 101 (a replay that records a footprint runs one host at a time; parallel runs record none)
test: none, a race between processes that no single test can hold; the replay's --parallel runs with and without a footprint are the measurement
---

# KB-025. The code coverage collector instruments the assemblies on disk, so test hosts started together from one output directory corrupt each other; its in-memory mode records no coverage here

On macOS (arm64) with SDK 10.0.402, `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0 run with `--coverage` and
no settings file instruments the assemblies of the output directory in place (static managed instrumentation)
and restores them when the host ends. Hosts started together from one directory load each other's half-written
or restored files: of 68 processes of the probe replay started sixteen at a time, 42 exited 7 having run zero
tests; four at a time, 14 and then 56. The kept logs of 61 failed hosts show, in 53, a
`BadImageFormatException` loading an assembly with a garbage identity (`'<Unknown>'. Index not found`,
`Version=1024.32.11.22528`), and in one a `MissingMethodException` for an instrumented constructor
(`zjo7…ctor`). The same 68 processes four at a time without `--coverage` all ran (40 passed, the 3 expected
failures), and six hosts of one light probe with coverage happened to survive, so the race is one of timing,
not of count.

A runsettings file passed with `--coverage-settings` that turns static instrumentation off and dynamic managed
instrumentation on (`EnableStaticManagedInstrumentation` False, `EnableDynamicManagedInstrumentation` True,
`EnableStaticNativeInstrumentation` False, `EnableDynamicNativeInstrumentation` False, the form the platform's
own integration tests use) lets eight hosts run at once with no crash, but every cobertura report then names no
class under `src/` and no line with a hit: on this platform the dynamic mode collects nothing the footprint can
read. Not measured on Linux x64 or Windows.

What follows for TypeKit: a replay that records a footprint runs one host at a time; the parallel runs are the
ones that record none ([decision 101](../adr/0101-ci-replays-by-family-and-footprint.md)).
