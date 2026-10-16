---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: https://www.postgresql.org/docs/17/datatype-numeric.html §8.1.2 ("If the scale of a value to be stored is greater than the declared scale of the column, the system will round the value to the specified number of fractional digits."), measured on the postgres 17 image
turned-on: claims E03-column-round-trip and L04-round-trip under a real provider, and the EF README's HavePrecision guidance, whose facet reaching the column is what makes the rounding happen (the analyst's finding E2 of 2026-10-16, consumer Q06)
test: test/CodoMetis.TypeKit.Facts/KB034.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-034. `numeric(p,s)` rounds a value with more fractional digits on write

A `decimal` configured `HasPrecision(18, 2)` is a `numeric(18,2)` column. Written as `1.005m` and `-0.004m`, the
values read back as `1.01m` and `0.00m`: PostgreSQL rounds to the column's scale, half away from zero, and does
not refuse.
