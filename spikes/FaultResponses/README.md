# Spike: can a fault response carry what a client acts on, without giving up decision 38's checks?

**Status: outline, not yet run.** Proposed 2026-10-12, from the "Fault responses a client can act
on" candidate in [docs/plan.md](../../docs/plan.md) §2, which takes `FaultKind` as its first part.

**Question.** Decision 38's `ToHttpResult()` answers a fault with its status and its bare name, and
a success with `200` or `204`. An application that shows a refusal to a person, creates resources,
or reads with an ETag writes those endpoints by hand. Can five opt-in additions close that gap? Each
must leave the default response, the registration-time mapping check, the startup check and the
OpenAPI description as they are, under JIT and Native AOT, in minimal APIs and MVC.

1. **`FaultResponse` per member**: status, `type` URI and `title`, from an
   `AddFaultResponses<TFault>(Func<TFault, FaultResponse>)` overload.
2. **`Fault<TFault>(TFault Kind, string? Detail)`**, a record struct in the base package, so a
   sentence travels per occurrence while the mapping stays keyed on the enum.
3. **`.WithFaultStatus(fault, status)`**, endpoint metadata overriding the host's status for one
   endpoint.
4. **`ToHttpResult(success)`**, the endpoint's own success result (`Created<T>`, or an application
   result that sets an ETag) in `Results<TSuccess, FaultProblem<TFault>>`.
5. **`IFaultFeature`** on the request while the problem is written, so `CustomizeProblemDetails`
   can add the host's own extensions, plus an option to qualify `fault` with the enum's name.

**Answer: pending.**

## The candidate (planned)

The layout follows [spikes/ResultHttp](../ResultHttp/README.md): `Domain.cs` with no ASP.NET in it
(`OrderFault`, methods returning `Result<Order, OrderFault>` and `Result<Order, Fault<OrderFault>>`),
`Candidate.cs` as the satellite would ship it, and `Program.cs` running each part, writing the
documents to `out/` and printing every response body. A `--mvc` switch adds a controller, and an
`-p:Aot=true` publish repeats the minimal-API half under Native AOT.

## Findings

Each row is a claim the spike must settle. A seed that breaks the guard proves each one, as in
`.claude/skills/verify-the-guard`.

| # | Claim | Result |
|---|---|---|
| 1 | The `FaultResponse` overload checks every member at registration, as the `int` one does, and refuses a status outside 400 to 599 | |
| 2 | A `type` URI and `title` reach the body and the operation's problem response, and nothing of the input does | |
| 3 | `ToHttpResult()` over `Result<T, Fault<TFault>>` and over `Result<T, TFault>` both resolve without type arguments, with no ambiguity | |
| 4 | `Detail` reaches the body only when set, and the default response is byte-identical to 1.5.1's | |
| 5 | `WithFaultStatus` changes the status for that endpoint only, in the response and in that operation's document entry | |
| 6 | The startup check refuses a `WithFaultStatus` naming a fault type the endpoint does not answer with | |
| 7 | MVC: the per-endpoint metadata reaches the operation transformer (ResultHttp finding 4 found `PopulateMetadata` handed an empty service provider) | |
| 8 | `ToHttpResult(success)` describes `201` with its body, and a `Location` header is written | |
| 9 | `CustomizeProblemDetails` sees `IFaultFeature` for a problem `ToHttpResult()` wrote, and not for any other problem | |
| 10 | Qualified `fault` names are opt-in; the default stays the bare member name | |
| 11 | `fault` stays a string on the shared `ProblemDetails` component; a per-operation enum, if offered, is opt-in and never a union across fault types | |
| 12 | `FaultKind` attributes are read under Native AOT without a suppression, or the generators emit the table | |
| 13 | Every part runs under Native AOT with no new trim or AOT warning | |
