# 0124. An operator in a query is the wrapped type's arithmetic

Date: 2026-10-30
Status: accepted

Context: [#186](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/186), under the 2.4.0 epic
[#177](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/177); [EF Core](../ef-core.md); refines
[decision 122](0122-arithmetic-operators-are-opt-in-and-a-hand-written-one-is-a-seam.md), whose EF Core bullet it
replaces, on [decision 123](0123-a-hand-written-operator-is-refused-in-a-query.md)'s mark.

EF translates an operator node as the database's arithmetic over its operands, and never calls the operator's
method. Between two value objects into one, that is what a generated operator does where TypeKit's converter
stores the column: `o.Price + o.Tax` is `ef_add("p"."Price", "p"."Tax")` on SQLite. Where the application's own
converter stores it, as `"M:" + value`, it added the texts: `Price + Tax > 4` became
`"p"."Price" + "p"."Tax" > 'M:4'` and kept no row where memory keeps two, and `-Price < -4` kept all three. With
another operand or result EF applied the value object's converter to the other side: `o.Price * 2m > limit` threw
"Invalid cast from 'System.Decimal' to 'ProbeMoney'", `o.Tax / o.Price` as a `decimal` "No coercion operator is
defined", and unary `+` was not translated (measured with EF Core 10.0.12 on SQLite).

**Every generated operator, marked `[TranslatedAsWrappedArithmetic]`, is rewritten to the wrapped type's
arithmetic over what the value objects wrap, before EF translates the query.**

- **The rewrite.** The interceptor and the filter convention of decision 123 rewrite a marked operator to the
  wrapped type's operator over `ValueObjectConverter<,>.ProviderValue` of each value-object operand, the other
  operand as it is. `ProviderValue` translates only where TypeKit's converter stores the column
  ([decision 56](0056-value-translated-only-through-typekit-converter.md)), so an operator over a column the
  application converts itself is EF's "could not be translated"; as a query's last `Select`, EF computes it in
  memory. A result of the value object's type is `ValueObjectConverter<,>.Materialize` of the wrapped type's, which
  the method translator re-types as the value object, with its mapping for the same store type, so it compares with
  and is read as one. Both methods are reached through `GeneratedValueObjectAttribute.Accept`, never by name, and
  are public, so a compiled model writes a rewritten filter out. Unary `+` of a number is its operand.
- **A `Nullable` operand is checked, as the lifted operator is.** The rewrite of an operator with an optional
  operand is null unless every optional operand is there, and an operator over such a rewrite takes its check over,
  so `o.Optional * 2m + o.Tax` checks `Optional` once: `CASE WHEN "p"."Optional" IS NOT NULL THEN
  ef_add(ef_multiply("p"."Optional", '2.0'), "p"."Tax") END` on SQLite. Unwrapped by conversions alone, which EF drops
  from the SQL, the null reached the arithmetic where EF computes the last `Select` in memory: over a column the
  application's own converter stores, `Select(o => o.Amount * 2m)` threw "Nullable object must have a value" on a
  null row (an outside review of #190).
- **Re-typed, not cast.** The database's arithmetic, and `.Value` of it, is re-typed as a column is: cast to the
  column's store type, SQL Server's default `decimal(18,2)`, `(o.Price / 32m).Value` over 5 read 0.16 where memory
  reads 0.15625, and a filter on it lost the row (measured on Azure SQL Edge). SQLite's decimal arithmetic is
  functions, `ef_add`, `ef_multiply`, `ef_divide`, `ef_mod` and `ef_negate`, re-typed by those names. So is what
  holds such a value without computing one, branch by branch: a `CASE` (`?:`, and the null check above), a
  `COALESCE` (`??`), `MAX` and `MIN`, and a subquery of one value. Cast whole, `(o.Optional / 32m ?? o.Price /
  32m).Value` read 0.09 where memory reads 0.09375, a filter on it lost its row, and arithmetic over a `?:` and a
  navigation's largest quotient rounded the same (an outside review of #190, measured on Azure SQL Edge). A branch of
  any other shape, such as a parameter, is converted alone, and any other function's result is converted. Each
  branch's own converter is checked, since EF gives a `CASE` the first branch's mapping.
- **A wrapped type without an expression of its own.** `byte`, `short`, `sbyte`, `ushort` and `char` have no
  arithmetic in an expression tree, where C# computes it as an `int`'s. Their operators are refused naming the value
  object, between two value objects too, which no release translated (#184 is 2.4's).

`ArithmeticQueryTests` holds it as a differential oracle: 45 queries (filters, projections, orderings and an
aggregate; a column, a nullable column, a JSON property, a navigation, a captured factor, a decimal column and an
optional one as the other operand; `checked`, unary `+` and `-`; past the column's scale, through `?:`, `??`,
`ValueOrNull()` and a navigation's `Max`) against the same queries over the objects
in memory, on SQLite, PostgreSQL 17 and SQL Server. `ArithmeticQueryFilterTests` holds the filters, named and not,
and `ForeignConvertedArithmeticTests` a converter of the application's own, optional operands computed in memory
included.
