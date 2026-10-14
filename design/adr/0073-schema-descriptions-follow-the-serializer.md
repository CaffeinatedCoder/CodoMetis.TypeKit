# 0073. Schema descriptions follow the converter the serializer picks, and refuse what they cannot say

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, an external review of the unreleased 2.1.0, each case reproduced before it was
fixed; refines [decision 71](0071-converters-say-what-they-write.md).

Each of decision 71's pieces was tested alone; their combinations produced wrong schemas without a word.

**The converter in charge is the serializer's.** `JsonSchemaDescription.For` answers in the order the
serializer picks a converter: the options' descriptions, the application's word, first; then the
property's own converter; the first of the options' converters that converts the type; the type's own,
a value object's as its wrapped type and a closed set's as its names; the resolver's. Decision 71 put
the generated value-object and closed-set descriptions before the options' converters, so a described
converter registered for a value object wrote strings under a schema of the wrapped decimal. The serializer
adapts a converter named on a `Nullable<T>` property to the nullable type, and the adapter says nothing;
the attribute is asked for the converter it names. The OpenAPI transformer leaves a value object or a
closed set to its own transformer only where the generated converter writes it, and a patch asks the
argument's description first, as a property of the argument's type does.

**A description sits where it is put.** The exporter writes a recursive type's references from that
type's root; put inside another schema, `#/properties/child` resolved nowhere. They are rewritten to point
from the document's root. A `SameAs` schema is one non-null value, so the exporter's null at a reference
type's root is taken out, and the position puts null back where a plain type in it would get one: a
nullable value type, a nullable property, a reference type's element unless the exporter treats
null-oblivious types as non-nullable. The OpenAPI document follows ASP.NET's own placement, which writes
a `string?` element as non-null.

**What cannot be said is refused.** Three cases fail where the document would otherwise be wrong for
some readers:
- Microsoft.OpenApi parses a keyword it does not model (`prefixItems`) into unrecognised keywords, where
  no validator sees it; beside `items: false` that refused every non-empty tuple. A description is written
  back in 3.1 and compared with what was described, a type list as a set, numbers by value, and a
  `false` annotation the writer leaves out as its default (`readOnly`, `writeOnly`, `deprecated`,
  `uniqueItems`) as kept; a difference fails the document, naming the keyword. A boolean schema where the
  model holds an object, `items: false`, is dropped and so refused too. Measured kept: `const`,
  `examples`, `$comment`, the numeric and array and object bounds, `contains`, `propertyNames`,
  `dependentRequired`, `if`/`then`/`else`, `not`, `unevaluatedProperties: false`, an `enum` with null, an
  `x-` extension. Rejected: refusing by a list of supported keywords, which goes stale with each
  Microsoft.OpenApi release.
- A described converter one property names, for a type ASP.NET writes once as a component, a value
  object: the component would say it for every position or for none, by whichever came first.
- The MVC agreement check compared converter names as a set, so the same converters in another order
  passed while MVC wrote numbers under a document of strings, and two description sets of one type were
  never told apart. It now compares, for each type the document holds and each element type, what
  `For` answers under each set of options, by `SameAs` type or by schema: order, configuration and
  entries count, two equal sets in separate instances agree.

**A converter that says nothing is not the generated one either.** A value object or a closed set the
options give a converter that does not describe itself was still described as its wrapped type or its
names, which that converter does not write, as in 2.0. It is now left as ASP.NET writes any converter of
one's own, in every JSON position and inside a patch: ASP.NET's `{}`, and no `items` for an element.
Rejected: refusing the document, which would stop a host on upgrade that 2.0 served, if wrongly. A
converter that one property names and that says nothing is left as it was: the type is a component, written
once for every position.

**Not refused**: a description in `JsonSchemaDescriptions` that contradicts the converter, as decision 71
says; and a `$ref`, `$id` or `$defs` is looked for only where JSON Schema holds a schema, so a property
or a `const` may be called `$id`.
