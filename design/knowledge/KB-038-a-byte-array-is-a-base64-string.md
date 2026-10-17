---
verified: 2026-10-17
pins:
  - sdk 10.0.4xx
source: "Supported types in System.Text.Json" (learn.microsoft.com, dotnet/docs supported-types.md, System.Array namespace): "`byte[]` is handled specially and serializes as a base64 string, not a JSON array."; measured with System.Text.Json of the pinned SDK
turned-on: row 83 of BaseProbe (claim C07-fault-responses-blanket), where an array of value objects over byte was written [7] and the wrapped twin, a byte[], "Bw=="; docs/json.md "The wrapped type's JSON" and the not-promised entry a-byte-array-is-base64 of claim L01-json-bytes
test: test/CodoMetis.TypeKit.Facts/KB038.cs
---

# KB-038. System.Text.Json writes a `byte[]` as one base64 string, and every other collection as a JSON array

`JsonSerializer.Serialize(new byte[] { 7, 255 })` is `"B/8="`, and a `byte[]` reads back from a base64 string,
never from `[7,255]`. A `List<byte>` is `[7,255]`, and an array of any other element, a struct holding a byte
included, is a JSON array.

So swapping `byte` for a value object over `byte` in an array changes the array's JSON: each element keeps the
byte's JSON, a number, and the array becomes a JSON array of numbers instead of the base64 string (docs/json.md,
"The wrapped type's JSON").
