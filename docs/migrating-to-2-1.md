# Migrating from 2.0 to 2.1

This page walks you through moving a project from 2.0 to 2.1. In most projects you only raise the
version. A few things build or run differently, and most of those change the OpenAPI document rather
than your code. The [changelog](../CHANGELOG.md) lists everything 2.1 adds.

Your platform stays the same. 2.1 targets `net10.0` and Metalama 2026.1, as 2.0 does, and its
dependencies keep 2.0's versions.

## Update the packages

All the packages share one version and move together. Raise every TypeKit package you reference,
the EF Core and ASP.NET Core packages included.

```xml
<PackageVersion Include="CodoMetis.TypeKit" Version="2.1.0" />
<PackageVersion Include="CodoMetis.TypeKit.Generators" Version="2.1.0" />
<PackageVersion Include="CodoMetis.TypeKit.AspNetCore" Version="2.1.0" />
```

## Check builds that treat warnings as errors

2.1 adds two warnings. A build that treats warnings as errors fails on them:

- **A `FaultKind` or `ConstructorAccessibility` that names no member** (CMTK0014), such as
  `(FaultKind)9` or `default`
  ([CMTK0014](../src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0014)).
- **A base record with its own equality under a value object** (CMTK1023). The base declares `Equals`,
  `GetHashCode` or `IEquatable<TBase>.Equals`, which 2.1 no longer calls
  ([the next section](#expect-value-equality-over-a-base-record)).

`FaultKind` also gains two members, `PreconditionFailed` and `Malformed`. A switch expression of yours
that lists every 2.0 member and has no `_` arm now gets the compiler's CS8509. Add the two members, or
a `_` arm.

To move in steps, lower the analyzer rule for a while:

```ini
[*.cs]
dotnet_diagnostic.CMTK0014.severity = suggestion
```

## Expect value equality over a base record

A value object that derives from a base record now compares by its underlying value alone. In 2.0,
the record's equality also called the base's. So a base that compared by reference made two value
objects holding `42` unequal, while `CompareTo` said they were equal. A `HashSet` could then hold two
of them where a `SortedSet` held one.

2.1 generates equality over the underlying value, also where a collection holds the value objects as
the base type. If the base declares equality of its own, the warning CMTK1023 names it. Remove it
from the base, or derive from a record without it
([Equality](../src/CodoMetis.TypeKit.Generators/README.md#equality)).

## Check JSON written with preserved references

Under `ReferenceHandler.Preserve`, writing a value object can now throw `NotSupportedException`. That
happens where the underlying value could hold a `$id`, such as a class the serializer writes as an
object. 2.0 wrote a duplicate `$id` there.

Register a `JsonConverter` for the underlying type, or serialize without preserving references. A
value object over a number, a string or a struct of plain values is unaffected, and so is
`ReferenceHandler.IgnoreCycles` ([JSON](../src/CodoMetis.TypeKit.Generators/README.md#json)).

## Register the same converters for controllers and minimal APIs

In a host with controllers, building the OpenAPI document now fails when MVC's JSON options and the
minimal-API JSON options write a type differently. The error names the type and what each set of
options says about it. 2.0 described every type from the minimal-API options, even where your
controllers wrote it another way.

Register the same converters and descriptions on both, in the same order: `ConfigureHttpJsonOptions`
for minimal APIs, and `AddJsonOptions` for controllers
([What the document says](../src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says)).

## Regenerate clients and document snapshots

Your code needs no change here, but the OpenAPI document does change. Regenerate your clients, and
update any snapshot test of the document.

- **`rule` beside `fault`.** A host with fault responses describes `rule`, an optional string, on the
  shared `ProblemDetails` component. A generated client gains an optional `rule` member on its
  problem type ([The rule that refused](../src/CodoMetis.TypeKit.AspNetCore/README.md#the-rule-that-refused)).
- **Statuses in ascending order.** An operation with fault responses lists every status in ascending
  order, also the ones its endpoint declares itself with `ProducesProblem` or
  `[ProducesResponseType]`. Where an endpoint declares its own, the order of its responses changes.
- **Problems your endpoint declares itself.** Such a problem response now describes `fault` and
  `rule`, and names its problem types.
- **`WithFaultStatus` on controllers.** A `WithFaultStatus` on the builder that `MapControllers()`
  returns now shows in the document. It already changed the response in 2.0.
- **Your own converters.** A value object or closed set that your JSON options write with a converter
  of their own is described as that converter writes it, or as ASP.NET writes it. 2.0 described it as
  its underlying type.
- **Required patches.** A `Patch<T>` that the serializer requires stays in `required`. Under
  `RespectRequiredConstructorParameters`, declare your patches as `init` properties so a request can
  leave them out ([Merge patches](merge-patches.md#required-constructor-parameters)).

## Rename or qualify clashing names

2.1 adds types. If a type of yours has one of these names and you import it beside the package's
namespace, a use becomes ambiguous (CS0104). Qualify one of them, or alias yours with `using`.

- **In `CodoMetis.TypeKit`:** `ValueObjectAttribute`, `InterfaceAttribute`, `UnsealedAttribute`,
  `ClosedSetAttribute`, `FlagsEnumAttribute`, `ReadOnlyStructAttribute`, `DistinctAttribute`,
  `UnrelatedAttribute`, `Fault<T1, T2>`, `Fault<T1, T2, T3>`, `FaultRuleExtensions`,
  `FaultClosedSetRuleExtensions`, `DecimalNumberJsonConverter`, `DecimalStringJsonConverter`,
  `IDescribedJsonConverter`, `JsonSchemaDescription`, `JsonSchemaDescriptions` and
  `DescribedJsonSchema`. Watch `[ValueObject]` and `[Interface]` in particular, which other libraries
  also declare.
- **In `CodoMetis.TypeKit.AspNetCore`:** `WithoutFaultsAttribute<TFault>` and
  `TypeKitWithoutFaultsEndpointConventionBuilderExtensions`.
- **In `CodoMetis.TypeKit.Testing`:** `FaultAssertions`.

2.1 also adds members. If you wrote an extension with the same name and parameters, and import it
beside the package's, a call becomes ambiguous (CS0121). Delete yours.

| On | Added |
|---|---|
| `string` | `AsOptionalText`, `AsOptionalIdentifier` |
| `Fault<TFault>` | `WithRule` |
| `Fault<T1, T2>`, `Fault<T1, T2, T3>` | `ShouldHold` (Testing) |
| an endpoint or a group | `WithoutFaults` |
| `IServiceCollection` | `AddFaultResponses()` without a type argument, and `AddFaultResponses(Func<IFault, FaultResponse>)` |

**Good to know**

- **Two error types that write one name now log a warning.** An endpoint returning
  `Results<NoContent, FaultProblem<A>, FaultProblem<B>>`, where both types have a `NotFound`, still
  starts. The host now logs a warning at startup naming both, and from 3.0.0 it won't start. Fix it
  with `QualifiedFaultNames`, a rename, or `WithoutFaults`
  ([Faults of several types](../src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types)).
- **Some fixes need nothing from you.** A value object over a NodaTime type now parses after MVC bound
  a plain one first. An MVC action that binds a complex type from the route under a name of its own no
  longer breaks the document. A value object whose underlying type has an unusual `Parse` now builds,
  without parsing.

## What is new

[Decimals in one spelling](json.md#decimals-in-one-spelling),
[converters that describe what they write](json.md#describing-what-a-converter-writes),
[optional text](text.md#required-and-optional),
[more type constraints](type-constraints.md#the-kind-of-a-type-argument) and
[constraints between two type parameters](type-constraints.md#two-parameters-apart). In the ASP.NET
Core package: [the rule that refused](../src/CodoMetis.TypeKit.AspNetCore/README.md#the-rule-that-refused),
[errors of several types](../src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types),
[one mapping for every error set](../src/CodoMetis.TypeKit.AspNetCore/README.md#every-fault-set-at-once)
and [leaving out what a route cannot answer](../src/CodoMetis.TypeKit.AspNetCore/README.md#leaving-out-what-a-route-cannot-answer).
