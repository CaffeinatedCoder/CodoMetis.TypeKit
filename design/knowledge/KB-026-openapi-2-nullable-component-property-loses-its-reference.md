---
verified: 2026-10-16
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: the document Microsoft.AspNetCore.OpenApi writes for an MVC [FromBody] record with a property of a record struct and one of its Nullable<T>, under OpenApiSpecVersion.OpenApi2_0 and OpenApi3_1, measured in a host without TypeKit
turned-on: row 38 of C21OpenapiSchemaParityProbe (claim C21-openapi-schema-parity), where a nullable value-object property that is a component had no type under OpenAPI 2.0
test: test/CodoMetis.TypeKit.Facts/KB026.cs
fixture: test/CodoMetis.TypeKit.Facts/KB026.fixture.json
---

# KB-026. Under OpenAPI 2.0, ASP.NET writes a nullable property of a component as `allOf: [{x-nullable: true}]`, without the reference

A property whose type is a component (a record struct here) refers to it by `$ref`. The same property made
nullable is written by the OpenAPI 2.0 writer as `allOf: [{"x-nullable": true}]` and nothing else: the reference,
and with it the type, is gone.

| Property | OpenAPI 2.0 | OpenAPI 3.1 |
|---|---|---|
| `Spot Always` | `{"$ref":"#/definitions/Spot"}` | `{"$ref":"#/components/schemas/Spot"}` |
| `Spot? Maybe` | `{"allOf":[{"x-nullable":true}]}` | `{"oneOf":[{"type":"null"},{"$ref":"#/components/schemas/Spot"}]}` |

A value object keeps a component of its own (decision 36), so a nullable value-object property loses its type
under 2.0 where the wrapped type's nullable property, which is no component, keeps it. With TypeKit the wrapped
type's `format` survives beside the `allOf` (`{"format":"time","allOf":[{"x-nullable":true}]}` for a value object
over `TimeOnly`); inlined, the value object's property is the wrapped type's,
`{"type":"string","format":"time","x-nullable":true}`.
