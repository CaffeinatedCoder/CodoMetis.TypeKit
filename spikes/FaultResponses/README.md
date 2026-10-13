# Spike: can a fault response carry what a client acts on, without giving up [decision 38](../../design/adr/0038-result-becomes-response-explicit-tohttpresult-faults.md)'s checks?

**Status: run 2026-10-13, answered.** From the "Fault responses a client can act on" part of phase 13
(2.0.0) in the former plan's phase log (git, `design/plan.md` before 2026-10-13). The fault's kind is no longer a `FaultKind`
attribute: it is a property of a closed-set member, in the same phase.

**Question.** Decision 38's `ToHttpResult()` answers a fault with its status and its bare name, and
a success with `200` or `204`. An application that shows a refusal to a person, creates resources,
or reads with an ETag writes those endpoints by hand. Can five opt-in additions close that gap? Each
must leave the default response, the registration-time mapping check, the startup check and the
OpenAPI description as they are, under JIT and Native AOT, in minimal APIs and MVC.

1. **`FaultResponse` per member**: status, `type` URI and `title`, from an
   `AddFaultResponses<TFault>(Func<TFault, FaultResponse>)` overload.
2. **`Fault<TFault>`**, a record struct in the base package, so a sentence travels per occurrence
   while the mapping stays keyed on the fault type.
3. **`.WithFaultStatus(fault, status)`**, endpoint metadata overriding the host's status for one
   endpoint.
4. **`ToHttpResult(success)`**, the endpoint's own success result (`Created<T>`, or an application
   result that sets an ETag) in `Results<TSuccess, FaultProblem<TFault>>`.
5. **`IFaultFeature`** on the request while the problem is written, so `CustomizeProblemDetails`
   can add the host's own extensions, plus an option to qualify `fault` with its type's name.

**Answer: yes, all five, beside a closed set as a fault type answered by its members' kinds.**
Measured 2026-10-13 on .NET SDK 10.0.401, ASP.NET Core 10.0.12 and Microsoft.AspNetCore.OpenApi
10.0.12, under JIT in minimal APIs and MVC, and published with Native AOT by the consumer smoke test.
The candidate was built in `CodoMetis.TypeKit.AspNetCore` itself, since it is the satellite's shipped
shape, and every claim below is a test of it, so this directory holds no project of its own: the
evidence is `FaultResponsePartsTests` and `FaultResponseTests` (test/CodoMetis.TypeKit.AspNetCore.Tests),
`FaultResponseSampleTests` (the README's blocks, run) and the `aot` consumer of
`test/consumer-smoke-test.sh`. Each guard was seeded (the endpoint override ignored, the feature left
on the request, a kind's status changed, the qualified names ignored, the detail dropped, the stray
status check removed, the problem types dropped from the document), and each seed failed its test.

## What the measurement decided

- **One reading of a fault type.** `FaultType<TFault>`, internal, holds the members and each one's
  name, qualified name, kind and description; the mapping, the startup check, the document and the
  response read nothing else. It is made by the registration overload whose constraint says what the
  type is (`Enum.GetValues<TEnum>()` for an enum, `TSet.All` for a closed set), and a member's kind
  is read through the base package's non-generic `IFault`, so nothing reflects.
- **Overloads by constraint, in two classes.** An enum's `AddFaultResponses` and `ToHttpResult` and a
  closed set's take the same parameters and differ only in their constraints, which C# allows only
  across classes; with the type arguments inferred or written, the candidate whose constraint fails
  drops out (C# 7.3), so each call resolves without ambiguity. `Fault<TFault>`'s overloads take a
  receiver of another shape and live beside the enum's.
- **A kind's default status**: `NotFound` 404, `Conflict` 409, `Invalid` 422, `Forbidden` 403,
  `Unavailable` 503, through `FaultResponse.For(kind)`. A host does not change the table; a mapping
  of its own, which can call `FaultResponse.For` for the members it keeps, is the escape hatch, as
  [decision 38](../../design/adr/0038-result-becomes-response-explicit-tohttpresult-faults.md)'s switch is. `FaultKind` starts at 1, so a member whose kind is `default` is refused
  where the set is mapped.
- **Where a sentence goes.** RFC 9457 §3.1.4: `detail` helps the client correct this occurrence. A
  member's description is the `detail` by default, and a `Fault<TFault>`'s detail replaces it; the
  `title` is the problem type's summary, a `FaultResponse`'s or the status's own.
- **The constraint relaxed.** `FaultProblem<TFault>` takes `notnull` where it took `struct, Enum`.
  Package validation reports it (CP0021) though no closed type it accepted is refused now; it is the
  first intended difference from the 1.5.1 baseline, suppressed alone ([decision 48](../../design/adr/0048-intended-break-suppressed-alone-baseline-stays.md)).

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | The `FaultResponse` overload checks every member at registration, as the `int` one does, and refuses a status outside 400 to 599 | measured: `A_response_per_member_is_checked_where_it_is_mapped` (200, 302, 600 refused, naming `OrderFault.NameTaken` and `responseOf`); a kind that is no `FaultKind` refused naming the member |
| 2 | A `type` URI and `title` reach the body and the operation's problem response, and nothing of the input does | measured: the body carries both; the response's description lists the problem types answered with that status (`Conflict. Problem types: …`) |
| 3 | `ToHttpResult()` over `Result<T, Fault<TFault>>`, over `Result<T, TFault>` and over a closed set's resolves without type arguments, with no ambiguity | measured: the tests call each without type arguments and compile |
| 4 | `Detail` reaches the body only when set, and the default response is unchanged from 1.5.1 | measured: the default body's members are 1.5.1's (`fault`, `status`, `title`, `traceId`, `type`), and [decision 38](../../design/adr/0038-result-becomes-response-explicit-tohttpresult-faults.md)'s tests pass unchanged; `detail` appears for a `Fault` with one and a set member with a description, never for one without |
| 5 | `WithFaultStatus` changes the status for that endpoint only, in the response and in that operation's document entry | measured: 422 on the endpoint, 409 elsewhere; the endpoint's operation lists 422 in place of 409 |
| 6 | The startup check refuses a `WithFaultStatus` naming a fault type the endpoint does not answer with | measured; and an MVC attribute naming no member |
| 7 | MVC: the per-endpoint metadata reaches the operation transformer (ResultHttp finding 4 found `PopulateMetadata` handed an empty service provider) | measured: `[FaultStatus<OrderFault>(nameof(OrderFault.NameTaken), 422)]` on an action changes its response and its operation; the transformer reads it from the endpoint's metadata, with the services from the document's context |
| 8 | `ToHttpResult(success)` describes `201` with its body, and a `Location` header is written | measured, minimal API and the smoke test's AOT binary; `Results<TSuccess, …>` needs `TSuccess` annotated as ASP.NET annotates `TResult1`, which the trim analyzer said (IL2091) and the annotation answers, without a suppression |
| 9 | `CustomizeProblemDetails` sees `IFaultFeature` for a problem `ToHttpResult()` wrote, and not for any other problem | measured: present for an enum fault, a `Fault` and a set's member, absent for `TypedResults.Problem` elsewhere and on the request once the problem is written |
| 10 | Qualified `fault` names are opt-in; the default stays the bare member name | measured: `FaultResponseOptions.QualifiedFaultNames` gives `OrderFault.NotFound`, the default `NotFound` |
| 11 | `fault` stays a string on the shared `ProblemDetails` component; a per-operation enum, if offered, is opt-in and never a union across fault types | measured: the component names no fault and no `enum`; no per-operation enum is offered in 2.0.0 |
| 12 | The mapping, the startup check and the document read a fault type through one internal abstraction, so an enum fault and a smart-enum fault take the same path | built so (`FaultType<TFault>`), and measured by a closed set answered by kind end to end, its document listing 403, 404 and 409 |
| 13 | Every part runs under Native AOT with no new trim or AOT warning | measured: the smoke test's `aot` web consumer publishes a fault set answered by kind, a `WithFaultStatus`, a `Fault` with a detail, a `Created` success and a `CustomizeProblemDetails` reading the feature, with no trim or AOT warning naming a package or woven code |
