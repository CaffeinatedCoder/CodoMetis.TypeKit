---
verified: 2026-10-15
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: the paths of the document Microsoft.AspNetCore.OpenApi writes for four MVC controllers, measured in a host without TypeKit
turned-on: docs/aspnet-core.md (which controllers are in the document) and the probe axis value controller-without-ApiController-visible (55c4068)
test: test/CodoMetis.TypeKit.Facts/KB002.cs
---

# KB-002. An MVC controller is in the OpenAPI document only when ASP.NET's API explorer sees it

`Microsoft.AspNetCore.OpenApi` documents what the API explorer lists, and the API explorer lists an MVC controller
only

- with `[ApiController]`,
- with `[ApiExplorerSettings(IgnoreApi = false)]`, or
- through an `IApplicationModelConvention` that sets `ApiExplorer.IsVisible` on its model.

A controller with none of these serves its routes and is missing from the document. A probe that reads the
document for such a controller finds nothing to check, so the probes reach one through a convention
([docs/aspnet-core.md](../../docs/aspnet-core.md#bind-value-objects-in-mvc)).
