# Harness generators

The apps here write the generated declarations of the probe harness, every
`tools/probe/corpus/harness/*.g.cs`. Each file's first line names the app that wrote it. Never edit one by
hand: change the app, or its input, and run it again.

| App | Mode | Writes | From |
|---|---|---|---|
| `shapes.cs` | | `Shapes.g.cs`: a value object per `shape.kind` x `shape.wrapped`, the registry, the polymorphic stand-ins, `ProbeContext` | the axes file's `shape.kind` and `shape.wrapped` |
| `containers.cs` | | `Containers.g.cs`: a generic container per `shape.containing` and member style | the axes file's `shape.containing` |
| `ef.cs` | `catalogue` | `EfShapes.g.cs`: a value object per (kind, wrapped, site), `EfRegistry` | E01's first 90 rows, E02's first 108 |
| `ef.cs` | `top-level` | `EfShapes3.g.cs`: every kind at top level for each wrapped type of the catalogue, `Reg3` | the same rows |
| `ef.cs` | `nested` | `EfShapes4.g.cs`: every kind at each nested site for each wrapped type of the catalogue, `Reg4` | the same rows |
| `ef.cs` | `keys` | `KeyShapes.g.cs`: a value object per (kind, wrapped), `KeyRegistry` | E04's rows |
| `json-family.cs` | `contexts` | `JsonFamilyShapes.g.cs`: the source-generated contexts of C01, C10 and L06, and L06's stand-ins over `Option` and `Result` | C01's, C10's and L06's rows |
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
dotnet run tools/probe/generators/shapes.cs -- --axes .claude/probe-axes.md --out tools/probe/corpus/harness/Shapes.g.cs
```

```bash
dotnet run tools/probe/generators/decls.cs -- C09 --rows tools/probe/corpus/rows/C09BaseRecordEqualityProbe.json.gz --out tools/probe/corpus/harness/C09Shapes.g.cs
```

The arguments of every file are in the table `Generators` of
`test/CodoMetis.TypeKit.Conventions.Tests/GeneratedHarnessTests.cs`, the one place the mapping lives, and
the head of each app shows how to run it. Run the app again after `prober rows` rewrites a rows file it reads,
after a change to the axes file's `shape.*` axes, and after a change to the app, and commit the output with
the change.

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
shape (`ef.cs`, `json-family.cs`) writes the same file without a row whose shape another row repeats; `decls.cs`
writes one per row.

They run inside the repository, so `global.json` and the root `Directory.Build.props` apply (warnings are
errors). This directory's `Directory.Build.props` and `Directory.Build.targets` keep `tools/Directory.Build.*`
out: those compile `tools/Shared` into a tool project and stamp its sources for a project file a file-based
app does not have. Never start two copies of one app at once on a cold build cache: the first runs race on it
(decision 92's evidence), which is why the test runs the files one after another.
