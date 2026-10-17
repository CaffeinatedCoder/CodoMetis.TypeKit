# Harness generators

The apps here write the generated declarations of the probe harness, every
`tools/probe/corpus/harness/*.g.cs`. Each file's first line names the app that wrote it. Never edit one by
hand: change the app, or its input, and run it again.

| App | Mode | Writes | From |
|---|---|---|---|
| `shapes.cs` | | `Shapes.g.cs`: a value object per `shape.kind` x `shape.wrapped`, its plain twin, the registry, the polymorphic stand-ins, `ProbeContext` | the axes file's `shape.kind`, `shape.wrapped`, `shape.position` and `shape.containing`; L06's rows for its carriers |
| `containers.cs` | | `Containers.g.cs`: a generic container per `shape.containing` and member style | the axes file's `shape.containing` |
| `ef.cs` | `catalogue` | `EfShapes.g.cs`: a value object per (kind, wrapped, site), `EfRegistry` | E01's first 90 rows, E02's first 108 |
| `ef.cs` | `top-level` | `EfShapes3.g.cs`: every kind at top level for each wrapped type of the catalogue, `Reg3` | the same rows |
| `ef.cs` | `nested` | `EfShapes4.g.cs`: every kind at each nested site for each wrapped type of the catalogue, `Reg4` | the same rows |
| `ef.cs` | `keys` | `KeyShapes.g.cs`: a value object per (kind, wrapped), `KeyRegistry` | E04's rows |
| `json-family.cs` | `stand-ins` | `JsonFamilyShapes.g.cs`: L06's polymorphic stand-ins over `Option` and `Result` | the table in the app |
| `json-family.cs` | `l05` | `L05Shapes.g.cs`: a value object or closed set per (kind, wrapped, inheritance, site), `L05Registry` | L05's rows |
| `decls.cs` | `C09` | `C09Shapes.g.cs`: each row's base records and value object, `C09Table` | C09's rows |
| `decls.cs` | `C11` | `C11Shapes.g.cs`: each row's value object and its control, `C11Table` | C11's rows |
| `faults.cs` | | `FaultClaimControllers.g.cs`: the controllers of C04, C06, C14 and C15, `FxCtl` | the table in the app |
| `rules.cs` | | `RuleControllers.g.cs`: the controllers of C03 and C05, one action per arity and response variant | the table in the app |

The axes file is `.claude/probe-axes.md`, read from its `json probe-axes` block. A rows file is
`tools/probe/corpus/rows/<probe>.json.gz`, gzipped JSON, one per probe with its claim inside, which `prober rows --probe P`
writes. A value the app's
tables do not know (a new `shape.wrapped`, say) is refused with exit 2 and the value named, never skipped.

## Running one

From the repository root, each app takes its inputs and `--out` by name:

```bash
dotnet run tools/probe/generators/shapes.cs -- --axes .claude/probe-axes.md --l06 tools/probe/corpus/rows/L06OffTheWireProbe.json.gz --out tools/probe/corpus/harness/Shapes.g.cs
```

```bash
dotnet run tools/probe/generators/decls.cs -- C09 --rows tools/probe/corpus/rows/C09BaseRecordEqualityProbe.json.gz --out tools/probe/corpus/harness/C09Shapes.g.cs
```

The arguments of every file are in the table `Generators` of
`test/CodoMetis.TypeKit.Conventions.Tests/GeneratedHarnessTests.cs`, the one place the mapping lives, and
the head of each app shows how to run it. Run the app again after `prober rows` rewrites a rows file it reads,
after a change to the axes file's `shape.*` axes, and after a change to the app, and commit the output with
the change.

## The twin and the context

`shapes.cs` writes each value object's twin beside it: the wrapped type at the value object's nullability, so a class value object
over a struct faces the struct made nullable (`int?` for a sealed record class over `int`) and the rest face the wrapped type itself.
The closed set's twin is a string, its member's name. The registry's `Plain` is the twin, and the harness builds both sides of a
comparison in one place (`Sides.Twins`), which refuses, as a harness error, a twin and a value object that do not hold the same shape.

`ProbeContext` is the one source-generated context: every value object and every twin, at the root and at every `shape.position` in every
`shape.containing`, and the `Option` and `Result` carriers L06's rows hold under the context. Under
`json.TypeInfoResolver=source-generated-context` a row resolves through it alone, with no reflection behind it, and a shape it does not
list is a harness error; `json.TypeInfoResolverChain=context-then-default` is the configuration with the reflection fallback. It is one
declaration because System.Text.Json's generator fails on a second partial part that carries `[JsonSerializable]`. The carriers come from
L06's rows, so run `shapes.cs` again after `prober rows --probe L06OffTheWireProbe`: an `Option` and two `Result`s over every value object
at every position would be some 16000 more types. Measured 2026-10-17 on Linux x64, a rebuild of the Samples after a harness change took
38 s before the context listed the positions, 73 s with it (8974 types: every shape and twin, the struct value objects made
nullable, L06's carriers), and 3 min 49 s in a trial that listed every carrier at every position too (23266 types).

`ef.cs` reads `--rows <path>` once per file; `--first <n>` after one reads only its first n rows. The
catalogue is E01's and E02's first round of rows: the rows appended since (old rows kept) run on the
complete grids of `top-level` and `nested`, which read the same rows to learn the catalogue's wrapped types,
in its order, and which of them it validates.

## The guard

`GeneratedHarnessTests` in the conventions tests runs every app into a temporary directory with the
arguments of its table and fails on any difference from the committed file, line endings aside. It also
fails on a `.g.cs` in the harness that the table does not list. The test takes about 13 seconds, warm or cold
(measured 2026-10-16 on macOS arm64: each `dotnet run` costs about a second, a first build little more).

The apps are file-based apps, the first form of
[decision 92](../../../design/adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md): one file each, BCL
only, no tests of their own. The conventions test is their guard, and it compares outputs, so it sees what a
change does to the harness rather than how the app does it. An app that writes one declaration per distinct
shape (`ef.cs`, `json-family.cs`'s `l05`, `shapes.cs`'s carriers) writes the same file without a row whose shape another row repeats;
`decls.cs` writes one per row.

They run inside the repository, so `global.json` and the root `Directory.Build.props` apply (warnings are
errors). This directory's `Directory.Build.props` and `Directory.Build.targets` keep `tools/Directory.Build.*`
out: those compile `tools/Shared` into a tool project and stamp its sources for a project file a file-based
app does not have. Never start two copies of one app at once on a cold build cache: the first runs race on it
(decision 92's evidence), which is why the test runs the files one after another.
