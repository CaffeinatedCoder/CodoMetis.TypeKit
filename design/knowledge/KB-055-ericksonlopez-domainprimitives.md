---
verified: 2026-10-30
pins:
  - external EricksonLopez.DomainPrimitives 3.0.0
source: the dotnet-domain-primitives repository at tag v3.0.0 (commit 1587616; main at f936152 differs only in benchmark baselines), its generated-code snapshots and workflows, its GitHub Actions run history and its registration on api.nuget.org, read 2026-10-30. Read from the source; nothing was built or run
turned-on: the EricksonLopez.DomainPrimitives column of the value-object table and its rows in "Choose something else when", in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-055. EricksonLopez.DomainPrimitives 3.0.0 is an attribute on a partial record struct, with a catalogue of ready-made types, re-validated on every read

- Released 2026-09-20; 1.0.0 on 2026-08-11 and 2.0.0 on 2026-08-25. Fourteen packages in lockstep: the core (with
  the generators and analyzers), `.Abstractions`, `.Generators`, `.Analyzers`, `.AspNetCore`, `.EFCore`, `.Dapper`,
  `.OpenApi`, `.NewtonsoftJson`, `.Testing` and four `*.SourceGenerators` packages. Targets `net8.0`, `net9.0` and
  `net10.0` (`.Abstractions` also `netstandard2.0`); Roslyn incremental source generators; license MIT.
- **Declared by** an attribute on a `readonly partial record struct`: `[StrongId<T>]` (`Guid`, `long`, `int`,
  `string`), `[StringPrimitive]`, `[NumericPrimitive<T>]`, `[DatePrimitive]`, `[SmartEnum<T>]` (`int` or `string`)
  or `[ValueObject]` for a value object of several properties. Validation is declarative where it can be:
  normalization attributes (`[Trim]`, `[UpperCase]` and others), constraint attributes (`[Length]`, `[Regex]`,
  `[PrimitiveRange]` and others), and 39 ready-made types such as `[Email]`, `[IBAN]`, `[ISBN]`, `[VIN]`, `[Money]`
  and `[Latitude]`. A `[ValueObject]` validates in a `static partial void Validate(ref T, ref PrimitiveError)`.
  Numeric primitives get the arithmetic operators.
- **Creating a value.** `Create` throws `DomainPrimitiveValidationException`; `TryCreate(value, out result, out
  PrimitiveError)` returns `false`. `PrimitiveError` is a string code and a message. No `Option` or `Result` in the
  packages: the README bridges `TryCreate` to the author's separate EricksonLopez.Result by hand.
- **`default`.** A generated `IsDefault`, and `.Value` throws on a default instance; `ToString()` returns `""`, and
  the generated `Empty` property returns `default`. DP0007 (Warning) reports only the written `default`,
  `default(T)` and `new()` (`PrimitiveUsageAnalyzer`), not `FirstOrDefault` and the like.
- **A `[ValueObject]` can skip `Validate`.** A positional declaration, as in the README's `Address` example, keeps
  its public primary constructor, and a `with` expression copies the private initialized flag without validating.
  Its JSON reader returns `default` for `null` and lets `Create`'s `DomainPrimitiveValidationException` escape.
- **JSON.** System.Text.Json, built in: a generated converter per type that dispatches on the wrapped type's name
  (`GeneratorHelpers.cs`). It writes numbers with `WriteNumberValue`, whatever `NumberHandling` says; overrides
  neither `ReadAsPropertyName` nor `WriteAsPropertyName`, so a value object is no dictionary key; writes a default
  instance as `null`, which its reader refuses; and writes a `[ValueObject]`'s properties under fixed camelCase
  names, whatever the naming policy. Newtonsoft.Json is an opt-in package.
- **Messages quote the input.** The generated `Parse` throws `FormatException($"The value '{s}' is not valid ...")`,
  and the Dapper handler quotes the database value.
- **EF Core.** `ConfigureDomainPrimitives()` on the `ModelConfigurationBuilder`, a generated `ValueConverter` per
  type, found by syntax in the compilation the generator runs in, by attribute namespace prefix and a table of
  attribute names. Reads go through `Create` (`FromValue` for a smart enum), so a stored value a later rule rejects
  throws. No translation of `.Value` in a query; `[ValueObject]` is not mapped.
- **Dapper.** Generated type handlers, `RegisterAll()`, found across referenced assemblies, with a deny-list of
  third-party assembly name prefixes and a fallback to a struct named `*Id` (ADR-044).
- **OpenAPI.** Swashbuckle only (`Swashbuckle.AspNetCore.SwaggerGen` 6.6.2): generated schema filters, with type and
  format from a table keyed by attribute name (`[Money]`, a `decimal`, is `number`/`double`). Nothing for
  Microsoft.AspNetCore.OpenApi, and nothing that describes a converter the host registers.
- **Native AOT.** Declared (`IsAotCompatible`) on the core, `.Abstractions` and `.AspNetCore` only. The generated
  code of a `[ValueObject]` calls the reflection-based `JsonSerializer.Deserialize<T>(string)` in `TryParse` and its
  type converter. The AOT smoke-test project, in CI since 1.0.0, sets `NoWarn` for IL2026 and IL3050, and it creates
  values without serializing any. The open release PR for 4.0.0 (#35, release-please, 2026-09-21) keeps both: its
  only change to the smoke test is the pin of the artifact-upload action.
- **Analyzers.** DP0001 to DP0018, several with code fixes. They recognize a primitive by an interface's simple
  name (`IDomainPrimitive`, `IStrongId`) in any namespace. A smart enum's `Match` takes one delegate per member, and
  nothing requires naming the arguments.
- **Quality claims.** The README states a 100% mutation score for all fourteen packages, generators included; its
  ADR-032 excludes the generators from mutation testing, and the weekly Stryker workflow failed on 2026-10-19 and
  2026-10-26.
