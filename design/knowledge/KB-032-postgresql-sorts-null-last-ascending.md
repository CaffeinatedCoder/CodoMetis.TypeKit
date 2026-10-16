---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: https://www.postgresql.org/docs/17/queries-order.html ("By default, null values sort as if larger than any non-null value; that is, NULLS FIRST is the default for DESC order, and NULLS LAST otherwise."), measured on the postgres 17 image
turned-on: a docs gap of the analyst's run of 2026-10-16, where a nullable value object or closed set sorts null first in memory and on SQLite and last on PostgreSQL; decision 98 covers comparisons, not ordering
test: test/CodoMetis.TypeKit.Facts/KB032.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-032. PostgreSQL sorts NULL last ascending and first descending

An `int?` column holding `null`, `1`, `3` and `2`:

- `OrderBy(r => r.Rank)` gives `[1, 2, 3, null]`;
- `OrderByDescending(r => r.Rank)` gives `[null, 3, 2, 1]`;
- EF writes no `NULLS FIRST` or `NULLS LAST`.

LINQ to Objects puts `null` first ascending, and so did SQLite in the analyst's run. So the same `OrderBy` answers in another order on
PostgreSQL, for a plain nullable type and a nullable value object alike
([decision 98](../adr/0098-a-null-passes-no-comparison-in-a-query.md) is about comparisons only).
