---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: System.Text.Json in the .NET 10 runtime, measured with a DefaultJsonTypeInfoResolver modifier setting JsonPropertyInfo.CustomConverter on a property that carries [JsonConverter]
turned-on: decision 86 (a property's attribute is asked only where its converter is the adapter)
test: test/CodoMetis.TypeKit.Facts/KB009.cs
---

# KB-009. A resolver modifier's `CustomConverter` writes a property over the converter its `[JsonConverter]` names

A `decimal?` property with `[JsonConverter(typeof(DecimalStringConverter))]` is written `{"value":"1.2"}`: the
serializer adapts the attribute's `JsonConverter<decimal>` to the nullable property. A modifier that sets
`JsonPropertyInfo.CustomConverter` to a `JsonConverter<decimal?>` of its own puts that one in charge, attribute or
not: `{"value":true}`. So a property's attribute says what writes it only while the property's converter is still
the adapter over the attribute's converter
([decision 86](../adr/0086-a-nullable-s-converter-is-seen-through-only-when-it-is-the-serializer-s-adapter.md)).
