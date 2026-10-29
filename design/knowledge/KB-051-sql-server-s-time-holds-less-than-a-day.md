---
verified: 2026-10-29
pins:
  - external SQL-Server 2022
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: https://learn.microsoft.com/sql/t-sql/data-types/time-transact-sql (its range, 00:00:00.0000000 through 23:59:59.9999999), measured with plain EF Core on the mssql/server 2022 image, the column type, the refusal and the JSON read
turned-on: the SQL Server query catalogue's TimeSpan cases (SqlServerQueryVariantTests, issue 81), whose 1.02:00:00 is refused in a column and read as 01:02:00 from a JSON column, and the EF README's TimeSpan warning for SQL Server
test: test/CodoMetis.TypeKit.Facts/KB051.cs
---

# KB-051. SQL Server's `time` holds less than a day, so a longer `TimeSpan` is refused in a column and misread in JSON

EF maps a `TimeSpan` to `time`. `SaveChanges` of `TimeSpan.FromHours(26)` into such a column throws a
`DbUpdateException`. In a JSON column (`OwnsOne(…).ToJson()`) the same value is written as its text, `"1.02:00:00"`;
an entity read whole has `1.02:00:00`, but a query that reads the property (`Select(h => h.Doc.Length)`) converts
the text to `time` and answers `01:02:00`, without an error.
