---
verified: 2026-10-29
pins:
  - external C# 15 and the .NET 11 SDK, release candidate 2
source: Microsoft Learn, "What's new in C# 15" (updated 2026-10-23, docs commit 433547d), and the .NET blog's "Explore union types in C# 15" (2026-04-02), read 2026-10-29
turned-on: the C# union types row of "Choose something else when" in docs/comparison.md
test: none, a language feature's release status, read from Microsoft's documentation
---

# KB-045. C# 15's union types and closed hierarchies need no preview flag in the .NET 11 SDK, which is still prerelease

- Union types (`public union Pet(Cat, Dog, Bird);`) and closed hierarchies (`closed` on a class) are C# 15 features,
  with exhaustive `switch` checking.
- C# 15 is the default language version for `net11.0` in the .NET 11 SDK, and except for memory safety its features
  need no `<LangVersion>preview</LangVersion>`.
- The .NET 11 SDK is still prerelease. Microsoft Learn's .NET 11 overview (updated 2026-10-21, docs commit 015d8a2) was
  last updated for release candidate 2, and expects general availability in November 2026.
- Unions first appeared as a preview feature in .NET 11 Preview 2 (the .NET blog, 2026-04-02).
