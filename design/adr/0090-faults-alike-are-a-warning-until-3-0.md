# 0090. Two fault types writing one name are a startup warning until 3.0.0

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a review of the unreleased 2.1.0; amends the refusal
[decision 78](0078-fault-sets-compose-side-by-side.md) added for faults of several types.

**What broke.** 2.1.0's startup check refused a host with an endpoint whose fault types write one name for two
members, `NotFound` of two types. 2.0.0 had no such check, and an endpoint written with 2.0's types alone,
`Results<NoContent, FaultProblem<OrderFault>, FaultProblem<ClashingFault>>`, started and answered 404 under
2.0.0 and stopped the host under 2.1.0. Decision 78 took that composition for unused; it was public and
undocumented, which is not the same. The composed `ToHttpResult()` returns the very same `Results<…>`, so the
check cannot tell 2.1's composition from 2.0's.

**Decision.** The clash is logged as a warning at startup (category `CodoMetis.TypeKit.AspNetCore.FaultResponses`,
event `FaultsAlike`), naming the endpoint and both members, and the host starts. 3.0.0 refuses it, on the roadmap.
A client of such an endpoint cannot tell the two faults apart, but nothing it relied on changes.

Rejected: an opt-in that refuses, public API that 3.0.0 makes moot; refusing only the 2.1 composition, which
the metadata cannot tell apart.
