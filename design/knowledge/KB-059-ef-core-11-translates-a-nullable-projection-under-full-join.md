---
verified: 2026-10-31
pins:
  - external Microsoft.NETCore.App.Ref 11.0.0-rc.3.26522.148 and Microsoft.EntityFrameworkCore.Sqlite 11.0.0-rc.3.26522.148
source: the join surface compiled against the 11.0.0-rc.3 reference assemblies by OuterJoinDefaultTests, and reflected over on the 11.0.0-rc.1 runtime; the translation measured with EF Core 11.0.0-rc.3 on SQLite in a `net11.0` console built by the 11.0.100-rc.1 SDK and run on the 11.0.0-rc.1 runtime, the only ones installed (`ToQueryString` and the rows read back); dotnet/runtime#127236 and "What's new in .NET 11 libraries"
turned-on: CMTK0009's outer joins (#261), which overloads it resolves, and the nullable projection its message and the Analyzers README name for a query
test: none, the repository's SDK cannot target net11.0 to run EF Core 11; the join surface is held by OuterJoinDefaultTests, which compiles against the 11.0.0-rc.3 reference assemblies
---

# KB-059. EF Core 11 translates a nullable projection under `FULL JOIN`

- **.NET 11 adds `FullJoin` on `Enumerable`, `Queryable` and `AsyncEnumerable`.** With a result selector,
  `FullJoin<TOuter, TInner, TKey, TResult>(outer, inner, outerKeySelector, innerKeySelector, resultSelector,
  comparer = null)`; `AsyncEnumerable` also has the form whose selectors take a `CancellationToken` and return a
  `ValueTask`. The type parameters are `TOuter` and `TInner` and the sequences `outer` and `inner`, as in .NET 10's
  `LeftJoin` and `RightJoin`.
- **.NET 11 also adds outer joins that return tuples.** `LeftJoin`, `RightJoin` and `FullJoin<TOuter, TInner, TKey>`
  take no result selector and return `(TOuter Outer, TInner Inner)`, on all three hosts, with an optional comparer.
  The side with no match is `default` in the tuple as it is in the result selector.
- **EF Core 11 translates `FullJoin` to `FULL JOIN`, and a nullable projection with it.** Over a struct column with a
  value converter, `db.Lefts.Select(l => (Code?)l.Code).FullJoin(db.Rights.Select(r => (Code?)r.Code), l => l, r => r,
  (l, r) => new { l, r })` is `SELECT "l"."Code" AS "l", "r"."Code" AS "r" FROM "Lefts" AS "l" FULL JOIN "Rights" AS
  "r" ON "l"."Code" = "r"."Code"`, and over the lefts 10, 20 and the rights 20, 30 reads back `10 | null`,
  `20 | 20`, `null | 30`. The same projection under `LeftJoin` is a `LEFT JOIN` with `10 | null`, `20 | 20`.
- **Without the projection, EF throws for the side with no match.** The same `FullJoin` over `l.Code` and `r.Code`
  translates to the same SQL and throws `InvalidOperationException: Nullable object must have a value.` when it reads
  the rows. LINQ to objects gives `default` instead, which is what CMTK0009 reports.

Checked on SQLite alone; SQL Server and PostgreSQL were not run.
