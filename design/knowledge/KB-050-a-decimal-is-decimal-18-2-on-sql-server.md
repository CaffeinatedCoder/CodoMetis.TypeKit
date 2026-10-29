---
verified: 2026-10-29
pins:
  - external SQL-Server 2022
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: measured with plain EF Core on the mssql/server 2022 image, the model's column type and the values read back. EF's documentation names no default precision, and SQL Server's precision page no rounding on write
turned-on: the SQL Server query catalogue's decimal cases and the key catalogue's decimal keys (SqlServerQueryVariantTests, SqlServerKeyVariantTests, issue 81), which read 10.00 where memory holds 10, and the EF README's HavePrecision guidance for SQL Server
test: test/CodoMetis.TypeKit.Facts/KB050.cs
---

# KB-050. A `decimal` is a `decimal(18,2)` on SQL Server, read back at scale 2 and rounded on write

A `decimal` property without `HasPrecision` is a `decimal(18,2)` column. Written as `10m`, `1.005m` and
`-0.004m`, the values read back as `10.00m`, `1.01m` and `0.00m`: every value at scale 2, which is equal to
`10m` but prints `"10.00"`, and more fractional digits rounded half away from zero, without an error.
