# 0129. Metalama 2027.0 moves into 2.4.0, so a consumer with only the .NET 11 SDK builds

Date: 2026-10-31
Status: accepted

Context: 2026-10-31, issue #280; replaces the timing in
[decision 120](0120-a-net11-0-target-and-documented-floor-raises-join-a-minor.md)'s Metalama paragraph ("2.3 and 2.4
stay on 2026.1, and 2.5.0 moves both targets to 2027.0"). Decision 120 named the cost of holding 2026.1 on SDK 11 as
LAMA0617, "a warning, falling back to the SDK 10 Razor compiler". The RC3 spike (`spikes/Sdk11Rc3Metalama`,
[KB-060](../knowledge/KB-060-metalama-2026-1-on-the-net-11-sdk.md)) found that the fallback exists only when an SDK 10
is installed beside SDK 11: without one, a web project with the Generators package fails with LAMA0625 (#275). The
maintainer decided, in these words: "I want a 2.4.0 that compiles at every .NET11-consumer, even when he has only
.NET 11 SDK installed. If this doesn't work with Metalama 2026, then we need to take a straight decision", and
accepted the proposal below the same day.

## Decision

**The Generators package moves to Metalama 2027.0 in 2.4.0, for the `net10.0` and `net11.0` assets alike.** Every
other rule of decision 120 stands: one Metalama version for every target, the floor raise documented under its
first rule, and the five conditions of its Metalama paragraph, which are now 2.4.0's. Where decision 120 says 2.5.0
of the move, read 2.4.0, and where it says the 2.4.x line, read 2.3.x.

**2.4.0 is tagged only on a stable Metalama 2027.0.** A stable package does not depend on a prerelease (NU5104,
an error under this repository's warnings as errors), so 2.4.0's date is Metalama's: 2027.0 is planned for
2027-01-01 in Metalama's internal documents (#173), with no public date on 2026-10-31. A fix a consumer needs before
then ships as a 2.3.x patch.

**One condition is added: a build with only SDK 11 installed.** CI and the consumer smoke test get a lane whose
.NET installation holds SDK 11 and nothing else, with a web consumer (`Microsoft.NET.Sdk.Web`) on the packages,
because that is the case Metalama 2026.1 fails and the one a machine with SDK 10 beside it never shows. It runs on
the general availability SDK before the tag (#281).

**2.3.x is the line below the ceiling.** A project that needs Metalama 2026 stays on 2.3.x. On a machine with only
SDK 11, its web projects need SDK 10 installed beside SDK 11, or a direct `Metalama.Framework` 2027 reference beside
the 2.3.x packages, which weaves (KB-060, decision 120); #275 says so, and 2.4.0's changelog names both.

Refused:

- **Staying on 2026.1 in 2.4.0 and documenting SDK 10 beside SDK 11.** It fails the requirement, on `net10.0` as on
  `net11.0`: the Razor compiler is the SDK's, whatever the target (KB-060).
- **A 2.4.0 without `net11.0`, on 2026.1.** The same: a `net10.0` web project fails on SDK 11 alone.
- **A 2.4.0 on 2027.0-preview.** A stable package on a prerelease dependency (above).
- **Waiting for a 2026.1 patch that supports SDK 11's Roslyn.** Not in this repository's hands, and 2026.1.31
  (compiler 2026.1.21) still fails.

## Evidence

[KB-060](../knowledge/KB-060-metalama-2026-1-on-the-net-11-sdk.md), measured 2026-10-31 on macOS arm64 with the
published 2.3.1 packages:

| Web project | Metalama 2026.1 | Metalama 2027.0.5-preview |
|---|---|---|
| SDK 11 RC3 alone, `net11.0` | LAMA0625 (2026.1.31 too) | builds |
| SDK 11 RC3 alone, `net10.0` | LAMA0625 | builds |
| SDK 11 RC1 alone | LAMA0625 | builds |
| SDK 11 RC3 with SDK 10.0.403 beside it | builds, LAMA0617 twice | builds |
| SDK 10.0.403, `net10.0` | builds | builds |

A console project builds on 2026.1 in every case, so the failure is the web SDK's Razor compiler, which needs Roslyn
5.11 (RC1) or 5.12 (RC3) where Metalama 2026.1 bundles 5.9. With 2027, one project on `net10.0;net11.0` and no
`LangVersion` compiles `net10.0` at C# 14.0 and `net11.0` at 15.0, the value object is generated on both, and a union
under `#if NET11_0_OR_GREATER` or in a file removed from `net10.0` builds on both targets. This machine has a Metalama
license entry for 2027.0, so these runs say nothing about a build without one; decision 120's clean-container run of
2027.0.4-preview did, and the CI condition stays.

## Consequences

- The roadmap's Metalama item moves to 2.4.0. #173 (the move), #272 (a union over value objects in a woven project)
  and #223 (what the generators do with a union or a `closed` class) move from the 2.5.0 epic to the 2.4.0 epic:
  the release that makes a woven project compile those shapes defines what they do, since a value object over a union
  that compiles with no defined answer is the silent failure this repository refuses. 2.5.0 keeps what 2027.0 offers
  beyond that: AspectTesting on xunit.v3 (#220), the aspect-author analyzers (#221), type facets (#222) and the
  platform requirement (#224).
- Decision 120's rule 2 sentence "until 2.5.0 a union member reaches the consumers without the Generators package"
  ends at 2.4.0: from 2.4.0 a woven `net11.0` project compiles at C# 15, so #209 and #210 reach every consumer.
- #275 closes when 2.4.0 ships on 2027.0; #281 is the lane.
- Decision 120's status and index row name this record.
