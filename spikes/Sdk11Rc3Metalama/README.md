# Spike: does Metalama 2026.1 still hold on the .NET 11 RC3 SDK?

**Status: run 2026-10-31, answered.** The RC3 part of [#170](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/170):
[decision 120](../../design/adr/0120-a-net11-0-target-and-documented-floor-raises-join-a-minor.md)'s Metalama
measurement, made on SDK 11.0.100-rc.1 on 2026-10-29, repeated on release candidate 3. Recorded as
[KB-060](../../design/knowledge/KB-060-metalama-2026-1-on-the-net-11-sdk.md).

**Question.** With the published 2.3.1 packages (Metalama 2026.1) in a `net11.0` project on SDK 11.0.100-rc.3:

1. does a value object with no union build, weave and run (`From`, `Parse`, JSON)?
2. which compiler, at which language version, does `#error version` name?
3. what does a `union` declared in the project give?
4. what does a `switch` over a union returned by a `net11.0`-only library give?
5. is LAMA0617 still reported, and is anything new?
6. what changes with a direct `Metalama.Framework` 2027 reference beside the 2.3.1 packages?

**Answer: every RC1 result holds on RC3.** Metalama 2026.1 builds, weaves and runs a `net11.0` value object, its
compiler (2026.1.20) runs the project at C# 14, a declared union fails with `CS0106`, `CS8803` and `CS1001`, and a
`switch` over a library's union with `CS8652`. With `Metalama.Framework` 2027.0.5-preview the compiler is 2027 at
C# 15 and both union rows build and run. The latest 2026.1 patch (2026.1.31, compiler 2026.1.21) is also at C# 14.

**One finding that the RC1 runs could not show, and that is not an RC3 change.** A web project
(`Microsoft.NET.Sdk.Web`, which loads the Razor source generator) on Metalama 2026.1 **fails** with `LAMA0625`
when SDK 11 is the only SDK in its .NET installation, on RC1 as on RC3, with 2026.1.31 too, and whether the
project targets `net11.0` or `net10.0`: a 2.x web application that moves its build to SDK 11 alone stops building. Metalama's compiler
bundles Roslyn 5.9; the SDK 11 Razor compiler needs 5.11 (RC1) or 5.12 (RC3), and Metalama looks for an older SDK to
take a compatible Razor compiler from. With SDK 10.0.403 installed beside it, that search succeeds and the build passes
with the `LAMA0617` warning decision 120 describes. Decision 120's runs had SDK 10.0.403 beside RC1, so they saw only
the warning. A console project reports neither. Metalama 2027.0.5-preview builds the web project in every cell.
Filed as [#275](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/275).

## The projects

- `UnionLib`: a `net11.0` library with no Metalama that declares `public union Pet(Cat, Dog)` and returns one.
- `App`: a `net11.0` console project on `CodoMetis.TypeKit` and `CodoMetis.TypeKit.Generators` 2.3.1, with
  `OrderId : IValue<Guid>`. Properties switch the rows (`App.csproj` lists them): `ProbeVersion`, `DeclareUnion`,
  `HoldUnion`, `MatchUnion`, `MetalamaFramework=<version>` (a direct reference) and `NoGenerators` (the control).
- `WebApp`: the same value object as a minimal-API route parameter, in `Microsoft.NET.Sdk.Web`. It suppresses
  ASP0020, the documented false alarm on a route parameter
  ([decision 12](../../design/adr/0012-asp0020-documented-suppressed.md)), which fires on SDK 10 as on SDK 11.

`nuget.config` clears every source but nuget.org, and the runs restore into an empty `NUGET_PACKAGES` folder, so
the 2.3.1 packages are the published ones. Every build runs with `UseSharedCompilation=false` and
`-nodeReuse:false`, after `dotnet build-server shutdown` and with no compiler process left.

## Running it

The SDKs are installed side by side with Microsoft's script, leaving `/usr/local/share/dotnet` as it is:

```bash
./dotnet-install.sh --version 11.0.100-rc.3.26522.148 --install-dir ~/.dotnet-sdk11-rc3 --no-path
./dotnet-install.sh --version 11.0.100-rc.3.26522.148 --install-dir ~/.dotnet-sdk11-rc3-sdk10 --no-path
./dotnet-install.sh --version 10.0.403 --install-dir ~/.dotnet-sdk11-rc3-sdk10 --no-path
./dotnet-install.sh --version 11.0.100-rc.1.26425.128 --install-dir ~/.dotnet-sdk11-rc1 --no-path
```

```bash
DOTNET=~/.dotnet-sdk11-rc3/dotnet NUGET_PACKAGES=<empty folder> spikes/Sdk11Rc3Metalama/run.sh <out>
RC3_ALONE=~/.dotnet-sdk11-rc3/dotnet RC3_BESIDE=~/.dotnet-sdk11-rc3-sdk10/dotnet RC1_ALONE=~/.dotnet-sdk11-rc1/dotnet \
  NUGET_PACKAGES=<same folder> spikes/Sdk11Rc3Metalama/web-matrix.sh <out>
```

`run.sh` builds `App` once per row and runs what builds; `web-matrix.sh` builds `WebApp` under each installation,
the SDK 10, RC1 and `net10.0` cells from a copy whose `global.json` pins that SDK. Each writes a log per row and `summary.txt`.

## Evidence

SDK 11.0.100-rc.3.26522.148 (runtime 11.0.0-rc.3.26522.148), macOS 27.0.1 on arm64. `CodoMetis.TypeKit` and
`CodoMetis.TypeKit.Generators` 2.3.1, which restore `Metalama.Framework` 2026.1.30 and `Metalama.Compiler`
2026.1.20; `Metalama.Framework` 2026.1.31 restores `Metalama.Compiler` 2026.1.21.

### The rows, console project, RC3 the only SDK

| Row | Build | Diagnostics | Run | RC1 (decision 120) |
|---|---|---|---|---|
| control: no TypeKit, no Metalama, a declared union and a library union matched | passes | none | `declared union: circle 1,5`, `matched union: cat Tom` | same |
| 1. value object, no union | passes | none | `From` prints the `Guid`, `Parse` equal, JSON `"0b9f7d3a-..."` read back equal | builds and weaves |
| 2. `#error version` | fails, as meant | `CS8304`: Compiler version '2026.1.20 (4a1b04c1...)'. Language version: 14.0 | | 2026.1.20 at 14.0 |
| 2b. `#error version`, `Metalama.Framework` 2026.1.31 | fails, as meant | `CS8304`: '2026.1.21 (c1b05ff9...)'. Language version: 14.0 | | not measured |
| 3. `public union Shape(Circle, Square)` in the project | fails | `CS0106` the modifier 'public' is not valid; `CS1001` identifier expected; `CS8803` top-level statements must precede namespace and type declarations | | `CS0106`, `CS8803`, `CS1001` |
| 4a. library union held in a `var` and printed | passes | none | `held: UnionLib.Pet` | builds |
| 4b. `switch` over the library union | fails | `CS8652`: The feature 'unions' is currently in Preview and *unsupported* | | `CS8652` |
| 5. warnings in rows 1 to 4 | | none: no `LAMA0617`, no other warning | | `LAMA0617` reported on a web project |
| 6. row 1, `Metalama.Framework` 2027.0.5-preview | passes | none | as row 1 | builds and weaves (2027.0.4-preview) |
| 6. row 2, 2027.0.5-preview | fails, as meant | `CS8304`: '2027.0.5-preview (dc7c9a1c...)'. Language version: 15.0 | | 2027.0.4-preview at 15.0 |
| 6. row 3, 2027.0.5-preview | passes | none | as row 1, then `declared union: circle 1,5` | builds and runs |
| 6. row 4b, 2027.0.5-preview | passes | none | as row 1, then `matched union: cat Tom` | not measured |

### Row 5 again: the web project, by installation

| Installation | Metalama 2026.1 (2.3.1's) | `Metalama.Framework` 2026.1.31 | `Metalama.Framework` 2027.0.5-preview |
|---|---|---|---|
| RC3 alone | **fails**: `LAMA0625`, plus `LAMA0617` and `CS8034` | **fails**: the same | passes, no warning |
| RC3 with SDK 10.0.403 beside | passes: `LAMA0617` twice | not run | passes, no warning |
| RC3 alone, `net10.0` | **fails**: `LAMA0625` | not run | passes, no warning |
| RC1 alone | **fails**: `LAMA0625` | not run | passes, no warning |
| SDK 10.0.403, `net10.0` (today) | passes, no warning | not run | passes, no warning |

The diagnostics, as reported on RC3 alone:

- `error LAMA0625`: Analyzer 'Microsoft.CodeAnalysis.Razor.Compiler.dll' from .NET SDK 11.0.100-rc.3.26522.148
  requires Roslyn 5.12.0.0, newer than the Roslyn 5.9.0.0 bundled with Metalama Compiler. Metalama searched all .NET
  SDKs installed on this machine for a compatible older version of 'Microsoft.CodeAnalysis.Razor.Compiler.dll', but
  none qualifies. To fix, install a .NET SDK whose 'Microsoft.CodeAnalysis.Razor.Compiler.dll' references Roslyn
  5.9.0.0 or earlier and pin it in global.json, or upgrade Metalama Compiler to a version supporting Roslyn 5.12.0.0.
  Installed SDKs found: 11.0.100.
- `warning LAMA0617`: Analyzer 'Microsoft.AspNetCore.App.Analyzers.dll' from .NET SDK (unknown) requires Roslyn
  5.11.0.0, newer than the Roslyn 5.9.0.0 bundled with Metalama Compiler. Metalama supplied a compatible version of
  'Microsoft.AspNetCore.App.Analyzers.dll' from .NET SDK 10.0.401 instead.
- `warning CS8034`: Unable to load Analyzer assembly
  `metalama.compiler/2026.1.20/sdkAnalyzers/Microsoft.AspNetCore.Razor.Utilities.Shared.dll`: Could not load file or
  assembly 'Microsoft.AspNetCore.Razor.Utilities.Shared, Version=10.4.0.0'.

On RC1 alone the Razor compiler needs Roslyn 5.11.0.0 and `LAMA0625` is the only diagnostic. With SDK 10.0.403
beside RC3, the two `LAMA0617` warnings name the ASP.NET analyzers taken from SDK 10.0.401 and the Razor compiler
taken from the SDK 10.0.403 beside it.

`Metalama.Compiler` 2026.1.20 carries copies of the SDK's analyzers and generators in its `sdkAnalyzers` folder,
`Microsoft.CodeAnalysis.Razor.Compiler.dll` among them; the `CS8034` above is the load of one of them failing. Whether
that failure is what leaves `LAMA0625` without a fallback is not established here.

"Installed SDKs found: 11.0.100" while this machine has SDK 10.0.403 in `/usr/local/share/dotnet` and 10.0.303 in
`~/.dotnet`: the search covers the .NET installation the build runs from, not the machine.

## Limits

- This machine has a Metalama licensing file with an entry for 2027.0, so the 2027 cells say nothing about a build
  without a license. The file was not changed by these runs. #170 keeps the license-free CI job.
- macOS arm64 only, the command line only (no IDE), and console and minimal-API projects only. The general
  availability SDK, Linux, Windows and a Razor Pages or Blazor project are not measured.
- An SDK 11 container image (`mcr.microsoft.com/dotnet/sdk:11.0`) holds one SDK, so it is the "RC3 alone" cell by
  construction; it was not run.
- Not repeated here: the repository's own build on SDK 11, the woven `net10.0` assets compared, the analyzers on the
  SDK 11 compiler, and the NU1605 measurement. They remain #170's.
