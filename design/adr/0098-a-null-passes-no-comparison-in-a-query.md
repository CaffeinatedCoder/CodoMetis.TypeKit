# 0098. A nullable record-class value object compared in a query answers as SQL does, and the docs say so

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, the EF variant catalogue of [decision 95](0095-a-fix-is-tested-across-its-input-class.md)
(`QueryTranslationVariantTests`), which left one case open; [EF Core](../ef-core.md),
[decision 57](0057-hand-written-ordering-refused-in-query.md).

The comparable aspect's operators on a record class take a nullable operand and sort null first
([aspects](../aspects.md), "A record class sorts null first"), so in memory `r.Rank < rank` is true for a null
`Rank`, and so is `<=`. EF drops the operator and translates `"r"."Rank" < @rank`, which is unknown for NULL,
and the row is left out without a word. Measured with EF Core 10.0.12 on SQLite, for a record class over
`string` and one over `int`, through `UseTypeKit()` and the named converter: the query leaves out exactly the
null rows memory keeps. A record struct is unaffected: its lifted operators give
false for null, in memory and in SQL alike.

**Decision.** The query keeps SQL's answer, and the EF README's "Good to know" and `docs/ef-core.md` say that a
null passes no `<`, `<=`, `>` or `>=` in the database, and how to keep those rows
(`r.Rank == null || r.Rank < rank`). Not refused, because:

- **SQL's answer is the underlying type's.** The same query over an `int?` column, whose lifted `<` is false
  for null, answers the same rows (the catalogue's differential). The departure is from the
  generated operators alone, where decision 57 refused an ordering the database cannot run at all, whose every
  row could be the opposite.
- **A guard cannot see it.** A nullable record-class property and a required one have the same type in the
  expression tree, so refusing by type would reject `r.Rank < rank` on every required column too. Reading
  nullability from the model still misses a parameter that is null at run time, since the interceptor sees one
  tree per query shape.
- **A refusal would leave no plain way to write the query.** `r.Rank.Value < rank.Value` throws in memory for
  a null `Rank`, and 2.2.0 takes guards, not breaks of queries 2.1.0 answers
  ([decision 97](0097-2-2-0-is-a-consolidation-release-and-the-gate-binds.md)).
- **Rewriting to memory's answer** (`IS NULL OR <`) would change rows silently, and depart from the SQL of the
  underlying type's column, which the docs promise ("re-typed, not cast").

Held by `QueryTranslationVariantTests` (`NullsLeftOut`): the query answers as the same query over the
underlying type, and memory's rows are the query's and the null rows. Seeded both ways on 2026-10-15: the
generated operators sorting null last, and an interceptor rewriting `<` to keep null rows, each fail all six
cases. The catalogue gained `<=` and a record class over `int` for it.
