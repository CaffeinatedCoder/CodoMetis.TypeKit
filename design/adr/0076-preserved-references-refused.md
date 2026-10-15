# 0076. A value object refuses preserved references inside its wrapped value

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, an outside review of 1.0.0 and 2.0.0; [Aspects](../aspects.md).

`GeneratedJsonPlan` hands a wrapped type the serializer walks (an object, a collection) to a nested
serializer call, and the serializer numbers references per call. Under `ReferenceHandler.Preserve` a
value object over a class wrote `"$id":"1"` inside a document that already had one, two around one
instance wrote two definitions where the wrapped type writes a `$ref`, and the wrapped type's document
was unreadable through the value object (measured on 1.0.0 and 2.0.0). That breaks
[decision 26](0026-value-object-s-json-wrapped-type.md), and a converter cannot reach the caller's
references to fix it.

The plan throws `NotSupportedException`, on writing and reading, under any reference handler but
`IgnoreCycles`, where the wrapped type's contract may hold references: an object or collection of a
reference type, `object`, or a struct with such a property. The message names the way out: a
`JsonConverter` for the wrapped type, which writes no `$id` for it or for the value object, or no
preserved references. A struct of numbers and strings writes no `$id` and keeps working. `IgnoreCycles`
writes no metadata, and a cycle through the value object fails at the depth limit, which is loud.

It ships in a minor version as a fix: what it refuses wrote a document the serializer itself refuses to
read (a duplicated `$id`), or lost the identity the handler exists to keep.
