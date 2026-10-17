---
verified: 2026-10-16
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: the document Microsoft.AspNetCore.OpenApi writes for minimal API header parameters of two enums written by name (JsonStringEnumConverter), each used once nullable and once not, in opposite orders, under OpenApiSpecVersion.OpenApi3_1 and OpenApi3_0, measured in a host without TypeKit
turned-on: row 285 of C21OpenapiSchemaParityProbe (claim C21-openapi-schema-parity, finding F3), where the wrapped E? header's enum listed null through E's component and the value object's did not
test: test/CodoMetis.TypeKit.Facts/KB027.cs
fixture: test/CodoMetis.TypeKit.Facts/KB027.fixture.json
---

# KB-027. The component ASP.NET makes for an enum written by name is the schema of the first use it meets

An enum written by name is a component, and every use of it, nullable or not, refers to that one component. What
the component says is whatever schema ASP.NET built for the first use it met: met first as a nullable parameter,
the component lists null among its values, for its non-nullable uses too; met first as itself, it does not, for
its nullable uses too.

| Uses, in the order ASP.NET meets them | The component |
|---|---|
| `E? v`, then `E v` | `{"enum":["X","Y",null]}` |
| `E v`, then `E? v` | `{"enum":["X","Y"]}` |

So whether a parameter of the wrapped `E?` admits null in a document with components depends on the endpoints
and their order, not on the parameter. A value object over `E` describes a nullable parameter outside the body
the same way in every document: with null among its values where the enum is inlined, as ASP.NET writes an
inlined `E?` there, and as the enum's content without the null where the enum is a component (decision 36: a
component shared with the non-nullable uses cannot admit null).
