# Spike: can a value object's fault become a validation error in the response?

**Question.** Phase 12 lists "a value object's fault as a validation error": a body or parameter that
`Create` refuses should be a `400` whose problem details name the member and the fault, as ASP.NET Core
10's validation reports a data annotation, instead of a bare `400` from a `JsonException`. Where does
ASP.NET hand the refusal to code a package could register, in minimal APIs and MVC, for a body, a route
and a query parameter?

**Answer: only for a minimal-API body, and only with `ThrowOnBadRequest`.** Measured 2026-10-11 with
ASP.NET Core 10.0.12, against value objects woven by the real generators. ASP.NET Core 10's validation
(`AddValidation()`) runs after binding and never sees a value that failed to bind. A refused body reaches
no hook unless `RouteHandlerOptions.ThrowOnBadRequest` is on; then the exception handler receives a
`BadHttpRequestException` around the converter's own `JsonException`, path included, and can answer
with a validation problem (finding 6). A refused route or query value leaves nothing to report: `TryParse`
returns `false`, and the fault is gone (finding 5). MVC already reports a refused body in its validation
problem, with the converter's message (finding 4).

```bash
dotnet run --project spikes/ValidationFaults                           # every setup
dotnet run --project spikes/ValidationFaults -- problem statuspages    # a subset
```

Setups: `bare`; `problem` (`AddProblemDetails()`, recording `CustomizeProblemDetails`); `statuspages`
(with `UseStatusCodePages()`); `validation` (with `AddValidation()`); `throw` (`ThrowOnBadRequest`,
`UseExceptionHandler()`, a recording `IExceptionHandler`); `bridge` (`throw` and the prototype in
`Bridge.cs`). `Probe` is a hand-written converter that refuses as the generated one does and also puts
its fault in the exception's `Data`, to see whether that instance reaches a hook.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | Minimal API, a refused body (`Email` in a record): `400` with an empty body, with `AddProblemDetails()` too. `CustomizeProblemDetails` is not called; nothing receives the exception | ✅ |
| 2 | With `UseStatusCodePages()` beside it, the `400` has a generic problem body (`"title":"Bad Request"`), naming neither the property nor the rule. The README's "Binding" said `AddProblemDetails()` alone gives a body; it is corrected, and `HostConfigurationTests` holds both halves | ✅ |
| 3 | `AddValidation()` reports a data annotation as `{"errors":{"Name":["…"]}}`, keyed by the CLR property name; a refused value object never reaches it, the body having failed to bind | ✅ |
| 4 | MVC, a refused body: a validation problem, `"$.email": ["Email refused the JSON value (NoAt). See …"]`, beside a spurious `"signup": ["The signup field is required."]`. The model-state entry keeps the message only, not the exception, so no structured fault is recoverable there | ✅ |
| 5 | A refused route or query value: minimal APIs answer an empty `400` (`BadHttpRequestException` "Failed to bind parameter \"Quantity seats\" from \"0\"" with `ThrowOnBadRequest`, no inner exception), MVC "The value '0' is not valid.". Both quote the input, and neither has the fault: `TryParse` reports only `false` | ✅ |
| 6 | With `ThrowOnBadRequest`, a refused body reaches the exception handler as `BadHttpRequestException` whose inner exception is the converter's own `JsonException` instance: its `Path` (`$.email`) is set and the probe's `Data` survives. Without a handler that answers it, the exception handler middleware writes `500` | ✅ |
| 7 | The prototype bridge (`Bridge.cs`): every `BadHttpRequestException` keeps its own status, and a refused probe becomes `400 {"errors":{"probe":["NoAt"]}}`. The generated converter's `JsonException` carries the fault only in its message, so for a real value object the bridge has nothing structured to report | ✅ |

## Consequences

- **What can be built** is narrow: an opt-in for minimal APIs that turns on `ThrowOnBadRequest` for the
  whole host, needs `app.UseExceptionHandler()` in its pipeline, and handles every
  `BadHttpRequestException` so that no bad request becomes a `500` (finding 6). For a value object's
  fault to be reported, the generated converter's refusal has to carry it as data, which is a public
  addition to the base package (finding 7).
- **What cannot**: route, query and header values (finding 5) without generated binding of a different
  kind (`BindAsync`), which would change how every value object binds; and MVC beyond what it already
  says (finding 4).
- **Not measured**: the exception handler middleware's logging of a handled `BadHttpRequestException`,
  the Request Delegate Generator under Native AOT, and nested or collection paths (`$.items[2].email`).
