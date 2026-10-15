---
verified: 2026-09-28
pins:
  - external Thinktecture.Runtime.Extensions 10.5.0
source: the Thinktecture.Runtime.Extensions repository at tag 10.5.0 and its nuspecs on api.nuget.org, read by a research agent
turned-on: the Thinktecture column of the value-object table and the rows of "Choose something else when" that name it in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-012. Thinktecture.Runtime.Extensions 10.5.0 declares value objects, smart enums and unions by attribute, for net8.0 and net9.0

- Declared by attribute on a partial type: `[ValueObject<T>]`, `[ComplexValueObject]` (several members),
  `[SmartEnum<T>]` and `[Union]`.
- Validation is a `ValidateFactoryArguments` partial method.
- EF Core through a package per EF Core version (8, 9, 10). JSON through System.Text.Json, Newtonsoft.Json and
  MessagePack packages.
- Targets `net8.0` and `net9.0`. License BSD-3-Clause.

Its OpenAPI and Native AOT facts, at 10.5.1, are [KB-013](KB-013-thinktecture-openapi-and-native-aot.md); its
smart enums, [KB-022](KB-022-thinktecture-smart-enums.md).
