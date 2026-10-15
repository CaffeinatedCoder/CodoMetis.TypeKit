---
verified: 2026-10-13
pins:
  - external Ardalis.SmartEnum 8.2.0
  - external Ardalis.SmartEnum.SystemTextJson 8.1.0
source: the SmartEnum repository at tag 8.2.0 and the nuspecs of Ardalis.SmartEnum 8.2.0 and its satellite packages on api.nuget.org, read by a research agent
turned-on: the closed-set table and the smart-enum row of "Choose something else when" in docs/comparison.md, and decision 51 (a closed set parses ignoring case only on request)
test: none, a competitor's feature, read from its source and package
---

# KB-020. Ardalis.SmartEnum 8.2.0 is a run-time base class, finds its members by reflection, and requires no member in `When`

- A smart enum derives from `SmartEnum<TEnum, TValue>`; each member is `new(string name, TValue value)`. Members
  are found by reflection at run time: no generator, no analyzers, no OpenAPI.
- `When(...).Then(...)` does not require every member.
- `FromName(name, ignoreCase = false)` and `TryFromName` are ordinal unless asked. The ignore-case lookup builds a
  dictionary with `ToDictionary`, so two names differing only in case throw on the first such call.
- JSON in separate packages (`Ardalis.SmartEnum.SystemTextJson` 8.1.0, `.JsonNet`, `.Utf8Json`, `.MessagePack`),
  a converter attribute per type, writing the name or the value.
- EF Core in `Ardalis.SmartEnum.EFCore`, by convention or per model; the column is the value.
- The name is a free string, so the wire name can differ from the C# identifier.
- Released 2024-11-19. Targets `netstandard2.0`, `net6.0`, `net7.0` and `net8.0`. License MIT.
