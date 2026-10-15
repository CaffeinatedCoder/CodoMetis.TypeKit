# 0066. Two more fault kinds: `PreconditionFailed` (412) and `Malformed` (400)

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a review of an application moving its own fault responses onto the package;
extends [decision 49](0049-fault-responses-client-can-act.md).

The five kinds left two refusals an API answers every day without a kind, so each needed a mapping
of the host's own that every other member then had to repeat with `FaultResponse.For(fault.Kind)`:

- **`PreconditionFailed`, 412.** A condition the request stated about the current state does not
  hold, such as the version it read (`If-Match`): the caller reads again before deciding again.
  `Conflict` stays the refusal the caller did not state a condition for, a race it may retry or a step
  the state forbids, so a stale version is one kind or the other by whether the request named it.
- **`Malformed`, 400.** The request cannot be read as what it should be: a page token, a cursor or a
  filter expression that does not parse, found by the application after binding. `Invalid` stays a
  well-formed request a rule refuses, 422, as RFC 9110 has it; a host that answers its rule refusals
  with 400 maps them so, since a host-wide table is still not offered.

Appended to `FaultKind` after `Unavailable`, so no value moves; adding members is additive.
`FaultResponse.For` and `AddFaultResponses<TSet>()` answer both with no mapping.
