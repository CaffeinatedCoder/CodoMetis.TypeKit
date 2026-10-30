# 0123. An operator in a query is the wrapped type's arithmetic

Date: 2026-10-30
Status: accepted

Context: [#186](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/186), under the 2.4.0 epic
[#177](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/177); [EF Core](../ef-core.md); refines
[decision 121](0121-arithmetic-operators-are-opt-in-and-a-hand-written-one-is-a-seam.md), whose EF Core bullet it
replaces, on [decision 122](0122-a-hand-written-operator-is-refused-in-a-query.md)'s mark.

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

- **The rewrite.** The interceptor and the filter convention of decision 122 rewrite a marked operator to the
  wrapped type's operator over `ValueObjectConverter<,>.ProviderValue` of each value-object operand, the other
  operand as it is. `ProviderValue` translates only where TypeKit's converter stores the column
  ([decision 56](0056-value-translated-only-through-typekit-converter.md)), so an operator over a column the
  application converts itself is EF's "could not be translated"; as a query's last `Select`, EF computes it in
  memory. A result of the value object's type is `ValueObjectConverter<,>.Materialize` of the wrapped type's, which
  the method translator re-types as the value object, with its mapping for the same store type, so it compares with
  and is read as one. Both methods are reached through `GeneratedValueObjectAttribute.Accept`, never by name, and
  are public, so a compiled model writes a rewritten filter out. Unary `+` of a number is its operand. A `Nullable`
  operand is unwrapped and wrapped again by conversions EF drops, and the column's nullability carries a null
  through the SQL.
- **Re-typed, not cast.** The database's arithmetic, and `.Value` of it, is re-typed as a column is: cast to the
  column's store type, SQL Server's default `decimal(18,2)`, `(o.Price / 32m).Value` over 5 read 0.16 where memory
  reads 0.15625, and a filter on it lost the row (measured on Azure SQL Edge). SQLite's decimal arithmetic is
  functions, `ef_add`, `ef_multiply`, `ef_divide`, `ef_mod` and `ef_negate`, re-typed by those names; any other
  function's result is converted.
- **A wrapped type without an expression of its own.** `byte`, `short`, `sbyte`, `ushort` and `char` have no
  arithmetic in an expression tree, where C# computes it as an `int`'s. Their operators are refused naming the value
  object, between two value objects too, which no release translated (#184 is 2.4's).

`ArithmeticQueryTests` holds it as a differential oracle: 32 queries (filters, projections, orderings and an
aggregate; a column, a nullable column, a JSON property, a navigation, a captured factor and a decimal column as
the other operand; `checked`, unary `+` and `-`; past the column's scale) against the same queries over the objects
in memory, on SQLite, PostgreSQL 17 and SQL Server. `ArithmeticQueryFilterTests` holds the filters, named and not,
and `ForeignConvertedArithmeticTests` a converter of the application's own.
