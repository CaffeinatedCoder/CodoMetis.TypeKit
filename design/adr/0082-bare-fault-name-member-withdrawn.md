# 0082. The bare fault name under a member of the host's is withdrawn before it ships

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the pilot of [decision 80](0080-second-pilot-one-mapping-held-fault-bare-name.md),
built against that decision's commit. It adopted the one mapping for every fault set and `ShouldHold`, and
kept its own line for the bare name rather than `FaultResponseOptions.BareFaultNameMember`.

**Withdrawn, not extended.** The member the option was for, `code`, is on every refusal the pilot's host
writes: a read route's 404, a malformed `If-Match`'s 400, a grid read's rejection and the 500, none of them
a fault, and that stays true once every slice is converted. The package described the member as present
only on a problem that answers a fault, which is false for that host, and its description reached the
document first, so the host's own was dropped. A member written on problems the package does not make is
the host's to describe, and a description the package cannot make true is worse than none. What the
option saved is one line in `CustomizeProblemDetails`, which reads the bare name from `IFaultFeature`, and
the host describes its member itself. 2.1.0 is unreleased, so removing the option breaks no one.

Rejected: *a description option*, `BareFaultNameMemberDescription`, which adds surface to an option the one
host that asked for it cannot use as described; *leaving the member undescribed when the document already
describes it*, which depends on the order the host's transformers run in, so the document would change with
a registration's position.

The rest of decision 80 stands.
