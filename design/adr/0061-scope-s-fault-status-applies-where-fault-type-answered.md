# 0061. A scope's fault status applies where its fault type is answered

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; [decision 49](0049-fault-responses-client-can-act.md);
[OpenAPI](../openapi.md).

Decision 49 gave an endpoint `WithFaultStatus` and an MVC action `[FaultStatus<TFault>]`, and the
startup check refused a status on an endpoint that answers with no fault of its type. Both also reach
every endpoint of a scope: a route group's `WithFaultStatus`, and the attribute on a controller class.
A group or controller that held a health check beside its fault endpoints then did not start, since
the health check carried the status too (measured by the review).

A status declared on one endpoint or action stays strict: that endpoint must answer with the fault
type, or the host does not start. A status declared on a scope applies to the endpoints of that scope
that answer with its fault type and leaves the others alone. A scope is a route group, any
convention builder that covers several endpoints (the one `MapControllers()` returns), or a
controller class; a controller that derives from a base class with the attribute is a scope of its
own. The host does not start when no endpoint of the status's own scope answers with the fault type.
The unknown-member and error-status refusals are unchanged, and so is the order: the last status in
ASP.NET's metadata wins, an endpoint's own over its group's, an inner group's over an outer group's,
an action's over its controller's, and `MapControllers()`'s builder over every attribute, since
ASP.NET puts its metadata last.

Its own scope, not the host: the check exists for a status that misleads, set on the wrong group or
left behind when the endpoints that needed it moved. Satisfied by any endpoint of the host that
answers with the fault type, which some other group usually does, it would pass almost every such
status.

A declaration is one instance: `WithFaultStatus` adds one object, which the builder's convention puts
on every endpoint of the scope, and MVC reads a controller's attributes once per controller and puts
the same instances on each of its actions (measured on ASP.NET Core 10.0.12: shared across a
controller's actions and an action's two routes, separate for two controllers deriving from one
base, stable across reads of the endpoints). The check groups statuses by instance, by reference, so
the scope comes from the endpoints that carry it; an endpoint's own status is a scope of one.
`FaultStatusScopeTests` holds each case.

Rejected:
- **Strict everywhere**, the rule before: a group or a controller cannot hold a health check, a
  version endpoint or anything else that reports no fault, and a host splits its routes by what they
  return.
- **No check for a status a scope gave**: a stale status on a group is then silent, and silent is
  what this package refuses.

Not refused: a group with no endpoint carries its status nowhere, and a scope's status that each of
its matching endpoints replaces with its own is never used.
