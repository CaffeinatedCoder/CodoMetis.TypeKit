# 0049. Fault responses a client can act on

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13, spikes/FaultResponses.

The five
parts ship as proposed, with these choices measured or made on the way:
- **A kind's status is fixed**: `NotFound` 404, `Conflict` 409, `Invalid` 422 (RFC 9110
  §15.5.21, a well-formed request a rule refuses), `Forbidden` 403, `Unavailable` 503. No
  host-wide table: a mapping of the host's own, calling `FaultResponse.For(kind)` for the members
  it keeps, is the escape hatch, as [decision 38](0038-result-becomes-response-explicit-tohttpresult-faults.md)'s switch is. `FaultKind` starts at 1, so a
  `default` kind is refused where the set is mapped.
- **`Fault<TFault>`'s fault is its `Reason`**, not its `Kind` as first proposed: a member's
  `FaultKind` is its `Kind`, and `fault.Kind.Kind` would have read as a stutter. A blank detail is
  refused; `null` says there is none.
- **A description is the `detail`**: RFC 9457 §3.1.4 has `detail` help the client correct the
  problem, and §3.1.3 keeps `title` the same for every occurrence of a type, which a status's own
  title already is. An occurrence's detail replaces the member's description.
- **`IFault` is non-generic**, beside `IFaultSet<TSelf>`, so the satellite reads a member's kind
  with an interface check whatever the fault type, and `Description` defaults to `null`.
- **Overloads by constraint live in two classes**, since C# forbids two methods that differ in
  constraints alone; with the type arguments inferred, the one whose constraint fails drops out.
- **The document lists problem types in the response's description**, and `fault` stays a string
  on the shared problem schema; a per-operation enum is not offered.
- **An MVC action names its member in an attribute**, `[FaultStatus<OrderFault>(nameof(…), 422)]`,
  since an attribute takes only constants; a name no member has stops the host.
