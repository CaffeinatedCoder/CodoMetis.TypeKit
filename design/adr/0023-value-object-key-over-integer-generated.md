# 0023. A value-object key over an integer is generated on add; one over a `Guid` is assigned

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, [EF Core](../ef-core.md).

Parity with the wrapped key where only the database can make one, and the
application's `OrderId.New()` where it can.
