# 0120. A `net11.0` target joins a 2.x minor only if it keeps every build; C# 15 and the `net10.0` drop stay 3.0.0

Date: 2026-10-29
Status: accepted

Context: 2026-10-29, issue #169; refines [decision 44](0044-2-0-0-net10-0-metalama.md). Decision 44 says a
`net11.0` target is additive and joins a 2.x minor when a feature needs it, and that unions and the `net10.0`
drop are 3.0.0, timed to .NET 12 LTS. The maintainer asked what making 3.0.0 the .NET 11 release would
change, and decided, in these words: "stick to the original plans regarding 3.0.0, add details to new 2.x
minors and .NET 11 related specifics". .NET 11 is a release candidate (SDK 11.0.100-rc.1 measured here, RC2
named in [KB-046](../knowledge/KB-046-csharp-15-unions-ship-with-the-net-11-sdk.md)), with general
availability expected in November 2026.

## Decision

**3.0.0 stays as decision 44 and the roadmap have it:** C# 15 unions in the public surface and the `net10.0`
drop, timed to .NET 12 LTS (November 2027) or to the end of .NET 10's support, whichever its consumers need.
A 3.0.0 for .NET 11 was considered and refused. A `net11.0`-only major strands the consumers on the .NET 10
LTS line, supported to November 2028, which is the argument that refused a `net11.0`-only 2.0.0. And nothing
.NET 11 offers needs a break: unions in `Option` and `Result` wait for the spike the roadmap names, since the
compiler-made union types have a public `object? Value` and are serialized by System.Text.Json. The version
number names the API, never the runtime.

**A `net11.0` asset joins a 2.x minor when a feature needs it, and only if it keeps every consumer's build.**
No patch adds a target. The next minor after the one in flight carries it; the number is not reserved here.
It holds when:

1. **Its dependency groups keep the `net10.0` group's floors.** An application on `net11.0` may hold the lower
   version of a satellite's dependency (EF Core 10 and Microsoft.AspNetCore.OpenApi 10 are `net10.0` packages,
   which a `net11.0` project may reference).
   NuGet picks the `net11.0` group for it, and a floor that group raises is a downgrade error, NU1605,
   on update (measured below). The satellites compile against the floors they have and are *tested* against
   EF Core 11 and OpenAPI 11. A feature that needs an API only EF Core 11 or OpenAPI 11 has waits for 3.0.0,
   unless it lives in a package no consumer has installed yet. Raising a floor is a release decision, and in
   a 2.x minor it is refused.
2. **Its public surface is a superset of `net10.0`'s, and nothing in it needs C# 15 to call.** A member only
   `net11.0` has is available on that target alone, and the changelog says so. No union type and no `closed`
   type appears in a public signature before 3.0.0.
3. **Generated members are the same on every target.** The generators run in the consumer's compiler whatever
   the asset, and a value object's generated surface is the contract. A member that exists on one target only
   would make a consumer that multi-targets see two value objects under one name; any member the generators
   introduce on `net11.0` is introduced on `net10.0` too, or not at all.
4. **`LangVersion` stays 14, and becomes per target only when the `net11.0` code uses a C# 15 feature inside
   an implementation.** The consumer needs nothing from the compiler the assembly was built with. Generated
   code compiles under C# 14 and under the C# 15 that SDK 11 defaults a `net11.0` consumer to.
5. **Each target is run, not assumed:** the tests, the Native AOT smoke run and package validation per target,
   and the smoke test's consumer on `net11.0` that holds the lower satellite versions.

**The SDK comes first.** `global.json` pins SDK 10.0.4xx, because a different Roslyn band can change what
Metalama and the analyzers emit. A `net11.0` target needs SDK 11, which changes the compiler under the `net10.0`
assets too, and a consumer on SDK 11 meets it already with the 2.x `net10.0` asset. Before any `net11.0`
target, a spike with no release attached (#170) measures on an SDK 11 compiler: the build and tests, the packed
`net10.0` assets unchanged, a consumer on SDK 11 building and weaving, and the smoke test's SDK 11 lane. The
pin moves by a record of its own, after the SDK is generally available.

**The 2.x line after 3.0.0** is decided with 3.0.0, not here.

## Evidence

SDK 11.0.100-rc.1.26425.128, a throwaway package `FloorLib` 1.0.0 with a `net10.0` group requiring
`Newtonsoft.Json >= 12.0.3` and a `net11.0` group requiring `>= 13.0.3`. An application that references
`Newtonsoft.Json 12.0.3` directly and `FloorLib`:

- on `net11.0`: `NU1605`, "Detected package downgrade: Newtonsoft.Json from 13.0.3 to 12.0.3", with the path
  `App -> FloorLib 1.0.0 -> Newtonsoft.Json (>= 13.0.3)`; an error under the SDK's defaults;
- on `net10.0`: restores.

Adding a target is therefore not additive by itself. The roadmap's "EF Core 11 and Microsoft.AspNetCore.OpenApi
11 in the satellites" is that case, and reads as the test lane in the first rule above from now on.

## Consequences

- The roadmap's `net11.0` item names these rules, and its 3.0.0 item is unchanged.
- Decision 44 stands; its index row says it is refined here.
- Whether Metalama 2026.1 and the analyzers hold under the SDK 11 compiler is open until #170.
