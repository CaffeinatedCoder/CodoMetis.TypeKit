---
verified: 2026-10-17
pins:
  - sdk 10.0.4xx
source: measured with System.Text.Json of the pinned SDK, a list of int nested in objects, written once by the serializer's own converter and once by a converter of the application's; the API reference of JsonSerializerOptions.MaxDepth says only that it is "the maximum depth allowed when serializing or deserializing JSON", nothing of converters
turned-on: the rows of BaseProbe, C10-preserve-guard, L01-json-bytes and L04-round-trip under json.MaxDepth=3 where a value object, which its converter writes, was refused at a depth its wrapped twin was written at; until WS2's three-sided differential these were told by the refusal's message (note stj-maxdepth), now by side (1), a converter of the application's for the wrapped type without TypeKit
test: test/CodoMetis.TypeKit.Facts/KB040.cs
fixture: test/CodoMetis.TypeKit.Facts/KB040.fixture.json
---

# KB-040. Under `MaxDepth`, System.Text.Json refuses a value a converter of one's own writes at the limit, where its own converter writes it

With `JsonSerializerOptions.MaxDepth = 3`, a list of `int` inside two objects, `{"P":{"P":[1]}}`, is written: the
writer is at depth 3 when it writes the element. Register a converter of your own for `int` that writes the same
number, and the same document is refused with a `JsonException` whose `Path` is the list's, `$.P.P`, before the
converter is asked to write anything. One level less deep, `{"P":[1]}`, the converter's value is written. Reading
is not charged: the converter reads the element of `{"P":{"P":[1]}}` under the same limit.

| The element of a `List<int>` at the writer's depth | the serializer's own converter | a converter of one's own |
|---|---|---|
| 2 (`{"P":[1]}`), `MaxDepth = 3` | written | written |
| 3 (`{"P":{"P":[1]}}`), `MaxDepth = 3` | written | `JsonException` at `$.P.P` |
| 3, read back | read | read |

A value object is written by its generated converter, so under a depth limit it is refused one level earlier than
its wrapped type, which the serializer's own converter writes, and the same holds for any converter an application
registers. The probes' side (1) for such a row is a converter of the application's for the wrapped type, without
TypeKit, or this fixture where it covers the row's limit and the depth the value is written at.
