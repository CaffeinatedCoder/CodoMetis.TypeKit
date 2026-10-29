---
verified: 2026-10-29
pins:
  - external SuperStrong.Types 1.0.0
source: the SuperStrong.Types repository at tag v1.0.0 (commit 1d059e0) and its nuspecs on api.nuget.org, read by a research agent and spot-checked (2026-10-29)
turned-on: the SuperStrong.Types column of the value-object table in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-043. SuperStrong.Types 1.0.0 is an attribute on a partial class, validated everywhere it creates a value, with no Native AOT declaration

- Released 2026-07-21 (1.0.0-alpha.1 on 2026-06-14). Packages: `SuperStrong.Types` (the generator, analyzers and code
  fixes ship inside it), `.EntityFrameworkCore`, `.AspNetCore.OpenApi`, `.HotChocolate` and `.NewtonsoftJson`, all 1.0.0.
- Declared by `[StrongType<TPrimitive>]` (or `[StrongType<TPrimitive, TTemplate>]` for a shared definition) on a
  `partial class`. The attribute targets classes only, and a record, a base type other than `object` or an abstract
  class fails the build (SST003, SST004, SST006).
- Validation is a `Define()` method returning a definition built from validators (`IsPositive()`, `HasMaxLength()`,
  `MatchesRegex()` and others). `From` throws `StrongTypeValidationException`, `TryFrom` returns `false`. Parsing, the
  System.Text.Json and Newtonsoft.Json converters, the type converter, the EF Core value converter (reads included) and
  Hot Chocolate's input coercion all go through `From` or `TryFrom`.
- EF Core: `UseStrongTypes()` adds a type-mapping plugin for every strong type, nothing per type. A query interceptor
  translates `AsPrimitive()` (filter, projection, ordering, aggregates).
- OpenAPI: Microsoft.AspNetCore.OpenApi only, opt-in, `AddStrongTypes(representation)`: the underlying type's schema
  inline, or a named component with the underlying type's `type` and `format`. Both come from
  `GetOrCreateSchemaAsync` for the underlying type; nothing describes a converter the host registers.
- System.Text.Json is built in (a generated `[JsonConverter]` that writes the underlying value under the host's
  options); Newtonsoft.Json is opt-in, registered on the settings.
- One analyzer: SST005 (Warning), `Equals(T)` and `GetHashCode()` declared together. None reports `default` or `new()`.
- No `IsAotCompatible` or `IsTrimmable` in any project file, and no Native AOT step in CI. No `Option`, `Result` or smart
  enum. Targets `net10.0`; a Roslyn incremental source generator; license MIT.
