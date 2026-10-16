---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: measured on the postgres 17 image, whose databases take the collation en_US.utf8 from template1 (EF translates StartsWith to LIKE 'b%', which on SQLite is case-insensitive for ASCII)
turned-on: none yet beyond the EF README's "Ordering in SQL follows the database's collation" (the analyst's consumer of 2026-10-16, Q01, Q02, Q05, Q20c, the value object and its plain twin alike)
test: test/CodoMetis.TypeKit.Facts/KB033.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-033. `StartsWith` is case-sensitive, and ordering follows the database's collation, not ordinal order

A `text` column holding `alpha`, `Bravo`, `charlie` and `Äpfel`, in a database whose collation is
`en_US.utf8`:

- `Where(n => n.Name.StartsWith("b"))` finds nothing: `LIKE 'b%'` is case-sensitive;
- `OrderBy(n => n.Name)` gives `["alpha", "Äpfel", "Bravo", "charlie"]`;
- `OrderBy(n => EF.Functions.Collate(n.Name, "C"))` gives the ordinal `["Bravo", "alpha", "charlie", "Äpfel"]`.

So a query's text order is the server's, which ordinal comparison in memory does not reproduce.
