# 0115. A setting of `AddTypeKit` writes every value object in place, with no component of its own

Date: 2026-10-29
Status: accepted

Context: [#83](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/83), in the 2.3.0 epic;
[OpenAPI](../openapi.md). With `AddTypeKit()` a value object in a JSON position keeps a component named after it
([decision 24](0024-nested-value-objects-named-after-nesting.md)), and a parameter is written in place where ASP.NET
writes one of the wrapped type so, or where the value object wraps a type the options describe
([decision 106](0106-a-parameter-of-a-value-object-over-a-described-type-is-inlined.md)). Some clients want plain
types: a generator that makes a class of every component gets a `CustomerId` wrapper where the application wanted a
`string`, and under OpenAPI 2.0 a nullable property of a component loses its type
([KB-026](../knowledge/KB-026-openapi-2-nullable-component-property-loses-its-reference.md)).

A host could already inline value objects with a `CreateSchemaReferenceId` of its own, set after `AddTypeKit()`,
that returned `null` for every type carrying `GeneratedValueObjectAttribute`: the package README showed that
predicate, and the transformer's inline path, nullable positions included, was measured and tested through it. It
asks the host to reflect over a `CompilerServices` attribute, and it replaces the delegate `AddTypeKit()` installed,
so a closed set lost its nesting-chain name and decision 55's check with it.

## Decision

**`AddTypeKit(typeKit => typeKit.InlineValueObjects = true)` writes every value object in place.** An overload of
`AddTypeKit` takes an `Action<TypeKitOpenApiOptions>`; the setting is off by default, and `AddTypeKit()`, or the
overload with the setting off, writes the document it wrote before, byte for byte. A settings object rather than a
`bool` parameter leaves room for a second setting without another overload, and it is per document, as `AddTypeKit`
is per `OpenApiOptions`.

**It is done where the names are given.** The `CreateSchemaReferenceId` that `AddTypeKit` wraps answers `null` for a
value object, or the `Nullable<T>` of one, before it asks the host's delegate. Every position then takes the path a
host's own `null` took: a property, a body, a response, a list's or a dictionary's elements, a parameter and an element
of a query or header array, at any depth, each the wrapped type's schema in place, and a nullable value object admits
null in its own schema as a nullable property of the wrapped type does
([decision 36](0036-nullable-value-object-admits-null-openapi.md)). A dictionary keyed by a value object is the one
keyed by the wrapped type, since ASP.NET describes no key.

**Value objects only.** A closed set keeps its component, and so does the type a value object wraps where ASP.NET makes
it one (an enum): the value object holds that component's content in place, as with the host's predicate.
A closed set is an enum of its own names, not a wrapper of another type's schema, and inlining the wrapped types would
be `CreateSchemaReferenceId = _ => null`, which ASP.NET already offers.

**With decision 55**, an inlined value object claims no name: a nested value object and a top-level one of its chain's
name are two schemas in place, not a refusal. A closed set still claims its name, and a document in which two types
claim one is still refused. A name the host's own delegate gives a value object is not used; one it gives any other
type is kept, as before. A delegate set after `AddTypeKit` replaces the setting, as it replaces the naming.

**With decision 106**, the setting leaves nothing to decide: where no value object is a component, its JSON positions
and its parameters share no schema, so a parameter over a described type is written in place as a parameter, and its
JSON positions as the converter writes them, each where it is used.

## Consequences

- The package README's predicate gives way to the setting in the documents. It still works, and
  `InlinedValueObjectTests`, `HostConfigurationTests`, `DescribedParameterTests` and `PolymorphicBaseTests` keep holding
  a host's own `null`.
- `SchemaTests` holds each value-object property to the wrapped type's under both settings, in OpenAPI 3.1, 3.0 and 2.0;
  inlined, the nullable properties, the containers and the nested containers too, whose controls were added. It is
  registered as an oracle, with the seed `inline-value-objects-ignored`.
- `NullablePositionTests`' `inline-value-objects` mode is the setting, in every position and version it already ran.
- `InlineValueObjectsSettingTests` holds the rest: no component of a value object in any version, the default document
  unchanged, a later plain `AddTypeKit()` keeping them inlined, the host's names for other types, and decision 55's
  claims.

## Evidence

- At the change: `SchemaTests`, `NullablePositionTests` and `InlineValueObjectsSettingTests` 705 green.
- With the setting ignored (the `null` taken out): 169 of 522 fail: 92 inline rows of `SchemaTests` (all but the
  dictionary keyed by a value object, in each version), 72 `inline-value-objects` rows of `NullablePositionTests`, and
  five of `InlineValueObjectsSettingTests`. `oracles seeds inline-value-objects-ignored`: caught, 92 of 164.
- With the setting on by default: 70 of 174 fail, every component row of `SchemaTests` and the default-document test
  in each version.
