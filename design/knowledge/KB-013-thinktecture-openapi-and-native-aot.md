---
verified: 2026-10-29
pins:
  - external Thinktecture.Runtime.Extensions 10.6.0
source: the Thinktecture.Runtime.Extensions repository at tag 10.6.0 (its Swashbuckle package and project files) and the 10.6.0 release notes, read without running anything; first read at 10.5.1 on 2026-10-14
turned-on: the OpenAPI, host converter, Native AOT and targets rows of the value-object table in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-013. Thinktecture.Runtime.Extensions 10.6.0 documents OpenAPI through Swashbuckle alone, and does not declare Native AOT compatibility

- OpenAPI is a Swashbuckle package only, with `type` and `format` copied from the schema Swashbuckle generates for
  the key type; nothing for Microsoft.AspNetCore.OpenApi.
- A smart enum's listed values are written with the host's JSON options: since 10.6.0, MVC's `JsonOptions` when MVC
  is registered, otherwise the Minimal API (`Http.Json`) options, as Swashbuckle 10.3 resolves them. A
  `JsonSerializerOptions` registered in the service provider is no longer used.
- A converter the host registers is not reflected in type and format.
- The Swashbuckle and ASP.NET Core packages target `net8.0` only; the core and JSON packages `net8.0` and `net9.0`.
- No package declares `IsAotCompatible` or `IsTrimmable`.
