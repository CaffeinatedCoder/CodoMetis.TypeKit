---
verified: 2026-10-29
pins:
  - external SQL-Server 2022
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: https://learn.microsoft.com/sql/t-sql/functions/max-transact-sql (MAX "can be used with numeric, char, nchar, varchar, nvarchar, uniqueidentifier, or datetime columns, but not with bit columns"), measured with plain EF Core on the mssql/server 2022 image
turned-on: the SQL Server query catalogue's bool cases (SqlServerQueryVariantTests, issue 81), where a Max or Min of a bool value object is refused for the wrapped type too
test: test/CodoMetis.TypeKit.Facts/KB052.cs
---

# KB-052. SQL Server takes neither `MAX` nor `MIN` of a `bit`

EF translates `Max(f => f.Flag)` and `Min` of a `bool` column to `MAX` and `MIN` of a `bit`, and SQL Server refuses
them: a `SqlException`, "Operand data type bit is invalid for max operator" (`min` for `MIN`).
