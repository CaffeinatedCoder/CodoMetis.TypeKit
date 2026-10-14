# 0038. A `Result` becomes a response through an explicit `ToHttpResult()`, its faults mapped in the host

Date: 2026-10-11
Status: accepted

Context: 2026-10-11, phase 12, spikes/ResultHttp.

The return type is ASP.NET's
`Results<Ok<T>, FaultProblem<TFault>>`, so ASP.NET describes the success and the Request Delegate
Generator needs nothing of ours; a handler returning the `Result` itself would document a `Result`.
The fault type is an **enum**: its members are what a mapping can be checked against when it is
registered, and what the document lists. A fault of another shape is `MapError`ed to one at the
endpoint. The extensions live beside the framework's (`ToHttpResult` in `Microsoft.AspNetCore.Http`,
`AddFaultResponses` in `Microsoft.Extensions.DependencyInjection`, [decision 22](0022-registration-extensions-live-framework-s-namespaces.md)). A missing mapping
stops the host from starting (a startup filter, which comes with the first `AddFaultResponses`), a
value no member names is refused rather than answered, and the README's mapping ends in a throwing
discard arm: the registration calls it for every member, so a missed member still fails at startup,
and without the arm C# reports CS8524.
