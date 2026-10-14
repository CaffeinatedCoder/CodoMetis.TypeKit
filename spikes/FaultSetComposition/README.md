# Spike: how do fault sets share members?

**Status: run 2026-10-14, answered.** From [decision 68](../../design/adr/0068-fault-set-composition-spiked-first.md),
listed under "2.x, with a spike first" in the [roadmap](../../design/roadmap.md).

**Question.** An application whose slices each declare a fault set answers some refusals alike in
all of them: a change that raced the request, a stale version, a hidden character, an outcome nothing
translated, and the shared removal path's three. Shared code decides those once, so today it returns
string codes, and every slice declares its own copy of each member and a table re-keying the codes
onto it. A pilot of `ToHttpResult()` on one slice of such an application measured the cost: 7 of the
slice's 11 members and a 15-line table, about 45 lines a slice, and the slice came out at 643 lines
against 651 before, so converting gave almost nothing back. Which shape lets a set use another's members?

1. **Copy**: the set declares the sets it includes (`[IncludesFaults<ConcurrencyFault>]`), and the
   generators copy their members, by name, kind and description, with a conversion from the included
   set's fault.
2. **Sets side by side**: a fault drawn from one of several sets, `Fault<TeamFault, ConcurrencyFault>`,
   which the endpoint answers as `Results<NoContent, FaultProblem<TeamFault>, FaultProblem<ConcurrencyFault>>`.
   This is the C# 15 union decision 68 named for 3.0.0, written as a library type today.

**Answer: side by side.** It costs the slice what copying costs, and a shared refusal keeps one name
on every route, where a copy gives it one name per slice. It needs no generator, and C# 15 unions
replace it in 3.0.0 rather than retire it. Neither shape narrows the statuses a route lists: that
needs an API of its own (finding 5).

Measured 2026-10-14 on .NET SDK 10.0.402, ASP.NET Core 10.0.13 and Microsoft.AspNetCore.OpenApi 10.0.12,
against the real generators and satellite of `feat/2.1.0` at 415ce73 (project references).

```bash
dotnet run --project spikes/FaultSetComposition                     # every mode
dotnet run --project spikes/FaultSetComposition -- wire document    # a subset
dotnet build spikes/FaultSetComposition -p:Probes=true              # the conversions, as build errors
```

Modes: `wire` (one request per shape and refusal), `document` and `typed-document` (each operation's
responses, the second with a problem type per member), `unmapped` (a shared set left unmapped),
`side-effect` and `middleware` (a host's own error responses, before and after `AddFaultResponses`).

## Layout

- `Shared/` is what a request-path library shares between slices: `ConcurrencyFault`, `RemovalFault`,
  today's string-coded translators (`Rejections`) and the same translators answering a fault of the
  shared set (`SharedFaults`).
- `Baseline/`, `Copy/` and `Union/` each hold one slice, `TeamFault` and its service and endpoints,
  written as that shape lets a consumer write it. Every slice answers the same requests alike.
- `Copy/TeamFault.WouldBeGenerated.cs` and `Union/Prototype.cs` hold what the package would provide,
  written by hand; they are not counted as the consumer's.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | A slice writes fewer lines with either shape | measured, lines that are neither blank, a comment nor a `using`, the side-by-side slice's two aliases counted: baseline 75, copy 58, side by side 58. The fault set goes from 22 to 17 (copy) or 15, the service from 36 to 24 in both; the endpoints stay at 16. The difference is the copied members and the re-keying table, the pilot's 45 lines with their blank lines and comments |
| 2 | A shared refusal reads alike on every route | measured, side by side only: `"fault":"ConcurrencyFault.Conflict"` from the team's create route and from any other slice. A copy answers `TeamFault.Conflict`, and another slice's copy its own name, so a client recognises the shared refusal only by the bare member name |
| 3 | The startup check covers a set side by side | measured: with `RemovalFault` unmapped the host does not start, naming `DELETE /union/teams/{id:guid}`; the check already reads every `FaultResponsesMetadata` an endpoint carries |
| 4 | The document lists every set side by side | measured, with a defect: each operation lists the statuses of all its sets. Where two sets share a status, the problem types of the second are dropped (409 lists `TeamFault.NameInUse` and `ShortNameInUse`, not `ConcurrencyFault.Conflict` nor `RemovalFault.StillReferenced`), since the transformer skips a status already listed; and the statuses follow the sets' order (`422 412`), not ascending. Both are fixes in the transformer, and the first exists today for any endpoint with two `FaultProblem` types |
| 5 | Composing narrows the statuses a route lists | measured, no: the create route lists 404 and 412 in all three shapes, from `TeamFault.NotFound` and `ConcurrencyFault.PreconditionFailed`, which it cannot answer. A route answers a subset of its sets' members, and only the route knows which; narrowing needs an API of its own |
| 6 | A slice's fault converts into the composed fault where it is returned | measured, no: `return SharedFaults.Translate(…)` and `return Fault.Of(TeamFault.NotFound)` into a `Result<Fault<TeamFault, ConcurrencyFault>>` are CS0029, since C# applies one user-defined conversion and these need two. What compiles: `Refusal.Of(TeamFault.NotFound)` (a static `Of` per set), `Result.Error<Refusal>(…)` and a cast. A slice that funnels its refusals through one helper, as the pilot's does for logging, writes it once |
| 7 | A copy needs a constructor shape the generators can call | built so: the copied member is `new(ConcurrencyFault.Conflict.Kind, ConcurrencyFault.Conflict.Description)`, which compiles only where the set declares a `(FaultKind, string?)` constructor; a set whose members carry other data could not include another. The conversion carries the detail, not the rule, outside the base package (`Fault.Naming` is internal) |
| 8 | `default` of the composed fault is refused | measured: CMTK0001 on `default` of `Fault<TeamFault, ConcurrencyFault>`, from `[RequireCustomInitialization]` alone, beside the control `default(Option<int>)`. A project reference does not load the analyzer it reaches ([dotnet/roslyn#79272](https://github.com/dotnet/roslyn/issues/79272)), so the spike passes it as an analyzer, as the tests do; without that both are silent. The message names `Fault`, not its arity |
| 9 | `AddFaultResponses` changes the host's other error responses | measured: after it, `Results.Problem` and `Results.ValidationProblem` gain `traceId`; under `UseStatusCodePages()` a bare 400 and an unknown route's 404 are `application/problem+json` where they were `text/plain`; `UseExceptionHandler()` without options, which refused to start, starts and answers a 500 as problem details. A bare `Results.BadRequest()` without status pages stays bodiless |

## What it decided

Recorded as [decision 78](../../design/adr/0078-fault-sets-compose-side-by-side.md), and built into
2.1.0 the same day: `Fault<T1, T2>` and `Fault<T1, T2, T3>` in the base package, their `ToHttpResult()`
overloads, the transformer fix of finding 4 and `WithoutFaults` for finding 5 in the ASP.NET Core
satellite. `Union/Prototype.cs` stays as measured; the shipped types add `Of(fault)`, refuse a bare
reason in `Equals`, and refuse converting a `default` fault of a class reason.
