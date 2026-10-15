---
verified: 2026-10-13
pins:
  - external Thinktecture.Runtime.Extensions 10.5.1
source: the Thinktecture.Runtime.Extensions repository at tag 10.5.1 and its nuspecs on api.nuget.org, read by a research agent
turned-on: the closed-set table and the smart-enum row of "Choose something else when" in docs/comparison.md, and decision 51 (a closed set parses ignoring case only on request)
test: none, a competitor's feature, read from its source and package
---

# KB-022. Thinktecture.Runtime.Extensions 10.5.1 smart enums have a key apart from the identifier, a `Switch` over every member, and look keys up ignoring case

- `[SmartEnum<TKey>]` on a partial class, each member `new("KEY")`; declared properties become constructor
  parameters.
- Generated `Switch` and `Map` take every member as a required argument.
- `Get` and `TryGet`; string keys compare `OrdinalIgnoreCase` by default (ordinal on request through
  `[KeyMemberEqualityComparer<...>]`). A case clash throws `ArgumentException` on the first lookup.
- JSON and MessagePack in separate packages, the wire is the key. EF Core through a package per EF Core version
  with `UseThinktectureValueConverters()`, the column is the key. OpenAPI through a Swashbuckle package.
- TTRESG analyzers, with refactorings. The key can differ from the identifier.
- Released 2026-10-08. Targets `net8.0` and `net9.0`. License BSD-3-Clause.
