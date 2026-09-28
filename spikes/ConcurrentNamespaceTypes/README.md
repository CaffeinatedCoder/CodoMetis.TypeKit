# Spike: finding a declared type by name while Metalama runs its callers in parallel

**Question.** CMTK1007 (a companion class's name is taken) was reported in most builds and missed
in some: the aspect then introduced `TakenNameExtensions` beside the declared class, and the
compiler reported CS0260 instead. Is that a race, where is it, and what removes it?

**Answer: a race inside Metalama's code model, removed by enumerating instead of looking up by
name.** Measured 2026-09-28 with Metalama 2026.1.28, SDK 10.0.401, 16 cores.

`INamespace.Types.OfName(name)`, like every by-name lookup on a type or member collection, reads a
by-name index that Metalama builds lazily and without a lock (`NonUniquelyNamedUpdatableCollection`
in Metalama.Framework.Engine). It reads the index, then separately whether the collection is
complete, and writes the index back from the copy it read. Another caller that completes the
collection in between (any enumeration of it does) makes it return nothing for a type that is
declared: once, or, when its stale write-back lands after the completion, for every later lookup of
a name that the stale copy lacks. Enumerating goes through the collection's lock (`EnsureComplete`)
and always sees every type.

Metalama runs the callers in parallel on one code model: the instances of one aspect layer, and the
fabric's `AddAspect` factory, which a `SelectTypes` query runs on up to `Environment.ProcessorCount`
threads. `CompanionClass.NameOwner` asked `OfName` for its own companion's name and then
enumerated the namespace for rivals, so the first enumeration raced every other value object's
`OfName`.

The first answer (2026-09-27, rows 1 to 3) was wrong about the cause. It blamed sibling instances
introducing their companion classes into the namespace, and moved the check from the extensions
aspect into the fabric. The fabric still called `OfName` in parallel, and its 0 misses in 48 were
chance: measured again at that commit, it missed in 13 of 300 builds (row 4).

```bash
spikes/ConcurrentNamespaceTypes/stress.sh 300 80                                    # concurrent, the default
spikes/ConcurrentNamespaceTypes/stress.sh 100 80 -p:MetalamaConcurrentBuildEnabled=false
```

The script builds a consumer with 80 value objects in one namespace and a declared `V7Extensions`.
Rows 4, 6, 8, 9 and 11 build the build-outcome tests' consumer instead (the `Declarations` of
`BuildOutcomeTests`, about 60 value objects and `TakenNameExtensions`) in the same loop, with
`error CMTK1007: 'TakenName'` as the expected line.

## Findings

| # | Configuration | Consumer | Runs | Missed CMTK1007 |
|---|---|---|---|---|
| 1 | `OfName` in the extensions aspect, concurrent build on (the default) | stress | 60 | 5 |
| 2 | The same, `MetalamaConcurrentBuildEnabled=false` | stress | 24 | 0 |
| 3 | `OfName` in the fabric, handed to the aspect as a constructor argument, concurrent build on | stress | 48 | 0 |
| 4 | Row 3 measured again (commit aa41ed6), concurrent build on | build-outcome | 300 | 13 |
| 5 | The same | stress | 300 | 9 |
| 6 | The same, `MetalamaConcurrentBuildEnabled=false` | build-outcome | 100 | 0 |
| 7 | The same, `MetalamaConcurrentBuildEnabled=false` | stress | 100 | 0 |
| 8 | Both lookups in one build, the enumerated one deciding, concurrent build on | build-outcome | 150 | 0; `OfName` found nothing in 10 |
| 9 | The fix: enumerate and compare names, concurrent build on | build-outcome | 300 | 0 |
| 10 | The same | stress | 300 | 0 |
| 11 | The fix reverted | build-outcome | 150 | 4 |

A miss shows up in two ways, and both occur (rows 4 and 5: 5 and 3 CS0260, 8 and 6 LAMA0531). CS0260: `OfName` wrote back a stale
index, so Metalama's own check before introducing the class missed the declared type too, and the
build compiled a clash. LAMA0531 with LAMA0041: only our lookup raced, and Metalama's check still
saw the type and failed the aspect.

Row 8 is the direct evidence: in the same build, on the same code model, `OfName` answered "no
such type" for `TakenNameExtensions` while enumerating the namespace found it.

## Consequences for the design

- **Compile-time code never looks up the code model by name.** It enumerates a collection and
  compares names: no `OfName`, no `OfExactSignature` or `OfCompatibleSignature` (both go through
  `OfName`), and no string indexer on `Fields`, `Properties` or `Events` (`Single(OfName(name))`).
  The race has no deterministic guard, so `CodeModelLookupTests` in the conventions tests pins its
  cause: it fails on any such lookup in `.Generators`.
- **Anything that scans the namespace or the compilation still runs in the fabric,** when the
  aspects are selected and nothing has been introduced yet, and reaches the aspect through its
  constructor and the aspect state (`CompanionClass.NameOwner`, `ValueObjectFabric`). The fabric is
  not free of parallelism, though; it is only free of introductions.
- The three CMTK1007 rows of the build-outcome tests stay the deterministic half: they fail when
  the fabric hands the aspect no owner. Building the consumer several times in that test was
  considered and not done: at the rate of row 4, five builds catch the race in one test run out of
  five, at five times the fixture's cost, while the conventions test catches the cause every time.
- The lock-free index is a Metalama defect, and Metalama's own checks read it in parallel too
  (the CS0260 mode above). It is not reported upstream yet.
