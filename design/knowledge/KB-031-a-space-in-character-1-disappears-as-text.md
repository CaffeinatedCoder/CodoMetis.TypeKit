---
verified: 2026-10-16
pins:
  - external PostgreSQL 17.11
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: https://www.postgresql.org/docs/17/datatype-character.html ("Trailing spaces are removed when converting a character value to one of the other string types."), measured on the postgres 17 image
turned-on: none yet, measured by the analyst's PostgreSQL consumer of 2026-10-16 (K06, where "[" + x.Id + "]" in a projection gave "[]" for a space, the value object and its plain twin alike; Q20 read the column back as the space, and K06b found the row by it)
test: test/CodoMetis.TypeKit.Facts/KB031.cs (runs only with TYPEKIT_PG set, until WS6's CI service)
---

# KB-031. A space in `character(1)` disappears when SQL converts it to text

Npgsql maps a `char` to `character(1)`. A row holding `' '` reads back as `' '`, but where SQL converts the
column to text, as EF's translation of `"[" + l.C + "]"` does, the trailing space is removed and the projection
is `"[]"`.
