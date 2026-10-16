---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: https://www.postgresql.org/docs/17/datatype-numeric.html §8.1.2 ("PostgreSQL treats NaN values as equal, and greater than all non-NaN values"), the same for floating point in §8.1.3, measured on the postgres 17 image
turned-on: none yet, until WS6's real-database checks turn it on; measured by the analyst's PostgreSQL consumer of 2026-10-16 (Q10, Q10b, Q11) where Ratio.Value > 5 kept the NaN row for the value object and its plain twin alike, which in memory is false
test: test/CodoMetis.TypeKit.Facts/KB028.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-028. NaN is greater than every number in PostgreSQL, and sorts after Infinity

A `double` column holding `NaN`, `1.5`, `10` and `Infinity`:

- `Where(r => r.R > 5)` answers the `NaN` row with `10` and `Infinity`, where in .NET every comparison with
  `NaN` is false;
- `OrderBy(r => r.R)` gives `[1.5, 10, Infinity, NaN]`.

So a query over a floating-point or `numeric` column answers rows memory would not, for a plain type and a value
object over it alike. Nothing in TypeKit turns on it yet; WS6's real-database checks will.
