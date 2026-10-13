# 0016. Native AOT wherever the platform allows it

Date: 2026-09-27
Status: accepted

Context: 2026-09-27, §11.

: the run-time packages build with
`IsAotCompatible`, the woven code is published with Native AOT in the smoke test, and a path that
needs reflection or dynamic code is rewritten rather than annotated or suppressed. EF Core itself
stays experimental under Native AOT; the satellite adds nothing to what EF requires.
