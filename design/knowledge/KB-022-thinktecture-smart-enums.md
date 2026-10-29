---
verified: 2026-10-29
pins:
  - external Thinktecture.Runtime.Extensions 10.6.0
source: the Thinktecture.Runtime.Extensions repository at tag 10.6.0 (its generators, attributes and README), its release notes from 10.5.1 to 10.6.0, and its nuspecs on api.nuget.org, read by a research agent; first read at 10.5.1 on 2026-10-13
turned-on: the closed-set table and the smart-enum row of "Choose something else when" in docs/comparison.md, and decision 51 (a closed set parses ignoring case only on request)
test: none, a competitor's feature, read from its source and package
---

# KB-022. Thinktecture.Runtime.Extensions 10.6.0 smart enums have a key apart from the identifier, a `Switch` over every member, and look keys up ignoring case

- `[SmartEnum<TKey>]` on a partial class, each member `new("KEY")`; declared properties become constructor
  parameters. The key can be an enum type since 10.5.0.
- Generated `Switch` and `Map` take every member as a required argument by default.
  `SwitchMapMethodsGeneration.DefaultWithPartialOverloads` also generates `SwitchPartially` and `MapPartially`,
  where a member may be left out for a default.
- `Get` and `TryGet`; string keys compare `OrdinalIgnoreCase` by default (ordinal on request through
  `[KeyMemberEqualityComparer<...>]`). A case clash throws `ArgumentException` on the first lookup.
- JSON and MessagePack in separate packages, the wire is the key. EF Core through a package per EF Core version
  with `UseThinktectureValueConverters()`, the column is the key. OpenAPI through a Swashbuckle package.
- TTRESG analyzers, with refactorings. The key can differ from the identifier.
- 10.6.0 released 2026-10-19, with no generator, analyzer or attribute change since 10.5.1. Targets `net8.0` and
  `net9.0`. License BSD-3-Clause.
