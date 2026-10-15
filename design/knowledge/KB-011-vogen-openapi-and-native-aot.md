---
verified: 2026-10-14
pins:
  - external Vogen 8.0.7
  - external Vogen 9.0.0-beta.2
source: the Vogen repository at tag 8.0.7 (its OpenAPI generation and project files), and the NuGet flat container for 9.0.0-beta.2, read without running anything
turned-on: the OpenAPI, host converter and Native AOT rows of the value-object table in docs/comparison.md, and design/openapi.md
test: none, a competitor's feature, read from its source and package
---

# KB-011. Vogen 8.0.7 generates OpenAPI schemas only on request, from a built-in type table, and does not declare Native AOT compatibility

- OpenAPI generation is opt-in: the default is `OpenApiSchemaCustomizations.Omit`. When it is on, Vogen generates
  a schema transformer for Microsoft.AspNetCore.OpenApi, and Swashbuckle support, with `type` and `format` taken
  from a table compiled into the generator (`decimal` is `number`/`double`).
- A converter the host registers is not reflected: the table's type and format stay, whatever the host's JSON
  options write.
- The package does not declare `IsAotCompatible`.
- 9.0.0-beta.2 is a GitHub prerelease and not on NuGet (its flat-container entry is a 404); 9.0.0-beta.1 is.
