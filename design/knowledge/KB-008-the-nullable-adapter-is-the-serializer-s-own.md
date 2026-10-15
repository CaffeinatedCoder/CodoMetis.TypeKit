---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: System.Text.Json in the .NET 10 runtime, the converter of a decimal? contract from the reflection resolver and from JsonMetadataServices.GetNullableConverter, inspected and measured
turned-on: decisions 83 and 86 (a nullable's converter is seen through only where it is the serializer's adapter, told by its assembly)
test: test/CodoMetis.TypeKit.Facts/KB008.cs
---

# KB-008. A `Nullable<T>` contract's converter is System.Text.Json's own adapter, and nothing public names the converter it wraps

The converter of a `decimal?` contract, from the reflection resolver or built by hand with
`JsonMetadataServices.GetNullableConverter(JsonTypeInfo<decimal>)`:

- has `Type == typeof(decimal?)`;
- is a type of System.Text.Json's own assembly, and not a public one;
- has no public property that returns the converter it wraps.

It writes null, or what the converter it was built over writes. The reflection resolver builds it over the
contract it gives `T`, so it writes what `T` writes. One built by hand over another contract writes that
contract's output: a number, with a string converter for `decimal` in the options. Telling the adapter apart is
possible only by its assembly, and reading what it wraps only by a private field, which trimming does not keep
([decision 86](../adr/0086-a-nullable-s-converter-is-seen-through-only-when-it-is-the-serializer-s-adapter.md)).
