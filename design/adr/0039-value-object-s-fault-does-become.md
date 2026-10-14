# 0039. A value object's fault does not become a validation error

Date: 2026-10-11
Status: accepted

Context: 2026-10-11, phase 12,
spikes/ValidationFaults.

ASP.NET Core 10's validation never sees a value that failed to bind, a
refused route or query value has lost its fault, and MVC already reports a refused body with the
converter's message. What remained, a minimal-API body, needs `ThrowOnBadRequest` for the whole
host, an exception handler in its pipeline, and the fault carried as data by a new public type in
the base package: too much host-wide change for one of four positions. Reopen it if ASP.NET hands
a binding failure to a hook without `ThrowOnBadRequest`.
