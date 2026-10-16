---
verified: 2026-10-16
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: the document Microsoft.AspNetCore.OpenApi writes for an MVC [FromBody] int and int?, and a minimal-API [FromBody] enum?, under OpenApiSpecVersion.OpenApi2_0 and OpenApi3_1, measured in a host without TypeKit
turned-on: the probe finding G1, dismissed as no TypeKit bug (tools/probe/corpus/dismissed.jsonl), and the probe harness comparing a nullable value object with the nullable wrapped type
test: test/CodoMetis.TypeKit.Facts/KB001.cs
---

# KB-001. Under OpenAPI 2.0, ASP.NET writes a nullable request body as `allOf: [{x-nullable: true}]` and drops its `pattern`

A request body whose type is nullable, a plain `int?` included, is optional and written by the OpenAPI 2.0 writer
as its `format` and `allOf: [{"x-nullable": true}]`, without the `pattern` the non-nullable body has:

| `[FromBody]` | OpenAPI 2.0 body parameter |
|---|---|
| `int` | `"required": true`, `{"format":"int32","pattern":"^-?(?:0\|[1-9]\\d*)$"}` |
| `int?` | not required, `{"format":"int32","allOf":[{"x-nullable":true}]}` |

A minimal API writes it the same, and a nullable enum body, which has no `format`, keeps nothing but the null:
`{"allOf":[{"x-nullable":true}]}` (row 257 of C21OpenapiSchemaParityProbe, measured 2026-10-16).

OpenAPI 3.1 keeps the pattern, inside `oneOf: [{"type":"null"}, {…}]`. In MVC a body parameter of a class type is
nullable too, so a class value object over `int` loses the pattern beside a required `int`, and has the same bytes
as `int?`. A probe that compares a nullable body with a required one measures this, not TypeKit.
