---
verified: 2026-10-14
pins:
  - external StronglyTypedId 1.0.0-beta08
source: the StronglyTypedId repository at tag 1.0.0-beta08 and its nuspec on api.nuget.org, read by a research agent (2026-09-28, OpenAPI and Native AOT again 2026-10-14)
turned-on: the StronglyTypedId column of the value-object table in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-014. StronglyTypedId 1.0.0-beta08 is an attribute on a partial struct, with no validation, analyzers or OpenAPI support

- Declared by `[StronglyTypedId]` on a partial struct.
- No validation and no analyzers. Integrations (EF Core, JSON beyond System.Text.Json) come through templates.
- No OpenAPI code. The package does not declare `IsAotCompatible`.
- Targets `netstandard2.0`. License MIT. Released 2024-04, with nothing since.
