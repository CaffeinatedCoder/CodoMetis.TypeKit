---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: measured with Npgsql 10.0.3, which Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 brings, on the postgres 17 image (https://www.npgsql.org/doc/types/datetime.html states microsecond resolution, not truncation against rounding)
turned-on: claims E03-column-round-trip and L04-round-trip under a real provider, where a value written does not read back equal below a microsecond, for the plain type and the value object alike (the analyst's consumer of 2026-10-16, Q12, Q15, Q17)
test: test/CodoMetis.TypeKit.Facts/KB029.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-029. Npgsql truncates ticks below a microsecond, for every date and time type

PostgreSQL stores `timestamp`, `timestamptz`, `time` and `interval` to the microsecond, ten ticks. Npgsql
drops the ticks below it when it writes, and does not round them:

- a `DateTime` of `…07.1234567Z` reads back as `…07.1234560Z` (rounding would give `…07.1234570Z`), still
  `Kind` Utc;
- a `DateTimeOffset`, a `TimeOnly` and a `TimeSpan` (26 hours and 3 ticks) lose their sub-microsecond ticks
  the same way.

So a round trip through PostgreSQL is equal only at microsecond resolution, for a plain type and a value object
over it alike.
