# 0122. A hand-written operator is refused in a query, and a generated one is marked

Date: 2026-10-30
Status: accepted

Context: [#187](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/187), under the 2.4.0 epic
[#177](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/177); [EF Core](../ef-core.md); refines
[decision 121](0121-arithmetic-operators-are-opt-in-and-a-hand-written-one-is-a-seam.md), beside
[decision 57](0057-hand-written-ordering-refused-in-query.md).

EF translates an operator node as the database's arithmetic over its operands, and never calls the operator's
method. A hand-written operator therefore ran as the database's arithmetic, not as written: with its `+` rounding
to cents, `Price + Tax > 1.006` over 1.005 and 0.001 became `ef_compare(ef_add("l"."Price", "l"."Tax"), @limit) > 0`
on SQLite and kept no row where memory keeps the one, and `Select(l => l.Price + l.Tax)` read 1.006 where memory
reads 1.01 (measured with EF Core 10.0.12, the refusal taken out). Decision 121 keeps such an operator as a seam, so
the build cannot stop it; the query must.

**Any arithmetic, bitwise or shift operator a value object declares without `[TranslatedAsWrappedArithmetic]` is
refused in a query, naming the value object and `.Value`.**

- **The mark.** The arithmetic aspect puts `[TranslatedAsWrappedArithmetic]` (new, `[EditorBrowsable(Never)]`, in
  `CodoMetis.TypeKit.CompilerServices`) on every operator it generates, which is the wrapped type's arithmetic over
  the wrapped values. An operator without it was written by hand, whatever its interface: a 2.3 operator, one kept
  as a seam, one without an interface, a validated value object's. Not `[CompilerGenerated]`, which another source
  generator may put on an operator that does more, and not the interface, which an operator written without one does
  not declare.
- **Where.** An `IQueryExpressionInterceptor` that `UseTypeKit()` registers, before translation, `Queryable`'s and
  `Enumerable`'s over a navigation alike, as decision 57's; and a finalizing convention for query filters, named or
  not, which fails when the model is built. An operator over the context's own values in a filter runs in memory,
  as written, and stays allowed.
- **Which.** `+`, `-`, `*`, `/`, `%`, unary `-` and `+`, their checked forms, `&`, `|`, `^`, `<<`, `>>`, `~` and
  `!`: the operators EF translates as the database's own. Comparison is decision 57's, equality the record's. A
  conversion operator is left to EF: `implicit operator string(Name n) => n.Value` is common, and translates to what
  it does.
- **A refusal in a minor.** A query with a hand-written operator that ran before now throws. It did not return what
  the code says. Decision 34 holds the build's diagnostics, not a query's answer, and a silent wrong answer is a
  defect, fixed in the release that finds it, as the refusal of a hand-written `CompareTo` was.

`HandWrittenArithmeticTests` holds it: `+`, `*` without an interface, unary `-`, `|`, `~` and a validated value
object's `+`, in a filter, a projection, a checked context, `OrderBy`, over an optional operand and a navigation,
and in a query filter, named or not; the generated `-` beside a hand-written `+`, `.Value` arithmetic, and an
operator over captured values stay allowed. `ArithmeticTests` holds the mark on every generated operator and on no
hand-written one.
