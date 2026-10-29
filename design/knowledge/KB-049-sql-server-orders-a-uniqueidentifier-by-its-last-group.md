---
verified: 2026-10-29
pins:
  - external SQL-Server 2022
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: https://learn.microsoft.com/sql/connect/ado-net/sql/compare-guid-uniqueidentifier-values (SqlGuid "implements CompareTo using SQL Server behavior, in the last six bytes of a value are most significant", where Guid "evaluates all 16 bytes"), measured with plain EF Core on the mssql/server 2022 image
turned-on: the SQL Server query catalogue's Guid cases (SqlServerQueryVariantTests, issue 81), where a Guid value object compared, ordered or aggregated answers in another order than memory's, and the catalogue's Guid values, which ascend by their first group and not by their last
test: test/CodoMetis.TypeKit.Facts/KB049.cs
---

# KB-049. SQL Server orders a `uniqueidentifier` by its last group first

Three Guids that ascend in memory by their first group, `00000001-…-000000000003`, `00000002-…-000000000001` and
`00000003-…-000000000002`, order on SQL Server by their last group: `OrderBy` answers the second, the third, then the
first, `Min` the second, and `< first` finds the other two. `Guid.CompareTo` compares the first group first, so
an ordering, a comparison or a `Min` or `Max` over Guids answers otherwise in memory.
