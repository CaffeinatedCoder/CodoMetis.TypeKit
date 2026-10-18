---
verified: 2026-10-17
pins:
  - Microsoft.EntityFrameworkCore.Sqlite 10.0.12
  - Microsoft.EntityFrameworkCore.Relational 10.0.12
source: the Query limitations section of SQLite Database Provider - Limitations (learn.microsoft.com, dotnet/EntityFramework.Docs providers/sqlite/limitations.md), which says "SQLite doesn't natively support the following data types. EF Core can read and write values of these types, and querying for equality (`where e.Property == value`) is also supported. Other operations, however, like comparison and ordering will require evaluation on the client." and lists DateTimeOffset, decimal, TimeSpan and ulong; measured with plain EF Core and its SQLite provider, where decimal is compared and ordered
turned-on: 12 rows of E02ValueTranslatesProbe (claim E02-value-translates-to-column), a value object over DateTimeOffset, TimeSpan or ulong under where-value-compare or order-by, where both sides refused the query (WS2 step 4, the EF family); E02's obligations declare those pairs inapplicable
test: test/CodoMetis.TypeKit.Facts/KB039.cs
---

# KB-039. EF Core's SQLite provider neither compares nor orders a `DateTimeOffset`, a `TimeSpan` or a `ulong`

Over a plain column of one of these types, with no TypeKit:

| Query | `DateTimeOffset`, `TimeSpan`, `ulong` | `decimal` |
|---|---|---|
| `Where(r => r.P >= v)` | `InvalidOperationException`: "The LINQ expression ... could not be translated" | translated, the rows at or above `v` |
| `OrderBy(r => r.P)` | `NotSupportedException`: "SQLite does not support expressions of type '...' in ORDER BY clauses" | translated, in order |
| `Where(r => r.P == v)` | translated | translated |

EF's page lists `decimal` beside the three. EF Core 10 translates its comparisons and orderings on SQLite, so the
table above, not the page's list, is what this repository relies on.

So on SQLite a query that compares or orders such a column fails for the plain type as it does for a value object
over it, before any value-object translation is asked anything: E02's comparison and ordering rows over these types
have nothing to check there, and its obligations declare them inapplicable. PostgreSQL translates both.
