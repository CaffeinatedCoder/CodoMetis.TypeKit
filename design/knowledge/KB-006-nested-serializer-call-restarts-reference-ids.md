---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: System.Text.Json in the .NET 10 runtime under ReferenceHandler.Preserve, measured with a converter that writes its value through JsonSerializer.Serialize(writer, value, options)
turned-on: decision 76 (a value object refuses preserved references inside its wrapped value)
test: test/CodoMetis.TypeKit.Facts/KB006.cs
---

# KB-006. Under `ReferenceHandler.Preserve`, a nested `JsonSerializer` call numbers its references from 1 again

References are numbered per serializer call. One call writes a second reference to an instance as `$ref`:

```json
{"$id":"1","First":{"$id":"2","Name":"n"},"Second":{"$ref":"2"}}
```

A converter that hands the same instance to a nested `JsonSerializer.Serialize(writer, value, options)`, or to the
typed `Serialize(writer, value, jsonTypeInfo)` a trim-safe converter calls, starts a new numbering, so it writes a
second definition with an `$id` the document already has, and no `$ref`:

```json
{"$id":"1","First":{"$id":"2","Name":"n"},"Second":{"$id":"1","Name":"n"}}
```

Reading that document back with `Preserve` throws `JsonException`: "The value of the '$id' metadata property '1'
conflicts with an existing identifier". A converter cannot reach the caller's reference resolver to avoid it
([decision 76](../adr/0076-preserved-references-refused.md)).
