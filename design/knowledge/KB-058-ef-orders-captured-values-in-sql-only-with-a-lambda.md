---
verified: 2026-10-31
pins:
  - Microsoft.EntityFrameworkCore.Sqlite 10.0.12
source: measured with plain EF Core on SQLite (`ToQueryString`), and with a value object whose `CompareTo` is descending in `HandWrittenOrderingTests`
turned-on: the EF satellite's refusal of an ordering in a query filter (`HandWrittenOrdering.OrderingRunsInMemory`, issue 225), which lets an ordering over the context's own values through only when it takes no lambda
test: test/CodoMetis.TypeKit.Facts/KB058.cs
---

# KB-058. EF orders captured values in SQL only when the chain takes a lambda

A LINQ chain over a collection a query captures, `values.Max()`, `values.Min()`, `values.Order().First()` or
`values.First()`, is evaluated in memory: the SQL holds one parameter with the answer. With a lambda,
`values.OrderBy(v => v).First()` or `values.OrderByDescending(v => v).First()`, EF sends the collection's elements and
orders them in SQL: for an `int[]`, one parameter per element in a `VALUES` list (EF 10's default), `ORDER BY "v"."Value"
DESC LIMIT 1`. `values.Max(v => v)` over an `int[]` throws a `NullReferenceException` inside EF. A query filter that
reads the context's own values, `HasQueryFilter(r => r.P == Values.Max())`, is keyed on the same distinction.

For a value object whose `CompareTo` is its own, the first kind orders by `CompareTo` and the second by the stored
value: over 1, 2, 3 with a descending `CompareTo`, `Values.Max()` is 1 and `Values.OrderByDescending(v => v).First()`
answered 3. `MaxBy(v => v)` over the parameter is not translated at all. One chain with a lambda,
`values.Select(v => (object)v).Max()`, was evaluated in memory too; the satellite still refuses it, since the
distinction it can rely on is the lambda.
