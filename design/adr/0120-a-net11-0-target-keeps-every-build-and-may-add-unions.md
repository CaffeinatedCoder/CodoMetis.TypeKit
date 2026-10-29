# 0120. A `net11.0` target joins a 2.x minor only if it keeps every build, and may add C# 15 unions; 3.0.0 stays the change of an existing contract

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

**3.0.0 stays timed as decision 44 and the roadmap have it:** the `net10.0` drop, and `Option` and `Result`
becoming C# 15 unions if the spike allows it, at .NET 12 LTS (November 2027) or at the end of .NET 10's
support, whichever its consumers need. A 3.0.0 for .NET 11 was considered and refused. A `net11.0`-only major
strands the consumers on the .NET 10 LTS line, supported to November 2028, which is the argument that refused
a `net11.0`-only 2.0.0. And nothing .NET 11 offers needs a break: unions in `Option` and `Result` wait for the
spike the roadmap names, since the compiler-made union types have a public `object? Value` and are serialized
by System.Text.Json. The version number names the API, never the runtime.

**What 3.0.0 holds is a change to an existing contract, never an addition.** Decision 44 puts "unions in the
public surface" in 3.0.0 as "two contracts under one version", which is true of a union that replaces or
retypes an existing member, and of `Option` or `Result` becoming unions: their identity, their serialization
and their pattern matching would differ between two versions of the same member. A union that is new, and
exists on `net11.0` only, is an addition like any other, and may join a 2.x minor (second rule below). A minor
that fixes bugs for every consumer and adds to `net11.0` alone is a plain 2.x minor.

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
2. **Its public surface is a superset of `net10.0`'s, and a shared member behaves the same on both.** A member
   only `net11.0` has is available on that target alone, and the changelog says so. That includes members that
   use C# 15 unions or `closed` types: the `net10.0` asset has none of them, and a `net11.0` consumer who does
   not use them is untouched. One who uses them on `LangVersion` 14 gets CS9327, an error, never a different
   answer (measured below). What stays out of a 2.x minor is a change to a member both targets have: its
   signature, its return type, its serialization or its behaviour on one target. Two conditions on a union
   member are this repository's own and are decided by the unions spike the roadmap names, not by the version:
   it gives no path to the content of an `Option` or `Result` past `Match` and `TryGetValue` (a compiler-made
   union has a public `object? Value`), and it is not written by System.Text.Json where `Option` and `Result`
   refuse it.
3. **Generated members are the same on every target.** The generators run in the consumer's compiler whatever
   the asset, and a value object's generated surface is the contract. A member that exists on one target only
   would make a consumer that multi-targets see two value objects under one name; any member the generators
   introduce on `net11.0` is introduced on `net10.0` too, or not at all.
4. **`LangVersion` stays 14 for `net10.0`, and is 15 for `net11.0` once its code uses a C# 15 feature**, in an
   implementation or in a new member's signature. The consumer needs nothing from the compiler the assembly was
   built with, except to use a union or a `closed` type, which needs C# 15 and says so (CS9327). Generated
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

A library with `net10.0` (`LangVersion` 14) and `net11.0` assets, whose `net11.0` asset alone declares
`public union Pet(Cat, Dog)` and a method returning it, consumed by:

- `net11.0`, C# 15 by default, with an exhaustive `switch` over the union: builds;
- `net11.0` with `LangVersion` 14, holding the union in a `var` and printing it: builds;
- `net11.0` with `LangVersion` 14, pattern matching on it: `CS9327`, "The feature 'unions' is not available in
  C# 14.0. Use language version 15.0 or greater";
- `net10.0`: builds, and sees only the shared members.

The SDK needs no `preview` flag for any of it (RC1), as [KB-046](../knowledge/KB-046-csharp-15-unions-ship-with-the-net-11-sdk.md)
says of RC2. So a union is addable on `net11.0` in a minor. What measurement one cannot make from this repository
yet is the unions spike's: what `Option` and `Result` may offer a union without losing the guarantees above.

Adding a target is not additive by itself, though. The roadmap's "EF Core 11 and Microsoft.AspNetCore.OpenApi
11 in the satellites" is that case, and reads as the test lane in the first rule above from now on.

## Consequences

- The roadmap's `net11.0` item names these rules, and its 3.0.0 item is unchanged except that it says what a
  union may do in a 2.x minor. Decision 44's "unions in the public surface are 3.0.0" is narrowed here to a
  union that changes an existing member; the maintainer's reading of Microsoft's compatibility guidance
  (a newer target may add to an older one's surface), checked against the measurement above.
- Decision 44 stands; its index row says it is refined here.
- Whether Metalama 2026.1 and the analyzers hold under the SDK 11 compiler is open until #170.
