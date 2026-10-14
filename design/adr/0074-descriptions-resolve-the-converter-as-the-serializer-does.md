# 0074. Descriptions resolve the converter as the serializer does, and survive the document's version

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a second external review of the unreleased 2.1.0, five cases reproduced before they
were fixed; refines [decision 73](0073-schema-descriptions-follow-the-serializer.md). Measured with
System.Text.Json 10, Microsoft.AspNetCore.OpenApi 10.0.12 and Microsoft.OpenApi 2.12.

Decision 73 made `JsonSchemaDescription.For` follow the converter the serializer picks, and tested each way
of picking one alone. Three ways of picking were still read by their own rule, and two keywords survived
in the document only for some readers.

**A factory is asked for the converter it makes.** `For` passed over a factory in the options as saying
nothing of itself and went on to the type's own description, so a factory that made a described converter
for a value object, and one that made a silent one, both left the document claiming the wrapped decimal;
a factory named on a property was never consulted, and its property was the empty schema. The converter in
charge is now resolved as the serializer resolves it: the property's own, a factory for what it makes for
the property's type; the first of the options' converters that converts the declared type, a
`Nullable<T>` as itself before `T`, a factory for what it makes; the type's own; the resolver's. That
converter is asked, and one that says nothing says nothing. The generated converter is recognised by the
type's own `[JsonConverter]`, which names it, so `StoredJsonConverterFactory`, which makes the generated
converter in its materializing mode, is still described as generated. Rejected: the generated converters
implementing `IDescribedJsonConverter`, which would change the aspects to say what the attribute already
says; and keeping the factory rule as it was, since the serializer's rule is the only one the wire follows.

**A converter registered for a `Nullable<T>` writes the nullable positions.** The serializer looks the
declared type up first, so `For` does too, and `DescribeJson` then receives the nullable type, the one
the converter converts. In the document, a nullable position of a type with a reference id refers to the
component `T` is written as once, for every position; a converter for `T?` alone that describes those
positions otherwise cannot be told there, and the document is refused naming both types and the
component, as it is for a converter one property names.

**A patch's argument is compared under MVC's options.** The document holds a type inside a `Patch<T>`
without asking a transformer of the type itself, so a described converter on each set of options, saying
different things, wrote `{"amount":7.8}` under a document of strings and no check saw it. The patch
transformer compares its argument before describing it.

**`if` and its branches judge the whole value**, null included, whatever the type says: a conditional
description gains null in a branch beside it, as a `oneOf` does, in the exporter and in the document.
Adding `"null"` to the type left the `else` branch refusing it.

**The document's version.** A 3.0 document writes `if`, `then`, `else`, `contains`, `minContains`,
`propertyNames`, `patternProperties`, `dependentSchemas`, `unevaluatedProperties`, `contentEncoding`,
`contentMediaType` and every example after the first aside, as `x-jsonschema-` extensions, and leaves
`dependentRequired` and `$comment` out; a validator reads none of them, so a described `if` accepted what
the description refused. Microsoft.OpenApi's 3.0 reader reads the extensions back into the model, so a
round trip through the model cannot see the loss. The schema is written in the version the document's
`OpenApiOptions` name and the written JSON is checked: every described keyword reaches it under its own
name or 3.0's spelling (null in the type as `nullable`, several types as `anyOf`, a `const` as an `enum`
of one, one example as `example`), and no `x-jsonschema-` key is written; a loss fails the document,
naming the keyword and the version. Rejected: reading the 3.0 document back through the model; a list of
the keywords each version holds, which goes stale with Microsoft.OpenApi.

**The matrix.** One test holds every way of writing a value object (the generated converter, a described
converter, a described factory, a decimal converter, a description in the options, the stored-JSON
factory) in every JSON position (a property, a nullable one, an element, a value, a patch) and both
document versions: what the host's options write is validated against the document's schema by a JSON
Schema validator, and a value the writer does not write is refused, so a schema that admits everything
cannot pass.
