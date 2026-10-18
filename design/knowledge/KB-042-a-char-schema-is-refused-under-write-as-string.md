---
verified: 2026-10-18
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: the document Microsoft.AspNetCore.OpenApi writes for a minimal API whose body holds a char, and one whose body holds an int, under JsonNumberHandling.WriteAsString and the default number handling, measured in a host without TypeKit; ASP.NET Core 10's OpenApiSchemaService builds each schema with System.Text.Json's JsonSchemaExporter under the host's options and reads the node back as its own OpenApiJsonSchema, and a char's schema is the exporter's minLength and maxLength of 1
turned-on: row 408 of C21OpenapiSchemaParityProbe (claim C21-openapi-schema-parity), under json.NumberHandling=WriteAsString with json.MaxDepth=3, where AddTypeKit() refused the document with this JsonException, asking for the char a value object wraps, and ASP.NET alone with MaxDepth's InvalidOperationException, met first at another endpoint
test: test/CodoMetis.TypeKit.Facts/KB042.cs
fixture: test/CodoMetis.TypeKit.Facts/KB042.fixture.json
---

# KB-042. Under `NumberHandling` with `WriteAsString`, ASP.NET refuses the OpenAPI document of an endpoint whose schema holds a `char`

ASP.NET builds a schema with System.Text.Json's schema exporter, under the host's JSON options, and reads the node
back as its own `OpenApiJsonSchema`. A `char`'s schema is a string of `minLength` and `maxLength` 1. With
`JsonSerializerOptions.NumberHandling` set to `WriteAsString`, those two numbers reach the reader as strings, and
generating the document fails with a `JsonException`, "The JSON value could not be converted to OpenApiJsonSchema",
whose inner exception cannot read a string token as a number. A host without TypeKit does so for any endpoint
whose body holds a `char`. A number's schema is written under the same option, and a `char`'s under the default
number handling.

| A minimal API's body of `Holder<T>` | `NumberHandling = WriteAsString` | the default number handling |
|---|---|---|
| `T` is `char` | `JsonException`, the document is not written | `{"maxLength":1,"minLength":1,"type":"string","format":"char"}` |
| `T` is `int` | `{"pattern":"^-?(?:0\|[1-9]\\d*)$","type":["integer","string"],"format":"int32"}` | (not measured) |

A value object over `char` is described with that same schema, asked of ASP.NET, so under this option its document
is refused as the wrapped type's is. Which of two refusals a document meets first follows the order ASP.NET visits
its endpoints and schemas in, so under a second refusing option (`MaxDepth`) a host with TypeKit, which asks for the
`char` while it describes the value object, can meet this one where ASP.NET alone meets the other.
