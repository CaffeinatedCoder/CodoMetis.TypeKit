---
verified: 2026-10-29
pins:
  - external SQL-Server 2022
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: EF Core's Entity Properties page ("the SQL Server provider maps DateTime properties to datetime2(7) columns"), measured with plain EF Core on the mssql/server 2022 image
turned-on: the SQL Server round trips (SqlServerRoundTripTests, issue 81), where a DateTime value object written as UTC reads back equal, with every tick and without its kind, as a plain DateTime does
test: test/CodoMetis.TypeKit.Facts/KB054.cs
---

# KB-054. `datetime2` keeps every tick of a `DateTime`, and reads it back without its kind

EF maps a `DateTime` to `datetime2`, precision 7 unless configured, so nothing is rounded: a value with 1234567 extra
ticks reads back with all of them. A `DateTime` written as UTC reads back with `Kind` Unspecified, and equal, since
`DateTime` equality ignores the kind. A `DateTimeOffset` (`datetimeoffset`) keeps its ticks and its offset.
