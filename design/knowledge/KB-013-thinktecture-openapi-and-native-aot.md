---
verified: 2026-10-14
pins:
  - external Thinktecture.Runtime.Extensions 10.5.1
source: the Thinktecture.Runtime.Extensions repository at tag 10.5.1 (its Swashbuckle package and project files), read without running anything
turned-on: the OpenAPI, host converter, Native AOT and targets rows of the value-object table in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-013. Thinktecture.Runtime.Extensions 10.5.1 documents OpenAPI through Swashbuckle alone, and does not declare Native AOT compatibility

- OpenAPI is a Swashbuckle package only, with `type` and `format` copied from Swashbuckle's static table; nothing
  for Microsoft.AspNetCore.OpenApi. A smart enum's listed values are written with the host's JSON options.
- A converter the host registers is not reflected in type and format.
- The Swashbuckle and ASP.NET Core packages target `net8.0` only; the core and JSON packages `net8.0` and `net9.0`.
- No package declares `IsAotCompatible`.
