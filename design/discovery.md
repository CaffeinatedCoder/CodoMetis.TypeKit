# Discovery is by interface

No namespace strings, assembly-name prefixes or type-name lists anywhere. A name-based check
passes in tests and breaks for the first consumer who renames something.

| Where | How |
|---|---|
| Analyzer, value-object rules | `CompilationStartAction` resolves `CodoMetis.TypeKit.ValueObjects.IValue`1` / `IValidatedValue`3` with `GetTypeByMetadataName` and compares `OriginalDefinition` with `SymbolEqualityComparer`. If `CodoMetis.TypeKit` is absent, nothing is registered |
| Analyzer, `[RequireCustomInitialization]` | The attribute symbol is resolved the same way |
| Analyzer, CMTK0002 | The compilation references an assembly whose name is exactly `CodoMetis.TypeKit.Generators`, ignoring case as assembly names do. That is the package's own id, which a consumer cannot rename, and a prefix or suffix match never counts. The assembly is not strong-named, so the name is the part of its identity to compare; the version must not be |
| EF Core | EF asks the type-mapping plugin per CLR type, and it answers for any type carrying `GeneratedValueObjectAttribute<TValueObject, T>` ([EF Core](ef-core.md)). There is no type scan and no assembly filter |
| OpenAPI | The transformer reads `GeneratedValueObjectAttribute<,>` on the schema's `JsonTypeInfo.Type`, its element type, or a parameter's type or model metadata ([OpenAPI](openapi.md)). There is no referenced-assembly walk |

Run-time code that holds only a `Type` reads the attribute rather than calling `GetInterfaces()`
([decision 17](adr/0017-run-time-discovery-reads-generatedvalueobjectattribute-tvalueobject.md)). Its type arguments are constrained to `IValueObject<TSelf, T>` and
`IValueObjectMaterializer<TSelf, T>`, so the interface still decides what a value object is; the
attribute is how a type says so after trimming, which removes an interface nothing uses and keeps
custom attributes.
