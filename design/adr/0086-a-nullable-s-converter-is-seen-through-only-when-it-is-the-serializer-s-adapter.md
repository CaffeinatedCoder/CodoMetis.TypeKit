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
