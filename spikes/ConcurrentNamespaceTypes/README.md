# Spike: reading a namespace's types from an aspect while sibling aspects introduce into it

**Question.** CMTK1007 (a companion class's name is taken) was reported in most builds and missed
in some: the aspect then introduced `TakenNameExtensions` beside the declared class, and the
compiler reported CS0260 instead. Is that a race between aspect instances, and does answering the
question in the fabric, before any introduction, remove it?

**Answer: yes to both.** Measured 2026-09-27 with Metalama 2026.1.28, SDK 10.0.401, on a
consumer with 80 value objects in one namespace and a declared `V7Extensions`
([stress.sh](stress.sh)). The instances of one aspect layer run in parallel on one code model.
`ValueObjectExtensionsAspect` read `namespace.Types.OfName(...)` to find a declared type of the
companion's name while its sibling instances were introducing their companion classes into the
same namespace, and the read sometimes missed a type that is declared in source.

```bash
spikes/ConcurrentNamespaceTypes/stress.sh 24 80                                    # concurrent, the default
spikes/ConcurrentNamespaceTypes/stress.sh 24 80 -p:MetalamaConcurrentBuildEnabled=false
```

## Findings

| # | Configuration | Runs | Missed CMTK1007 |
|---|---|---|---|
| 1 | The check in the extensions aspect, concurrent build on (the default) | 60 | 5 |
| 2 | The same, `MetalamaConcurrentBuildEnabled=false` | 24 | 0 |
| 3 | The check in the fabric, handed to the aspect as a constructor argument, concurrent build on | 48 | 0 |

Row 1 is four runs of the loop before the fix and one with the fix reverted. At the rate of row 1,
48 clean runs happen by chance about once in 250 times.

## Consequences for the design

- **Anything that scans the namespace or the compilation runs in the fabric,** when the aspects
  are selected and nothing has been introduced yet, and reaches the aspect through its
  constructor and the aspect state (`CompanionClass.NameOwner`, `ValueObjectFabric`). An aspect
  reads its own target and what the previous layers left in the state; it does not enumerate its
  siblings' namespace.
- Aspects have no access to the Roslyn model, which is immutable, so this is the only race-free
  place to look.
- The deterministic half of the guard is the three CMTK1007 rows of the build-outcome tests, which
  fail when the fabric hands the aspect no owner. The race itself has no deterministic guard; the
  numbers above are the evidence, and this script reproduces them.
