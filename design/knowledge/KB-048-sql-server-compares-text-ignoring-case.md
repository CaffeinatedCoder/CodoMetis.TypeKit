---
verified: 2026-10-29
pins:
  - external SQL-Server 2022
  - Microsoft.EntityFrameworkCore.SqlServer 10.0.12
source: https://learn.microsoft.com/sql/t-sql/language-elements/string-comparison-assignment ("Transact-SQL considers the strings 'abc' and 'abc ' to be equivalent for most comparison operations") and EF Core's Entity Properties page, whose example configures "a SQL Server column to be case-insensitive" with SQL_Latin1_General_CP1_CI_AS; measured with plain EF Core on the mssql/server 2022 image, whose databases take that collation
turned-on: the SQL Server query catalogue's text cases (SqlServerQueryVariantTests, issue 81), where a text or char value object compared, ordered or aggregated answers in another order than memory's ordinal one, and the EF README's "Ordering in SQL follows the database's collation", extended to comparison
test: test/CodoMetis.TypeKit.Facts/KB048.cs
---

# KB-048. SQL Server compares and orders text ignoring case, and trailing spaces

A `nvarchar` column holding `alpha`, `Bravo` and `charlie`, under the default collation
`SQL_Latin1_General_CP1_CI_AS`:

- `Where(n => n.Name == "BRAVO")` finds `Bravo`, and `n.Name == "alpha  "` finds `alpha`;
- `upper.Contains(n.Name)` over `["ALPHA"]` finds `alpha`, and `StartsWith("b")` finds `Bravo`;
- `OrderBy(n => n.Name)` gives `["alpha", "Bravo", "charlie"]`, where ordinal order is `["Bravo", "alpha", "charlie"]`;
- `OrderBy(n => EF.Functions.Collate(n.Name, "Latin1_General_BIN2"))` gives the ordinal order.

So a query over text matches and orders rows that ordinal comparison in memory keeps apart.
