# 0111. A mapping can withhold a fault's name from its problem

Date: 2026-10-28
Status: accepted

Context: 2026-10-28, issue #85, the 2.3.0 roadmap item that [decision 64](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md)
named: an additive opt-in that withholds the `fault` name for chosen members or a whole fault type, so two
causes can share a status without a mapping at the boundary.

## Decision

**The opt-in is part of the response a mapping gives a member**: `FaultResponse.WithoutFaultName()`, beside
`WithType` and `WithTitle`, and `FaultResponse.NamesFault` reads it. A mapping already answers member by
member, so chosen members are `fault => fault is SignInFault.Locked ? 403 : FaultResponse.Status(401).WithoutFaultName()`
and a whole type is the same call for every member. An endpoint's `WithFaultStatus` changes the status and
keeps the choice; there is no endpoint-level switch, since what a client may know about a fault is the host's
decision about the fault, not about a route.

**A withheld member's problem names nothing of the member.** It has no `fault` and no `rule`, the rule naming
the cause as surely as the member does. Its `detail` is the occurrence's alone (`Fault<TFault>.Detail`): a fault
set member's description, which otherwise stands in for a missing detail, is per member, and is left out with
the name. So two withheld members with the same status, `type` and `title` write the same problem but for the
detail the application wrote at the occurrence. A `type` or `title` the mapping gives one and not the other
tells them apart, and is the host's to give.

**The host still sees the fault.** `IFaultFeature` carries the name and the rule as before, and gains
`NameWithheld`, a default member, so a `CustomizeProblemDetails` that copies the name into the problem can see
it would undo the mapping.

**The document says it.** The problem schema is shared and its `fault` stays optional; a response whose every
fault, across the fault types the endpoint answers with, withholds its name has " The problem names no fault."
at the end of its description. A status a named member shares keeps its description. The startup check that
warns of two fault types writing one name counts only the members that write it.

Rejected: a separate registration (`WithholdFaultNames<TFault>()`), which a second place to read would make a
mapping's response depend on; and an attribute on the fault's member, which puts an HTTP decision into the
domain that declares the fault, as decision 64 rejected.
