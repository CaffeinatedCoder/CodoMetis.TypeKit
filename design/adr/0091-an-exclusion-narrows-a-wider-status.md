# 0091. An endpoint's exclusion narrows a wider scope's status; a status every endpoint leaves out is refused

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a review of the unreleased 2.1.0; settles how [decision 61](0061-scope-s-fault-status-applies-where-fault-type-answered.md)
(a scope's status applies where the fault type is answered) meets [decision 78](0078-fault-sets-compose-side-by-side.md)
(a member given a status and left out is refused).

**What broke.** The check judged each endpoint alone: a group's `WithFaultStatus(NotFound, 410)` started until one
endpoint inside called `WithoutFaults(NotFound)`, and then stopped the host, though the group's other endpoints
still answered `NotFound` with 410.

**Decision.** A status is judged against its scope, the endpoints that carry its declaration and answer its fault
type. Where every one of them leaves the member out, the status serves nothing and the host does not start, as
before: the endpoint declaring both, or a group whose endpoints all leave it out. Where some still answer the
member, the ones that leave it out narrow the status, as they narrow the host's mapping: no status and no problem
type for it in their document, and reporting it throws there.
