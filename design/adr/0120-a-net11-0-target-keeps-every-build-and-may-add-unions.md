# 0120. A `net11.0` target joins a 2.x minor only if it keeps every build, and may add C# 15 unions; 3.0.0 stays the change of an existing contract

Date: 2026-10-29
Status: accepted

Context: 2026-10-29, issue #169; refines [decision 44](0044-2-0-0-net10-0-metalama.md). Decision 44 says a
`net11.0` target is additive and joins a 2.x minor when a feature needs it, and that unions and the `net10.0`
drop are 3.0.0, timed to .NET 12 LTS. The maintainer asked what making 3.0.0 the .NET 11 release would
change, and decided, in these words: "stick to the original plans regarding 3.0.0, add details to new 2.x
minors and .NET 11 related specifics". On Metalama 2027 the maintainer decided the same day (see the
Metalama paragraph). .NET 11 is a release candidate (SDK 11.0.100-rc.1 measured here, RC2
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
   a 2.x minor it is refused, except for Metalama, which has a rule of its own below.
2. **Its public surface is a superset of `net10.0`'s, and a shared member behaves the same on both.** A member
   only `net11.0` has is available on that target alone, and the changelog says so. That includes members that
   use C# 15 unions or `closed` types: the `net10.0` asset has none of them, and a `net11.0` consumer who does
   not use them is untouched. One who uses them on `LangVersion` 14 gets CS9327, an error, never a different
   answer (measured below). What stays out of a 2.x minor is a change to a member both targets have: its
   signature, its return type, its serialization or its behaviour on one target. Two conditions on a union
   member are this repository's own and are decided by the unions spike the roadmap names, not by the version:
   it gives no path to the content of an `Option` or `Result` past `Match` and `TryGetValue` (a compiler-made
   union has a public `object? Value`), and it is not written by System.Text.Json where `Option` and `Result`
   refuse it. A project that has the Generators package, on Metalama 2026.1, can neither declare nor match a
   union (measured below), so until 2.5.0 a union member reaches the consumers without the Generators package
   and those that reference Metalama 2027 themselves.
3. **Generated members are the same on every target.** The generators run in the consumer's compiler whatever
   the asset, and a value object's generated surface is the contract. A member that exists on one target only
   would make a consumer that multi-targets see two value objects under one name; any member the generators
   introduce on `net11.0` is introduced on `net10.0` too, or not at all.
4. **`LangVersion` stays 14 for `net10.0`, and is 15 for `net11.0` once its code uses a C# 15 feature**, in an
   implementation or in a new member's signature. The consumer needs nothing from the compiler the assembly was
   built with, except to use a union or a `closed` type, which needs C# 15 and says so (CS9327). In a project
   that has the Generators package, Metalama's compiler sets the language version, not the SDK: 14 on both
   targets with Metalama 2026.1, 15 on `net11.0` with 2027 (measured below). Generated code compiles under both.
5. **Each target is run, not assumed:** the tests, the Native AOT smoke run and package validation per target,
   and the smoke test's consumer on `net11.0` that holds the lower satellite versions.

**The SDK comes first.** `global.json` pins SDK 10.0.4xx, because a different Roslyn band can change what
Metalama and the analyzers emit. A `net11.0` target needs SDK 11, which changes the compiler under the `net10.0`
assets too, and a consumer on SDK 11 meets it already with the 2.x `net10.0` asset. Before any `net11.0`
target, a spike with no release attached (#170) measures on an SDK 11 compiler: the build and tests, the packed
`net10.0` assets unchanged, a consumer on SDK 11 building and weaving, and the smoke test's SDK 11 lane. The
pin moves by a record of its own, after the SDK is generally available. The pin protects the packages that
Metalama does not touch and this repository's own build: in a project woven by Metalama the SDK does not choose
the compiler either, Metalama's does, so what such a project compiles with follows the Metalama year.

**Metalama is one version for every target of a release, and TypeKit follows the maintained Metalama year in a
minor.** The Generators package stays on Metalama 2026.1 in 2.3 and 2.4, and 2.5.0 moves both targets to 2027.0
once it is stable. The version never differs between the `net10.0` and `net11.0` assets: NuGet resolves one
`Metalama.Framework` per project, that version brings the engine and Metalama's compiler, and different floors
per target would give a consumer that multi-targets two compilers for its two inner builds (LAMA0054 refuses a
project whose `Metalama.Framework` is newer than its engine). This is a standing rule for Metalama alone, not
a general permission: every other dependency keeps the first rule.

It is not new. 1.5.0, a minor, raised the floor from 2026.1.28 to 2026.1.29 (`CHANGELOG.md`, 1.5.0 "Changed"),
the same restore mechanism in a smaller step, because ADR 4 follows the only line Metalama's MIT maintenance
covers, the latest `YYYY.N`. Semantic Versioning's FAQ calls an update of a library's own dependencies with an
unchanged public API compatible, and leaves the conflicts to the dependents. The maintainer decided the move,
in these words: "every consumer that somehow needs Metalama 2026.x pinned for whatever he wants to achieve is
hitting TypeKit's version ceiling, so can continue using any TypeKit version that still allows any Metalama
2026 version; from 2.5.0 by depending my own packages on Metalama 2027 this is a clear, transparent ceiling".
Holding 2026.1 has a cost too: on SDK 11 it reports LAMA0617, its Razor analyzer needing a Roslyn newer than
the one it bundles (the review of 2026-10-29; a warning, falling back to the SDK 10 Razor compiler).

**What a consumer sees is not only a restore error.** A project that references `Metalama.Framework` 2026.1
directly gets NU1605, loud, with a one-line way out (raise it) or the line it already has (TypeKit 2.4.x). The
rest is quieter, and the documents say so:

- a woven `net11.0` project moves from C# 14 to C# 15, with C# 15's own breaking changes (`union` and `closed`
  become contextual keywords, `with(...)` in a collection expression binds differently);
- an IDE host Metalama 2027 does not support shows no Metalama diagnostics, code lens or generated code, and
  Visual Studio 2022 gets a LAMA0602 warning at build (Metalama's platform support document);
- Metalama 2027 needs .NET SDK 10 or newer and declares 11 as its newest, so 3.0.0, or any SDK 12 build, will
  need Metalama 2028;
- a consumer on `Metalama.Patterns` or `Metalama.Extensions` 2026.1 has a floor and no ceiling, so it resolves to
  Framework 2027 with no restore error (inferred; the manifest format is 1 in both, so LAMA0061 should not
  fire; the mix is untested).

The move holds when:

1. **Metalama 2027.0 is stable.** A stable package should not depend on a prerelease: NU5104 is a warning, and
   an error under the warnings-as-errors this repository sets.
2. **A build with no Metalama license passes** in CI against it, as `dotnet.yml` has every job on 2026.1, and in
   a clean container. The preview passed in a clean container (below); the general-availability build is
   repeated, then a CI branch with the floor raised. A conventions test keeps `Metalama.Licensing`, whose
   description says it fails a build without a valid license, out of the restore output.
3. **The weaving review and the smoke test run on 2027.0:** a consumer on Metalama 2027 beside the packages,
   one with a direct 2026.1 reference that shows the NU1605 the changelog describes, one on `Metalama.Patterns`
   2026.1, and the design-time experience in Visual Studio, Rider and VS Code, which no command line shows.
   The `[Durable]` placeholder in `ValueObjectAspect.Templates.cs` returns with the upgrade.
4. **The ceiling is written where a consumer reads it:** 2.5.0's changelog "Changed" and the Generators README
   name the Metalama version and both ways out, the language-version change on `net11.0`, the minimum IDE
   versions with the silent failure named, and what Metalama 2027 drops (Visual Studio 2022, .NET SDK 8 and 9).
5. **The line below the ceiling is declared:** whether 2.4.x gets patches after 2.5.0, and whether Metalama
   2026.1 is still maintained then. ADR 4 says MIT maintenance covers only the latest `YYYY.N`, while Metalama's
   versioning policy page, as summarised, lists 2026.1 as a long-term release; the two are not reconciled.

A consumer that needs Metalama 2027 before TypeKit moves does not wait: a direct reference beside 2.x weaves
(measured below, on the command line only). Metalama checks an order, not equality: compile-time code from
another version is accepted when its manifest format matches (LAMA0061; both read in the 2026.1.31 and
2027.0.4-preview sources by the research of 2026-10-29).

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

**Metalama.** `net11.0` console projects on SDK 11.0.100-rc.1 with the 2.2.0 packages (Metalama 2026.1), a
value object declared and used:

- no union anywhere: builds and weaves;
- a `union` declared in the project: `CS0106`, `CS8803` and `CS1001`, since Metalama's compiler runs the project
  at language version 14, where unions are still a preview feature (the same declaration compiles with no
  Metalama);
- a union returned by a `net11.0`-only library: holding it builds; matching on it: `CS8652`, "unions are in
  preview", where the same match compiles without Metalama.

The same project with a direct `Metalama.Framework 2027.0.4-preview` reference beside the 2.2.0 packages
weaves the value object, and a declared union with a `switch` over it builds and runs. `#error version` in the same
projects names the compiler: Metalama's `2026.1.20` at language version 14.0 on `net10.0` and on `net11.0`, and
`2027.0.4-preview` at 15.0 on `net11.0`, whatever the SDK. One limit on my runs: this machine has a Metalama
license entry for 2027.0 in its licensing file and compiler servers were already running, so they say nothing
about a build without a license. CI on 2026.1 does (`dotnet.yml`, every job runs with no Metalama license on
the machine), and so did the review's run in a clean `mcr.microsoft.com/dotnet/sdk:10.0` container with
`UseSharedCompilation=false` and warnings as errors: a `net10.0` application with the 2.2.0 packages and
`Metalama.Framework 2027.0.4-preview` restored, wove and ran (`From`, `Parse`, JSON), and created no licensing
file; the same with 2026.1.30. Not established: the general-availability build, the IDEs, and
`Metalama.Patterns` beside it.

Adding a target is not additive by itself, though. The roadmap's "EF Core 11 and Microsoft.AspNetCore.OpenApi
11 in the satellites" is that case, and reads as the test lane in the first rule above from now on.

## Consequences

- The roadmap's `net11.0` item names these rules, and its 3.0.0 item is unchanged except that it says what a
  union may do in a 2.x minor. Decision 44's "unions in the public surface are 3.0.0" is narrowed here to a
  union that changes an existing member; the maintainer's reading of Microsoft's compatibility guidance
  (a newer target may add to an older one's surface), checked against the measurement above.
- Decision 44 stands; its index row says it is refined here.
- Metalama 2026.1 and the 2.2.0 packages build and weave a `net11.0` project on SDK 11 RC1 (measured above).
  What #170 still owns is the rest of the SDK 11 spike, the general-availability SDKs and `rc.3` packages, the
  analyzers on that compiler, the NU1605 measurement repeated there, and the license-free build on Metalama 2027.
- The roadmap's Metalama 2027.0 item becomes 2.5.0 (#173), on the conditions above. Its old test, "a minor if a
  consumer's own aspects still build and weave", missed the restore error of a direct lower reference, the
  language-version change and the IDE hosts.
