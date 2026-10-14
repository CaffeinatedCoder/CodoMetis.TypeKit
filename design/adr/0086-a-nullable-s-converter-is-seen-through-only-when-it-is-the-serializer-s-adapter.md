# 0086. A nullable's converter is seen through only where it is the serializer's adapter, and a hand-built nullable contract is described by its value's

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the reviews of the unreleased 2.1.0 that led to [decision 84](0084-cmtk0010-proofs-read-what-a-closed-use-can-be.md);
refines how [decision 83](0083-the-contract-s-converter-first-and-enum-proves-nothing-against-struct.md) sees through
a `Nullable<T>`'s converter. Measured with System.Text.Json 10.

**A property's converter of the nullable type is its own unless it is the adapter.** `JsonSchemaDescription.For`
asked a nullable property's `[JsonConverter]` attribute for the converter it names whenever the property's
converter converted the nullable type and described nothing, on the reading that it was System.Text.Json's adapter
of the named converter. A resolver's modifier can put a converter of the nullable type of its own in charge:
with `[JsonConverter(typeof(DecimalStringJsonConverter))] decimal? Value` and a modifier setting
`CustomConverter` to one that writes `true`, the wire was `{"value":true}` and the description the attribute's
decimal string. The attribute is now asked only where the property's converter is the adapter
(`GeneratedJson.IsNullableAdapter`, the check the contract's converter already goes through), and a converter of
one's own that says nothing is described as nothing, the empty schema.

**A nullable contract built by hand is described and judged by its value's, a known limit.** The adapter is
System.Text.Json's converter of a nullable type, recognised by its assembly; it writes null or what the
converter it was built over writes. The reflection resolver and a source-generated context build it from the
contract they give `T`, so asking `T`'s contract is asking the converter it holds. A resolver of one's own
can build one by hand, `JsonMetadataServices.GetNullableConverter(JsonTypeInfo<T>)` over a contract whose
converter is not the one `T` resolves to. Then the description is `T`'s contract's (measured: a number
written, the options' decimal string described), and under `ReferenceHandler.Preserve` a value object over a
nullable struct is judged by the struct's walk, refused though the converter inside writes no `$id`. No
public API exposes the adapter's element converter; it is a private field, which trimming and Native AOT do
not keep, and reflection over it was rejected in decision 83. The description's error is the kind decision
73 refuses to make silently, but there is nothing to read, and the reference check errs loud. Both are pinned
by tests (`DescribedJsonSchemaTests`, `ReferenceHandlingTests`) and said in `For`'s remarks and in the JSON
guide; the way out is a converter of the nullable type itself, which is described and judged as its own.
