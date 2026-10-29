---
verified: 2026-10-29
pins:
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: measured with plain EF Core, no server opened, ToQueryString at EF's default level, at 150 and at 160. EF's SQL Server function-mappings page (learn.microsoft.com/ef/core/providers/sql-server/functions) lists Math.Max as GREATEST from EF Core 9.0 and names no level
turned-on: the SQL Server query catalogue's Greatest and Least cases (SqlServerQueryVariantTests, issue 81), refused at EF's default level and translated, as the wrapped type's are, at 160
test: test/CodoMetis.TypeKit.Facts/KB053.cs
---

# KB-053. EF translates `Greatest` and `Least` on SQL Server only from compatibility level 160

Under `UseSqlServer` at EF's default compatibility level, or at 150, `Where(p => EF.Functions.Greatest(p.Left, p.Right) > 2)`
is refused, "could not be translated", and `Select(p => EF.Functions.Least(p.Left, p.Right))` reads the two columns and
calls `Least` in memory, where it throws "switched to client-evaluation" when the query runs. With
`UseCompatibilityLevel(160)` both translate to `GREATEST` and `LEAST`.
