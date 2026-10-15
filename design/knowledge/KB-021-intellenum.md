---
verified: 2026-10-13
pins:
  - external Intellenum 2.0.1
source: the Intellenum repository at tag 2.0.1 and its nuspec on api.nuget.org, read by a research agent (OpenAPI again 2026-10-14)
turned-on: the closed-set table and the smart-enum row of "Choose something else when" in docs/comparison.md, and decision 51 (a closed set parses ignoring case only on request)
test: none, a competitor's feature, read from its source and package
---

# KB-021. Intellenum 2.0.1 generates a closed set with one underlying value per member, and its case-insensitive lookup answers the first declared

- `[Intellenum<T>]` on a partial class; members as fields (`new("STD", 1)`), `[Member]` attributes or a static
  constructor, through a Roslyn source generator with analyzers INTELLENUM001 to 030.
- One underlying value per member; richer data through a custom underlying type.
- No `Switch` or `Map`: a `switch` over `.Value` with generated constants, which nothing holds to every member.
- `FromName(name, ignoreCase)` compares with `CurrentCultureIgnoreCase` and answers the first declared member. No
  analyzer for names differing only in case (INTELLENUM030 is duplicate values).
- JSON generated into the consuming assembly; the wire is the value, as an `enum` writes it. EF Core: an opt-in
  generated converter, registered per property; the column is the value.
- No OpenAPI code. Its documentation pages on JSON, Swagger, EF Core and the analyzers are copies of Vogen's and
  describe options its source lacks.
- Released 2025-09-30. Targets `netstandard2.0`. License Apache-2.0.
