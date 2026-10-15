---
verified: 2026-10-15
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: System.Text.Json and Microsoft.AspNetCore.OpenApi in .NET 10, measured with a property written by a converter of one's own, and the issues dotnet/runtime#105769 and dotnet/aspnetcore#63051 as of 2026-10-14
turned-on: decision 71 (converters say what they write) and "A gap none of them fills" in docs/comparison.md
test: test/CodoMetis.TypeKit.Facts/KB023.cs
---

# KB-023. A JSON converter of one's own cannot say what it writes: the schema exporter writes the empty schema, and ASP.NET only its CLR type's format

For a `decimal` property written as a string by `[JsonConverter(typeof(DecimalStringConverter))]`:

- `JsonSerializerOptions.GetJsonSchemaAsNode` writes the property as `true`, the empty schema, while the same
  type without the converter is a number;
- Microsoft.AspNetCore.OpenApi writes `{"format":"double"}`: the format of the CLR type, no type, and not the
  string the converter writes.

The serializer's hook for a converter to describe itself, `JsonConverter.GetSchema`, is internal. As of
2026-10-14 the proposal to make it public, dotnet/runtime#105769, is open with milestone Future, and
dotnet/aspnetcore#63051 (a custom converter documented as the empty schema) is open. A test observes the first
three; the issues' state is read, and changes without a dependency bump.
