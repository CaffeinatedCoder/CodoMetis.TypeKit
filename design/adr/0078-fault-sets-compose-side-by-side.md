# 0078. Fault sets compose side by side, and a route leaves out what it cannot answer

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, [spikes/FaultSetComposition](../../spikes/FaultSetComposition/README.md), answering
[decision 68](0068-fault-set-composition-spiked-first.md); a pilot of `ToHttpResult()` on one slice of
an application, built against `feat/2.1.0` at 543e382, which came out 8 lines shorter than the slice
it replaced. 7 of its fault set's 11 members, and a 15-line table re-keying shared string codes onto
them, existed only because a set could not use another's members.

**A fault of one of several sets.** The base package gains `Fault<T1, T2>` and `Fault<T1, T2, T3>`:
one `Fault<Tn>` of one of the sets, its reason, detail and rule intact. Each converts implicitly from
every `Fault<Tn>` and has a static `Of(reason, detail)` and `Of(fault)` per set; `Match` takes one
delegate per set, each given the `Fault<Tn>`. A bare reason converts to nothing, and `Equals` with one
is a compile error, as for `Fault<TFault>` ([decision 53](0053-fault-made-by-fault-of-alone.md)). It
carries `[RequireCustomInitialization]` (CMTK0001) and refuses System.Text.Json as every exported struct
does, and has no public content. A `default` one holds no fault, and `Match` throws for it; a `default`
`Fault<TFault>` of a class reason holds no reason and does not convert, while one over an enum holds the
member valued 0, which nothing tells from a real one. A slice keeps its own set and lists the shared
ones beside it, `Result<Fault<TeamFault, ConcurrencyFault>>`, and the shared translators return
`Fault<ConcurrencyFault>` directly. `ToHttpResult()` answers it as
`Results<NoContent, FaultProblem<TeamFault>, FaultProblem<ConcurrencyFault>>`, with a success of the
endpoint's choosing too, so `FaultProblem`, the mapping and the startup check are unchanged: each set
is mapped once for the host, and the check already reads every fault type an endpoint answers with.
Three sets is what the pilot needs; more is additive, up to the five ASP.NET's `Results` leaves beside
a success.

What the spike measured: a slice of 58 lines either way against 75 today, and a shared refusal answered
as `ConcurrencyFault.Conflict` from every route. What it costs: C# applies one user-defined conversion
where these need two, so `return Fault.Of(TeamFault.NotFound)` into the composed result is CS0029 and
is written `Refusal.Of(TeamFault.NotFound)` (with `using Refusal = Fault<TeamFault, ConcurrencyFault>;`),
and a shared fault is converted where it is returned. C# 15 unions are this shape in the language; 3.0.0
weighs them against these types rather than retiring a generator feature ([decision 44](0044-2-0-0-net10-0-metalama.md)).

**Names a route answers with stay apart.** An endpoint whose sets write one `fault` name for two faults
would answer them alike: with bare names, the default, where two sets share a member name,
`TeamFault.Conflict` beside `ConcurrencyFault.Conflict`; with `QualifiedFaultNames`, where two sets of one
nesting chain live in two namespaces, since the name carries none. The startup check refuses it and
names both; a rename, the other form, or leaving one out of the route answers it.
Before this, an endpoint answered two fault types only by taking a `FaultProblem` out of one
`ToHttpResult()` with a cast, which nothing documented, so no host is expected to stop.

**The document lists every set's problem types, statuses ascending.** The transformer skipped a status
an earlier fault type had listed, so at a status two sets share the second set's problem types were
missing; it already did for any endpoint answering two `FaultProblem` types. Fixed with this.

**A route leaves out what it cannot answer.** Composing does not narrow: a create route still lists 404
and 412 from members it never reports. `.WithoutFaults(TeamFault.NotFound)` on an endpoint or a group,
and `[WithoutFaults<TeamFault>(nameof(TeamFault.NotFound))]` on an MVC action or controller, take
members out of that route's document; a status no member is left at goes. The startup check refuses a
scope none of whose endpoints answers with the set, a value or a name no member has, and a member also
given a status, as it refuses those for a `WithFaultStatus`. Reporting
a left-out member is refused at run time with an `InvalidOperationException` naming the endpoint and
the member, as an unmapped value is, rather than answered with a status the document does not list.
Not measured by the spike; `FaultExclusionTests` are the measurement.

**Rejected.** *Copying an included set's members* (decision 68's first shape): the same lines, but a
shared refusal gets one name per slice on the wire, the generators can only call a constructor shape
they fix, and the conversion from the included set is a member per set. *Waiting for C# 15 unions*:
3.0.0, no earlier than November 2027, while the pilot's conversion of every slice waits on this. *A
positive list per route*, `WithFaults(…)`: every route writes in full what narrowing writes only where a
route answers less. *Answering a left-out member anyway*: the document would then be wrong silently.

All of it is additive and joins 2.1.0.
