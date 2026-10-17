---
verified: 2026-10-17
pins:
  - sdk 10.0.4xx
source: measured in a host without TypeKit (Microsoft.AspNetCore.App 10.0 of the pinned SDK, an [ApiController] with an int route parameter, MVC's JSON options given a source-generated context listing int alone); ASP.NET Core's web API documentation, "Automatic HTTP 400 responses", names ValidationProblemDetails as the default type of that response
turned-on: rows 286 and 309 of BaseProbe (claim C07-fault-responses-blanket) under json.TypeInfoResolver=source-generated-context, where a value Create refuses answered 500 instead of MVC's 400; until WS2's three-sided differential told by the exception's words (note mvc-400-unwritable-under-source-gen), now by side (1), the wrapped twin sent a text it cannot read on a host without TypeKit
test: test/CodoMetis.TypeKit.Facts/KB041.cs
fixture: test/CodoMetis.TypeKit.Facts/KB041.fixture.json
---

# KB-041. MVC cannot write its automatic 400 under a source-generated context that does not list `ValidationProblemDetails`

An `[ApiController]` action whose parameter MVC cannot bind answers 400 with a `ValidationProblemDetails`, which
MVC writes with its own JSON options. Give those options a source-generated context that lists the action's types
but not `ValidationProblemDetails`, and the 400 is never written: the request fails with System.Text.Json's
`NotSupportedException`, which a test server passes to its client, for a parameter of any type. Under the
serializer's default resolver the same request is a 400.

| `GET /kb041/abc` to an action's `int` route parameter | MVC's JSON options' resolver |
|---|---|
| `NotSupportedException` | a source-generated context listing `int` alone |
| 400 | the serializer's default |

So under such a context a refused binding has no 400 to compare: a value object whose text its `Create` refuses
fails the request as the wrapped type does with a text it cannot read. The HTTP probes show it in the same row with
the twin on a host without TypeKit, or read this fixture where it covers the row.
