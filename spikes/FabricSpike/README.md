# Spike: transitive project fabric instead of `[Inheritable]`

**Question.** Can the value-object aspects be applied by a `TransitiveProjectFabric` shipped in the
aspect package, so the package holding `IValue<T>` / `IValidatedValue<,,>` needs no Metalama at all?
The answer decides the package boundaries in [docs/plan.md](../../docs/plan.md).

**Naming (2026-09-27).** The spike predates the package names. `Spike.Abstractions` stands for
`CodoMetis.TypeKit` and `Spike.ValueObjects` for `CodoMetis.TypeKit.Generators`. The spike keeps
its original names as a record of what was run.

**Answer: yes.** First run on 2026-09-26 with .NET SDK 10.0.401 and Metalama 2027.0.3-preview.
The same day it was re-run on **2026.1.28** (latest stable), which is now the target (see C).

## Layout

| Project | References | Role |
|---|---|---|
| `Spike.Abstractions` | nothing | Marker interfaces plus a minimal `Result`. No Metalama. |
| `Spike.ValueObjects` | Abstractions, Metalama.Framework | An **internal** aspect and an **internal** `TransitiveProjectFabric` that selects every non-abstract type directly implementing a marker |
| `Spike.Domain` | ValueObjects | Declares `OrderId : IValue<Guid>` and `EmailAddress : IValidatedValue<…>` |
| `Spike.App` | Domain **only** | Declares its own `Sku : IValue<string>`, reaching ValueObjects only through Domain |
| `Spike.AbstractionsOnly` | Abstractions only | Negative control |

```bash
dotnet build spikes/FabricSpike/FabricSpike.slnx
dotnet run --project spikes/FabricSpike/Spike.App --no-build
```

## Findings

| # | Claim | Result | Evidence |
|---|---|---|---|
| 1 | The fabric weaves types in a directly referencing project | ✅ | `Spike.Domain.dll` contains `_value`, `get_Value`, `From`, `IsValid` |
| 2 | …and in a project that reaches the fabric only transitively | ✅ | `Spike.App` calls `Sku.From` and `Sku.Value`; CS0183 "always of type `IValueObject<Sku,string>`" proves the interface was introduced |
| 3 | The aspect and the fabric can stay `internal` | ✅ | Both are `internal sealed`; no LAMA diagnostics |
| 4 | Static abstract members introduced by the aspect satisfy generic constraints | ✅ | `Wrap<TVO,T>() where TVO : IValueWrapper<TVO,T>` calls `TVO.From` |
| 5 | The same works across a **package** reference, not only a project reference | ✅ | Packed to `artifacts/spike-feed`, consumed by a throwaway project outside the repository: `Sku.Value=SKU-1 Probe4=4` |
| 6 | `Metalama.Framework` reaches the consumer although the nuspec says `exclude="Build,Analyzers"` | ✅ | Same run as #5. This matches Metalama's docs: a transitive fabric makes the framework flow, and no `PrivateAssets` change is needed |
| 7 | The abstractions package (now `CodoMetis.TypeKit`) stays Metalama-free | ✅ | `Spike.Abstractions` has no Metalama reference, and its nupkg has no dependencies |
| 8 | Implementing a marker **without** the aspect package fails loudly | ❌ **silent** | `Spike.AbstractionsOnly` builds clean, and `Orphan : IValue<int>` has no `Value`, no `From`, no field. See consequence A |
| 9 | Five aspect classes on one type build with no license | ✅ | The Metalama "Open Source" edition (MIT) has no aspect-class limit. The old 3-class limit belonged to the pre-2025 "Free" edition. The local run could not prove the machine license was ignored, because Metalama wrote nothing to the redirected HOME. **Confirmed 2026-09-27 on a clean machine**: in a fresh `mcr.microsoft.com/dotnet/sdk:10.0` container (SDK 10.0.401), with no license file and no `METALAMA_*` variable anywhere, the whole solution, ten aspect classes on every value object, builds in Release, and the woven probes pass all 254 generator tests. Afterwards Metalama had written only its extraction cache, no `licensing.json`. CI runs every job the same way |
| 10 | The same code builds on the latest stable, 2026.1.28 | ✅ after one change | `[Durable]` (`Metalama.Framework.Utilities`) does not exist before 2027.0 (CS0246). Without it the template placeholder compiles on 2026.1. `IDurableRef` / `ToDurableRef()` already exist in 2026.1, so aspect state keeps them |
| 11 | A package built on 2026.1.28 works in a consumer that pins 2027.0.3-preview | ✅ | The consumer resolves `Metalama.Framework/2027.0.3-preview`, weaves `Sku.Value` and `Probe4`, and reports no LAMA0870 |

## Consequences for the design

**A. The analyzer travels with the base package, not with the aspects.** Finding 8 is the
fabric's price. With `[Inheritable]` on the interface, anyone who could name `IValue<T>` also got
the aspect. Now they get it only by referencing `.Generators`. So `CodoMetis.TypeKit` brings the
analyzer package along (plan §10). When a type implements a marker and the compilation does not
reference `CodoMetis.TypeKit.Generators`, it reports an error (CMTK0002). For the same reason
`ForbiddenStructDefaultInitializationAnalyzer` (CMTK0001) travels the same way, since it guards
the interfaces wherever they are visible.

**B. Aspects can stay internal.** Finding 3 means no Metalama type leaks into the public API of
`CodoMetis.TypeKit.Generators`. The only public surface is what the aspects generate.

**C. Metalama version: 2026.1 (decided 2026-09-26).** Target the latest stable, 2026.1.x. Code
stays forward-compatible with 2027.0: aspect state uses `IDurableRef`. The one 2027.0-only
attribute, `[Durable]` on the `_value` template placeholder, is left out, with a comment saying to
add it back on upgrade. Finding 11 shows a consumer on a newer Metalama is not blocked by this.
MIT covers only the latest `YYYY.N`: once 2027.0 is stable, 2026.1 stops receiving fixes under
MIT, so the upgrade is planned rather than optional.

**D. Still open (not covered by this spike):**
- IDE design time: does Rider or VS show the introduced members in a consumer's IntelliSense?
- A consumer project that itself uses other Metalama aspects: fabric ordering, and
  `AspectOrder` across assemblies (the ten aspects are ordered with an assembly-level attribute
  in the aspect assembly).
- Whether a consumer that disables Metalama (`MetalamaEnabled=false`, as analyzer test projects
  often do) still gets the analyzer from finding A.
