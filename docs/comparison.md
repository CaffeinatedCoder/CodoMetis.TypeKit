# How it compares

TypeKit isn't the only good choice. Other libraries cover value objects, `Option` and `Result`, and
smart enums, and for some needs one of them fits better. This page helps you decide, first by need
and then feature by feature.

The [README](../README.md#how-it-compares) gives the reasons to choose TypeKit. This page is the
other side, with the detail.

Every fact here was read from each library's source, at the release named in the table:

- **28 September 2026** for value objects, `Option` and `Result`, including the next major release
  where one is in preview.
- **13 October 2026** for closed sets.
- **14 October 2026**, again, for OpenAPI and Native AOT.

What was read for each library, and when, is kept in the repository's
[verified facts](../design/knowledge/README.md). Each table below lists the entries it rests on.

If something here is wrong or has changed, please open an issue.

## Choose something else when

| If you need | Consider | Because |
|---|---|---|
| .NET Framework, .NET Standard or a .NET before 10 | Vogen for value objects; CSharpFunctionalExtensions, ErrorOr or FluentResults for results | TypeKit targets .NET 10 only. These ship for .NET Standard 2.0 |
| Newtonsoft.Json, Dapper, LINQ to DB, MongoDB, Orleans, ServiceStack or protobuf-net for value objects | Vogen | It ships an opt-in integration for each. TypeKit covers System.Text.Json, EF Core and ASP.NET Core |
| A plain Roslyn source generator in the build rather than Metalama | Vogen or Thinktecture.Runtime.Extensions | TypeKit's generators run on Metalama (open source, no license key) in the project that declares value objects |
| Value objects of several members, or discriminated unions | Thinktecture.Runtime.Extensions | It has `[ComplexValueObject]` and `[Union]`. A TypeKit value object wraps exactly one value. Its closed sets are the smart enums |
| Several errors per failure, of typed kinds | ErrorOr | An `ErrorOr<T>` carries a list of errors. A TypeKit `Result` carries one `TError`, which can be a list of your own |
| A cause nested under an error, metadata per occurrence, or success messages | FluentResults | Its results carry reasons, errors and successes. An error can name what caused it, and both take metadata. A TypeKit `Result` carries one `TError`. A `Fault<TFault>` is one reason from a fixed set, with a sentence about the occurrence and the rule that rejected the value. A closed-set fault carries a kind and constant data. `Catch` carries an exception as the whole error. Nothing attaches a cause to a fault, and a success carries only its value |
| A functional-programming framework: `Either`, `Fin`, effects, immutable collections | LanguageExt (4.4.9; 5.0 in beta) | TypeKit's `Option` and `Result` are small on purpose, with no effect or validation types |
| Unions of arbitrary types | OneOf | `OneOf<T0, T1, ...>` is general-purpose. TypeKit has only `Option` and `Result` |
| A `Result` on the wire | CSharpFunctionalExtensions | It ships opt-in System.Text.Json converters for its `Result`. TypeKit rejects serializing `Option` and `Result`, by design |
| A smart enum whose JSON or column is a number, or whose wire name differs from its C# identifier | Ardalis.SmartEnum, Intellenum or Thinktecture.Runtime.Extensions | A TypeKit closed set is its member's name everywhere, and the name is the field's |
| Only your own closed set of outcomes, on .NET 11 | C#'s union types | They arrive with .NET 11 |
| Years of production use behind the library | Any of the above | TypeKit is new: 1.0.0 shipped in September 2026 |

Sources: FluentResults [KB-018](../design/knowledge/KB-018-fluentresults.md), OneOf [KB-019](../design/knowledge/KB-019-oneof.md), and the entries under each table below.

## Value objects in detail

| | TypeKit 2.1.0 | Vogen 8.0.7 (the 9.0.0-beta.2 tag the same; not on NuGet) | Thinktecture.Runtime.Extensions 10.5.0 (OpenAPI and AOT: 10.5.1) | StronglyTypedId 1.0.0-beta08 |
|---|---|---|---|---|
| Declared by | Implementing `IValue<T>` or `IValidatedValue<...>` on a `partial record` | `[ValueObject<T>]` on a partial type | `[ValueObject<T>]`, `[ComplexValueObject]` or `[SmartEnum<T>]` on a partial type | `[StronglyTypedId]` on a partial struct |
| Validation | `Create` returns a `Result`, and every generated way of creating a value runs it | `Validate` returns a `Validation`. `From` throws | A `ValidateFactoryArguments` partial method | None |
| EF Core | `UseTypeKit()` maps every value object, nothing per type. `.Value` in a query translates to the column | A generated converter per value object, which you register. Comparing with a primitive in a query is an error (VOG034) | Packages per EF Core version (8, 9, 10) | Through a template of your own |
| OpenAPI | Microsoft.AspNetCore.OpenApi: the schema ASP.NET publishes for the underlying type, under the host's JSON options | Opt-in, off by default: a generated schema transformer, with type and format from a built-in table. Swashbuckle support | Swashbuckle filters only, with type and format from Swashbuckle's table | None built in |
| A JSON converter the host registers, such as a decimal written as a string | Described as it writes, wherever it applies. Either the converter says what it writes (`IDescribedJsonConverter`), or the options say it for that converter (`JsonSchemaDescriptions`). A document that would describe different JSON from what the controllers write is rejected | Not reflected: the table's type and format stay | Not reflected for type and format. A smart enum's listed values are written with the host's options | None built in |
| Native AOT | Declared (`IsAotCompatible`). On every CI build, a consumer with value objects, JSON, OpenAPI and EF Core is published with Native AOT | Not declared. Its docs say the Swashbuckle schema filter needs reflection that Native AOT lacks | Not declared | Not declared |
| JSON | System.Text.Json, byte for byte the underlying type's JSON | System.Text.Json by default. Newtonsoft.Json and more, opt-in | System.Text.Json, Newtonsoft.Json, MessagePack | System.Text.Json (more through templates) |
| Targets | `net10.0` | `netstandard2.0`, .NET Framework projects included | `net8.0`, `net9.0`. The ASP.NET Core and Swashbuckle packages `net8.0` only | `netstandard2.0` |
| Generated by | Metalama, in the declaring project | A Roslyn source generator | Roslyn source generators | A Roslyn source generator |
| License | MIT | Apache-2.0 | BSD-3-Clause | MIT |

Sources: Vogen [KB-010](../design/knowledge/KB-010-vogen-declaring-validating-storing.md) and [KB-011](../design/knowledge/KB-011-vogen-openapi-and-native-aot.md), Thinktecture.Runtime.Extensions [KB-012](../design/knowledge/KB-012-thinktecture-value-objects.md) and [KB-013](../design/knowledge/KB-013-thinktecture-openapi-and-native-aot.md),
StronglyTypedId [KB-014](../design/knowledge/KB-014-stronglytypedid.md).

**A gap none of them fills.** ASP.NET Core and System.Text.Json's schema exporter can't see through
a JSON converter you write yourself ([KB-023](../design/knowledge/KB-023-a-converter-cannot-describe-its-schema.md)). They write it as the empty schema, or with only the `format` its
CLR type maps to. The serializer has a hook for a converter to describe itself, but it is internal.
The proposal to make it public ([dotnet/runtime#105769](https://github.com/dotnet/runtime/issues/105769))
is open, without a release. None of the libraries above fills that gap, so with any of them, a
converter you register means describing it in the document by hand, type by type.

## Option and Result in detail

| | TypeKit 2.1.0 | LanguageExt 4.4.9 (5.0.0-beta-77) | CSharpFunctionalExtensions 3.7.0 | ErrorOr 2.1.1 |
|---|---|---|---|---|
| Reaching the value | `Match`, `TryGetValue` and the combinators. No `.Value` | `Match`, `IfNone` and the like. No public `.Value` on `Option` | `.Value`, which throws on a failure | `.Value`, which returns `default` on an error |
| A `default` instance | `Option`: `None`. `Result`: throws when used, and fails the build when written in code (CMTK0001) | `Option`: `None` | `Maybe`: none. `Result<T>`: a success holding `default(T)` | A success holding `default(T)` |
| An ignored result | Reported (CMTK0003) | No analyzer | No analyzer of its own | No analyzer |
| System.Text.Json | Rejected in both directions, rather than written as `{}` | No converter of its own | Opt-in converters | No converter for the type |
| Targets | `net10.0` | `netstandard2.0` (5.0 beta: `net10.0`) | `netstandard2.0`, `net6.0`, `net8.0` | `netstandard2.0`, `net8.0`, `net10.0` |

Sources: LanguageExt [KB-015](../design/knowledge/KB-015-languageext-option.md), CSharpFunctionalExtensions [KB-016](../design/knowledge/KB-016-csharpfunctionalextensions.md), ErrorOr [KB-017](../design/knowledge/KB-017-erroror.md).

## Closed sets in detail

| | TypeKit 2.1.0 | Ardalis.SmartEnum 8.2.0 | Intellenum 2.0.1 | Thinktecture.Runtime.Extensions 10.5.1 |
|---|---|---|---|---|
| Declared by | Implementing `IClosedSet<TSelf>` on a `sealed partial class`, one `public static readonly` field per member | Deriving from `SmartEnum<TEnum>`, each member `new("Name", value)` | `[Intellenum]` on a partial class. Members as fields, `[Member]` attributes or a static constructor | `[SmartEnum<TKey>]` on a partial class, each member `new("KEY")` |
| Data on a member | Properties, set through the private constructor | The subclass's own fields | One underlying value. Richer data through a custom underlying type | Declared properties, which become constructor parameters |
| Deciding by member | `Match`, one required delegate per member, every argument named (CMTK0012) | `When(...).Then(...)`. Nothing requires every member | A `switch` over `.Value` with generated constants. Nothing requires every member | Generated `Switch` and `Map`, every member a required argument |
| Parsing by name | `Parse` and `TryParse`, ordinal. `ignoreCase: true` on request, and two members differing only in case fail the build (CMTK1021) | `FromName` and `TryFromName`, ordinal. `ignoreCase` on request. A case clash throws at the first such lookup | `FromName` and `TryFromName`, ordinal. `ignoreCase` on request, and the first declared member answers | `Get` and `TryGet`, ignoring case by default. A case clash throws at the first lookup |
| JSON | System.Text.Json, the name, nothing registered | A separate package per serializer, a converter attribute per type. The name or the value | Generated into your assembly. The value, as an `enum` writes it | A separate package per serializer. The key |
| EF Core | `UseTypeKit()`, a text column of the name, nothing per set | A separate package, a convention or per model. The value | An opt-in generated converter, registered per property. The value | A separate package per EF version, `UseThinktectureValueConverters()`. The key |
| OpenAPI | `AddTypeKit()`, a string enum of the names | None built in | None built in | A Swashbuckle package |
| Analyzers | CMTK1013 to 1021 fail the build for a declaration that can't be generated, CMTK0012 flags an argument by position, CMTK0013 a member written after it was named | None | INTELLENUM001 to 030 | TTRESG rules, with refactorings |
| A wire name apart from the identifier | No: the name is the field's | Yes, the constructor's name | Yes, `new("STD", 1)` | Yes, the key |
| Targets | `net10.0` | `netstandard2.0`, `net6.0` to `net8.0` | `netstandard2.0` | `net8.0`, `net9.0` |
| Generated by | Metalama, in the declaring project | Nothing: a run-time base class | A Roslyn source generator | Roslyn source generators |
| License | MIT | MIT | Apache-2.0 | BSD-3-Clause |

Sources: Ardalis.SmartEnum [KB-020](../design/knowledge/KB-020-ardalis-smartenum.md), Intellenum [KB-021](../design/knowledge/KB-021-intellenum.md), Thinktecture.Runtime.Extensions [KB-022](../design/knowledge/KB-022-thinktecture-smart-enums.md).
