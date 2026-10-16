---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: https://www.npgsql.org/doc/types/datetime.html (infinity is mapped to the MinValue and MaxValue of the corresponding .NET types), the Kind read back measured with Npgsql 10.0.3 on the postgres 17 image, which the page does not state
turned-on: none yet, until WS6's real-database checks turn it on; measured by the analyst's PostgreSQL consumer of 2026-10-16 (W05), for the value object and its plain twin alike
test: test/CodoMetis.TypeKit.Facts/KB030.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-030. `DateTime.MaxValue` written as UTC reads back with `Kind` Unspecified

`DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)` written to a `timestamptz` column is stored as
`infinity`, and reads back as `DateTime.MaxValue` with `Kind` `Unspecified`, not `Utc`. The value compares
equal; the kind does not survive.
