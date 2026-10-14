# 0057. A query that orders by a hand-written `CompareTo` is refused

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the second outside review of 2.0.0; [EF Core](../ef-core.md),
[decision 28](0028-seams-declared-everything-else-refused.md).

A value object may declare `CompareTo(TSelf)`, and the generated operators follow it. EF drops the
operator method and translates `<`, `OrderBy` and `Max` to the column's order, so a reversed
`Priority` answered the opposite of memory (measured on SQLite). Nothing can translate an arbitrary
C# ordering, so the query is refused rather than answered: the comparable aspect marks a type whose
ordering is hand-written with `[HandWrittenOrdering]` (`CodoMetis.TypeKit.CompilerServices`,
hidden; written by hand it is CMTK1011), and `UseTypeKit()` registers an `IQueryExpressionInterceptor`
that refuses the comparison operators, `CompareTo`/`Compare`, `OrderBy`/`ThenBy` and their descending
forms, `Order`, `MinBy`/`MaxBy` and `Min`/`Max` on such a type, nullable included, naming the value
object and saying to order by `.Value` where the column's order is meant. The interceptor sees the
LINQ tree before translation with the types intact (measured).
