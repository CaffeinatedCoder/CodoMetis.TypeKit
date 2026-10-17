---
verified: 2026-10-17
pins:
  - sdk 10.0.4xx
source: measured in a host without TypeKit (Microsoft.AspNetCore.App 10.0 of the pinned SDK, an [ApiController] with an int parameter per binding source); the API reference of MvcOptions.MaxValidationDepth says only that "ValidationVisitor will throw if traversing an object exceeds the maximum allowed validation depth" and that "The default value is 32", nothing of binding errors
turned-on: 28 rows of BaseProbe (claim C07-fault-responses-blanket) under mvc.MaxValidationDepth=1, where a validated value object answered a value its Create refuses with 200 holding its default (WS2 step 4 triage)
test: test/CodoMetis.TypeKit.Facts/KB037.cs
---

# KB-037. Under `MaxValidationDepth = 1`, MVC runs an action whose parameter it could not bind, with the parameter at its default

With `MvcOptions.MaxValidationDepth = 1`, an `[ApiController]` action whose `int` parameter MVC cannot bind from
the text it was sent runs anyway. The parameter is `0`, its default, and the action answers 200. That holds for a
route, query, header, form and body parameter alike. With the default depth, 32, every one of those requests is a
400 from the model-state filter.

| Request to an `int` parameter | `MaxValidationDepth = 1` | default (32) |
|---|---|---|
| route, query, header or form `abc`, body `"abc"` | 200, the parameter `0` | 400 |
| the same with `5` | 200, `5` | 200, `5` |
| `[FromQuery]` class whose `int` property gets `abc` or `5` | `InvalidOperationException`: "ValidationVisitor exceeded the maximum configured validation depth '1' when validating property 'V'" | 400 for `abc`, 200 for `5` |

So under that setting a binding failure leaves no 400 to compare: a value object whose text is rejected arrives at
the action as its default, an uninitialized instance that went through no factory, as the wrapped type arrives as
its own default. The HTTP probes compare the two in the same row and cite this entry where both hold their default.
