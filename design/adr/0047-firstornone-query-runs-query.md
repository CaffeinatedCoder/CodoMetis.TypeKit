# 0047. `FirstOrNone` on a query runs in the query

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

The `IEnumerable<T>`
overloads were the only ones, so `db.Orders.FirstOrNone(o => o.Id == id)` read the table and ran
the predicate in memory, with no warning (reproduced: no `WHERE`, no `LIMIT`). `IQueryable<T>`
overloads take an expression and `Take(1)`, a break only for a predicate the provider cannot
translate, which now throws: a major's to make. `LastOrNone` and `LastOrError` on a query are
`[Obsolete(error: true)]`, as `Catch`'s task trap is, since a query has no last element until it
is ordered. EF's `FirstOrNoneAsync` lives in the satellite, in EF's namespace; under Native AOT
neither is precompiled (measured), which the EF README says with the call-site form to use.
