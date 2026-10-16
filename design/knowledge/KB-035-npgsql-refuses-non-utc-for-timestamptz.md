---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: https://www.npgsql.org/doc/types/datetime.html ("trying to send a non-UTC DateTime as timestamptz will throw an exception", and DateTimeOffset "only with Offset=0"), measured with Npgsql 10.0.3 on the postgres 17 image
turned-on: the EF converter passing DateTime and DateTimeOffset through, so that a value object over either meets this refusal exactly as the plain type does (the analyst's consumer of 2026-10-16, W01, W02, Q14b, Q15c, Q15d, the same exception for both models)
test: test/CodoMetis.TypeKit.Facts/KB035.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-035. Npgsql refuses a non-UTC `DateTime` and a non-zero-offset `DateTimeOffset` for `timestamptz`

Npgsql maps `DateTime` and `DateTimeOffset` to `timestamptz` and refuses what that column cannot hold unchanged:

- a `DateTimeOffset` with offset +02:00, saved: a `DbUpdateException` over an `ArgumentException`, "only offset
  0 (UTC) is supported";
- a `DateTime` of `Kind` `Unspecified`, saved: a `DbUpdateException` whose inner message names
  `Kind=Unspecified`;
- a `DateTime` of `Kind` `Local` as a query parameter: an `ArgumentException` naming `Kind=Local`.
