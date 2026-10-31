---
verified: 2026-10-31
pins:
  - external .NET-SDK 11.0.100-rc.3.26522.148
  - external Metalama.Framework 2026.1.30
  - external Metalama.Framework 2027.0.5-preview
source: spikes/Sdk11Rc3Metalama, measured 2026-10-31 on macOS arm64 with the published CodoMetis.TypeKit and CodoMetis.TypeKit.Generators 2.3.1, against SDK 11.0.100-rc.3.26522.148 and 11.0.100-rc.1.26425.128, each alone in its .NET installation, and RC3 with SDK 10.0.403 beside it
turned-on: decision 120 (a net11.0 target in 2.4.0 on Metalama 2026.1, and Metalama 2027.0 in 2.5.0), and the RC3 part of issue 170
test: none, a measurement of SDKs and compilers that CI does not install; the spike's run.sh and web-matrix.sh repeat it
---

# KB-060. Metalama 2026.1 weaves a `net11.0` project on SDK 11 RC3 at C# 14, and a web project fails when SDK 11 is the only SDK

On SDK 11.0.100-rc.3, with the 2.3.1 packages (`Metalama.Framework` 2026.1.30, `Metalama.Compiler` 2026.1.20):

- A `net11.0` console project with a value object builds, weaves and runs: `From`, `Parse` and a JSON round trip.
  No warning.
- `#error version` names Metalama's compiler 2026.1.20 at language version 14.0. `Metalama.Framework` 2026.1.31
  (compiler 2026.1.21) is at 14.0 too.
- A `union` declared in the project fails with `CS0106`, `CS1001` and `CS8803`; a `switch` over a union returned by
  a `net11.0`-only library fails with `CS8652` ("unions ... in Preview"); holding that union builds. Without Metalama
  both compile.
- A direct `Metalama.Framework` 2027.0.5-preview reference beside the 2.3.1 packages: compiler 2027.0.5-preview at
  15.0, the value object woven, and both union cases build and run.

These are the RC1 results of decision 120, unchanged.

**A web project (`Microsoft.NET.Sdk.Web`) on Metalama 2026.1 fails with `LAMA0625` when SDK 11 is the only SDK in
its .NET installation**, on RC1 and on RC3, with 2026.1.31, and for a `net10.0` project as for a `net11.0` one. The SDK 11 Razor compiler needs Roslyn 5.11 (RC1) or
5.12 (RC3), Metalama 2026.1 bundles 5.9, and Metalama finds no older SDK in that installation to take a compatible
Razor compiler from ("Installed SDKs found: 11.0.100"). With SDK 10.0.403 in the same installation the build passes
with two `LAMA0617` warnings (the ASP.NET analyzers from SDK 10.0.401, the Razor compiler from SDK 10.0.403).
Metalama 2027.0.5-preview builds the web project with neither. Pinned to SDK 10.0.403, `net10.0`, both versions build
it with no warning.

Not measured: the general availability SDK, Linux and Windows, an IDE, a Razor Pages or Blazor project, and any
2027 build without a Metalama license on the machine (this machine has a license entry for 2027.0).
