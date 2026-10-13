# Packages

| Package | Depends on | Metalama | Contents |
|---|---|---|---|
| `CodoMetis.TypeKit` | Analyzers (flows, [Analyzers](analyzers.md)) | **no** | `Option<T>`, `Result<T,TError>`, `Result<TError>` and their extensions, all carrying `NotWireTypeJsonConverterFactory` ([Option and Result](option-result.md)); the value-object contracts `IValueObject<,>`, `IPlainValueObject<,>`, `IValue<T>`, `IValidatedValue<,,>`, `IValueObjectMaterializer<,>` ([EF Core](ef-core.md)); the closed-set contracts `IClosedSet<>`, `IFault`, `IFaultSet<>` and `FaultKind`; `Fault<TFault>` and `Fault.Of`; `Patch<T>`, `PatchState` and `PatchJsonConverterFactory`; `Result.Catch`/`CatchAsync`; the text guards `AsText()`/`AsIdentifier()` (`StringTextExtensions`) and `TextFault`; the type-argument constraint attributes `[Abstract]`, `[Concrete]`, `[Sealed]`, `[Record]`; `RequireCustomInitializationAttribute`, `RequireParameterlessConstructorAttribute` and `ConstructorAccessibility`; `GuidValueExtensions` (`OrderId.New()` from a version 7 Guid); and what only generated code and the satellites use ([Discovery](discovery.md), [Aspects](aspects.md), [Native AOT](native-aot.md)): `GeneratedValueObjectAttribute<,>`, `GeneratedClosedSetAttribute<>`, `IValueObjectVisitor<>`, `IClosedSetVisitor<>`, `TranslatedAsWrappedValueAttribute`, `GeneratedFactories`, `GeneratedParsing`, `GeneratedFormatting`, `GeneratedJson`, `GeneratedJsonPlan<>`, `GeneratedClosedSets`, `IStoredJsonConverterSource` |
| `CodoMetis.TypeKit.Analyzers` | — | no | Roslyn analyzers and code fixes ([Analyzers](analyzers.md)). Nobody references it directly: it reaches every consumer through the base package |
| `CodoMetis.TypeKit.Generators` | TypeKit, Metalama.Framework 2026.1.x (flows) | yes | Internal aspects (the value-object family and `ClosedSetAspect`), internal `TransitiveProjectFabric`, `AspectOrder`. Future generated type families join this package too: one fabric, one aspect order |
| `CodoMetis.TypeKit.EntityFrameworkCore` | TypeKit, EF Core Relational | no | Generic converter, convention, `.Value` / `GetValue` / `ValueOrNull` translators, `UseTypeKit()` |
| `CodoMetis.TypeKit.AspNetCore` | TypeKit, Microsoft.AspNetCore.OpenApi | no | `AddTypeKit()`: one OpenAPI schema transformer ([OpenAPI](openapi.md)); `ToHttpResult()`, `AddFaultResponses`, `FaultProblem<TFault>`, `FaultResponseOptions`, `IFaultFeature` ([decision 38](adr/0038-result-becomes-response-explicit-tohttpresult-faults.md), [decision 49](adr/0049-fault-responses-client-can-act.md)) |
| `CodoMetis.TypeKit.Serilog` | TypeKit, Serilog 4.0.0 | no | `Destructure.WithTypeKit()`: one destructuring policy that logs every value object as its `ToString()` ([decision 41](adr/0041-sensitive-value-object-declares-tostring-serilog.md)) |
| `CodoMetis.TypeKit.Testing` | TypeKit | no | `ShouldBeSome()`/`ShouldBeNone()`, `ShouldBeSuccess()`/`ShouldBeError()`, and the `Patch` assertions: test assertions that hand back the content, with no test framework dependency ([decision 40](adr/0040-codometis-typekit-testing-holds-assertions-hand.md)) |

**The bare name is the cheapest package.** Someone who only wants `Option`/`Result` references
`CodoMetis.TypeKit` and never receives Metalama by accident. Taking Metalama on is always an
explicit, named choice (`.Generators`). The EF and ASP.NET packages depend on the base package,
not on `.Generators`. They work at run time against the interfaces, so a host that maps value
objects from a referenced domain assembly never needs Metalama itself.

**Namespaces:**

| Namespace | Holds |
|---|---|
| `CodoMetis.TypeKit` | `Option`, `Result`, `ResultState`, the markers, `NotWireTypeJsonConverterFactory`, `RequireCustomInitializationAttribute`, `GuidValueExtensions` (`New()` is call-site vocabulary, like `Option`) |
| `CodoMetis.TypeKit` (2.0.0) | `Fault`, `Patch`, `PatchState`, `PatchJsonConverterFactory`, `StringTextExtensions`, `TextFault`, the constraint attributes, `RequireParameterlessConstructorAttribute`, `ConstructorAccessibility` |
| `CodoMetis.TypeKit.ClosedSets` | `IClosedSet<>`, `IFault`, `IFaultSet<>`, `FaultKind`: what a user declares a closed set or a fault set with |
| `CodoMetis.TypeKit.ValueObjects` | Every contract a user declares or constrains on: `IValue`, `IValidatedValue`, `IValueObject`, `IPlainValueObject`, `IValueObjectMaterializer`, and `StoredJsonConverterFactory` |
| `CodoMetis.TypeKit.CompilerServices` | What only generated code and the satellites touch, all `[EditorBrowsable(Never)]`: `GeneratedValueObjectAttribute`, `IValueObjectVisitor`, `TranslatedAsWrappedValueAttribute`, `GeneratedFactories`, `GeneratedParsing`, `GeneratedFormatting`, `GeneratedJson`, `IStoredJsonConverterSource`. The name follows `System.Runtime.CompilerServices` |
| `CodoMetis.TypeKit.EntityFrameworkCore` | `ValueObjectConverter<,>`; the plugins, internal |
| `CodoMetis.TypeKit.AspNetCore` | the schema transformers, internal; `FaultProblem<TFault>`, `FaultResponseOptions`, `IFaultFeature` |
| `Microsoft.AspNetCore.Http` | `ToHttpResult()`, beside ASP.NET's own `Results` ([decision 22](adr/0022-registration-extensions-live-framework-s-namespaces.md)) |
| `CodoMetis.TypeKit.Serilog` | the destructuring policy, internal |
| `Serilog` | `WithTypeKit()`, beside Serilog's own configuration methods ([decision 22](adr/0022-registration-extensions-live-framework-s-namespaces.md)) |
| `CodoMetis.TypeKit.Testing` | the assertions and `TypeKitAssertionException` |
| `Microsoft.EntityFrameworkCore` | `UseTypeKit()`, beside the providers' `UseNpgsql` ([decision 22](adr/0022-registration-extensions-live-framework-s-namespaces.md)) |
| `Microsoft.Extensions.DependencyInjection` | `AddEntityFrameworkTypeKit()`, `OpenApiOptions.AddTypeKit()` and `AddFaultResponses()`, beside `AddEntityFrameworkNpgsql` and `AddOpenApi` ([decision 22](adr/0022-registration-extensions-live-framework-s-namespaces.md)) |

The internal aspects live in `CodoMetis.TypeKit.Generators`. Users never type that namespace.
