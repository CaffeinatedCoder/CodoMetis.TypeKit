# Verified facts

What the frameworks, platforms and tools TypeKit builds on do, as this repository verified it: one fact per
file, each pinned to the versions it was checked on, dated, with the primary source it was checked against and
what here turned on it ([decision 93](../adr/0093-verified-facts-are-pinned-dated-and-executable-where-they-can-be.md)).
A fact a test can observe has a characterization test in `test/CodoMetis.TypeKit.Facts`, named by its id, which
fails when a dependency changes the behaviour.

Look here before researching a framework question: `docs-query entry KB-NNN`, or `docs-query find <words>`. A fact
whose pin no longer matches `Directory.Packages.props` or `global.json` is stale and settles nothing until it is
re-verified; `docs-query check` lists the stale ones.

**Adding one.** Only what something here turned on (a decision, a fix, a probe's verdict), pinned and
falsifiable, checked against a primary source. A competitor's feature is a fact too, pinned
`external {name} {version}`: no check compares it, so it is as fresh as its date. Take the next number, copy an
entry's frontmatter, write the test first where a test can observe it, and add a row below.

| Id | Fact | Pins | Test |
|---|---|---|---|
| [KB-001](KB-001-openapi-2-nullable-body-drops-pattern.md) | Under OpenAPI 2.0, ASP.NET writes a nullable request body as `allOf: [{x-nullable: true}]` and drops its `pattern` | OpenApi 10.0.12, SDK 10.0.4xx | `KB001` |
| [KB-002](KB-002-api-explorer-lists-a-controller-only-when-visible.md) | An MVC controller is in the OpenAPI document only when ASP.NET's API explorer sees it | OpenApi 10.0.12, SDK 10.0.4xx | `KB002` |
| [KB-003](KB-003-typedescriptor-described-before-registered.md) | A type `TypeDescriptor.GetConverter` described first can no longer be registered | SDK 10.0.4xx | `KB003` |
| [KB-004](KB-004-windows-refuses-a-replace-over-a-held-file.md) | On Windows, a rename that replaces a file someone holds open is refused, but moving it aside is allowed | SDK 10.0.4xx | `KB004` |
| [KB-005](KB-005-launch-costs-of-the-tool-forms.md) | `dotnet <built dll>` starts in about 0.02 s, `dotnet run --project` in 0.7 to 0.9 s | SDK 10.0.4xx | prose |
| [KB-006](KB-006-nested-serializer-call-restarts-reference-ids.md) | Under `ReferenceHandler.Preserve`, a nested `JsonSerializer` call numbers its references from 1 again | SDK 10.0.4xx | `KB006` |
| [KB-007](KB-007-a-resolver-s-contract-wins-over-the-options-converters.md) | A resolver of one's own decides a type's contract over a converter in the options, and `T?` follows `T` | SDK 10.0.4xx | `KB007` |
| [KB-008](KB-008-the-nullable-adapter-is-the-serializer-s-own.md) | A `Nullable<T>` contract's converter is System.Text.Json's own adapter, and nothing public names what it wraps | SDK 10.0.4xx | `KB008` |
| [KB-009](KB-009-a-modifier-s-converter-wins-over-the-attribute.md) | A resolver modifier's `CustomConverter` writes a property over its `[JsonConverter]` | SDK 10.0.4xx | `KB009` |
| [KB-010](KB-010-vogen-declaring-validating-storing.md) | Vogen 8.0.7 declares a value object by attribute, validates through `Validate`, and targets .NET Standard 2.0 | Vogen 8.0.7, 9.0.0-beta.2 | prose |
| [KB-011](KB-011-vogen-openapi-and-native-aot.md) | Vogen 8.0.7 generates OpenAPI schemas only on request, from a built-in type table, and does not declare Native AOT compatibility | Vogen 8.0.7, 9.0.0-beta.2 | prose |
| [KB-012](KB-012-thinktecture-value-objects.md) | Thinktecture.Runtime.Extensions 10.5.0 declares value objects, smart enums and unions by attribute, for net8.0 and net9.0 | Thinktecture 10.5.0 | prose |
| [KB-013](KB-013-thinktecture-openapi-and-native-aot.md) | Thinktecture.Runtime.Extensions 10.5.1 documents OpenAPI through Swashbuckle alone, and does not declare Native AOT compatibility | Thinktecture 10.5.1 | prose |
| [KB-014](KB-014-stronglytypedid.md) | StronglyTypedId 1.0.0-beta08 is an attribute on a partial struct, with no validation, analyzers or OpenAPI support | StronglyTypedId 1.0.0-beta08 | prose |
| [KB-015](KB-015-languageext-option.md) | LanguageExt has no public `.Value` on `Option`, and its `default` is `None`, in 4.4.9 and the 5.0 beta | LanguageExt 4.4.9, 5.0.0-beta-77 | prose |
| [KB-016](KB-016-csharpfunctionalextensions.md) | CSharpFunctionalExtensions 3.7.0 throws from `.Value` on a failure, and its `default(Result<T>)` is a success | CSharpFunctionalExtensions 3.7.0 | prose |
| [KB-017](KB-017-erroror.md) | ErrorOr 2.1.1 returns `default` from `.Value` on an error, and its `default` instance is a success | ErrorOr 2.1.1 | prose |
| [KB-018](KB-018-fluentresults.md) | FluentResults 4.0.0 results are classes that carry reasons, errors and successes, and an error can name its cause | FluentResults 4.0.0 | prose |
| [KB-019](KB-019-oneof.md) | OneOf 3.0.271 is a readonly struct whose `default` is its first case | OneOf 3.0.271 | prose |
| [KB-020](KB-020-ardalis-smartenum.md) | Ardalis.SmartEnum 8.2.0 is a run-time base class, finds its members by reflection, and requires no member in `When` | Ardalis.SmartEnum 8.2.0 | prose |
| [KB-021](KB-021-intellenum.md) | Intellenum 2.0.1 has one underlying value per member, and its case-insensitive lookup answers the first declared | Intellenum 2.0.1 | prose |
| [KB-022](KB-022-thinktecture-smart-enums.md) | Thinktecture.Runtime.Extensions 10.5.1 smart enums have a key apart from the identifier, a `Switch` over every member, and look keys up ignoring case | Thinktecture 10.5.1 | prose |
| [KB-023](KB-023-a-converter-cannot-describe-its-schema.md) | A JSON converter of one's own cannot say what it writes: the schema exporter writes the empty schema, ASP.NET only a format | OpenApi 10.0.12, SDK 10.0.4xx | `KB023` |
| [KB-024](KB-024-no-library-constrains-type-arguments-by-kind.md) | No .NET library constrains a type argument to be abstract, sealed, a record or concrete, or two arguments to differ | AdvancedGenericTypeConstraints 0.4.1, RossLean.GenericsAnalyzer 1.4.0 | prose |
| [KB-025](KB-025-the-coverage-collector-rewrites-assemblies-on-disk.md) | The code coverage collector instruments the assemblies on disk, so test hosts started together from one output directory corrupt each other, and its in-memory mode records no coverage on macOS arm64 | Microsoft.Testing.Extensions.CodeCoverage 18.12.0, SDK 10.0.4xx | prose |
| [KB-026](KB-026-openapi-2-nullable-component-property-loses-its-reference.md) | Under OpenAPI 2.0, ASP.NET writes a nullable property of a component as `allOf: [{x-nullable: true}]`, without the reference | OpenApi 10.0.12, SDK 10.0.4xx | `KB026` |
| [KB-027](KB-027-an-enum-component-is-its-first-use.md) | The component ASP.NET makes for an enum written by name is the schema of the first use it meets, null included | OpenApi 10.0.12, SDK 10.0.4xx | `KB027` |
