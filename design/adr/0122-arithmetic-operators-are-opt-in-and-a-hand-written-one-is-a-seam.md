# 0122. Arithmetic operators are opt-in by their interfaces, and a hand-written one is a seam

Date: 2026-10-30
Status: accepted

Context: [#184](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/184), under the 2.4.0 epic
[#177](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/177); [Aspects](../aspects.md); refines
[decision 28](0028-seams-declared-everything-else-refused.md) under [decision 34](0034-errors-fixed-within-major-version-warnings.md).
EricksonLopez.DomainPrimitives generates arithmetic on every numeric primitive
([KB-055](../knowledge/KB-055-ericksonlopez-domainprimitives.md)); TypeKit wrote `Money.From(a.Value + b.Value)`.

**A plain value object gets the arithmetic operators of the operator interfaces it declares, and nothing
else.** `ValueObjectArithmeticAspect` reads `IAdditionOperators`, `ISubtractionOperators`,
`IMultiplyOperators`, `IDivisionOperators`, `IModulusOperators`, `IUnaryNegationOperators` and
`IUnaryPlusOperators` among the interfaces the value object implements, directly or through another
interface, whose first type argument is the value object. It generates an interface's operator, and its
`checked` form where the interface has one, when the wrapped type implements the same interface with the
value object replaced by the wrapped type throughout: `IMultiplyOperators<Money, decimal, Money>` needs
`IMultiplyOperators<decimal, decimal, decimal>`, `IDivisionOperators<Money, Money, decimal>` (a ratio) needs
the same. An operand or a result of another type passes through as it is.

- **Opt-in, never by default, never by attribute.** Declaring the interface is what a generic-math constraint
  reads anyway, and the operators a value object has are visible on its declaration. `TSelf * TSelf` is
  allowed when declared: whether two amounts multiply is the domain's question, not TypeKit's.
- **The wrapped type's operator, through `GeneratedArithmetic`** in the base package: a call through a type
  parameter constrained to the interface, unchecked or checked, as `GeneratedFormatting` calls formatting.
  Written as `left.Value + right.Value`, two `byte`s were added as `int`s, and `char`, whose operator
  interfaces are explicit, could not be reached at all. Each helper states its context (`unchecked(...)`,
  `checked(...)`), so the consumer's `CheckForOverflowUnderflow` changes nothing: the context picks the
  operator, as it picks the wrapped type's, and the operator does what the wrapped type's does in it. No
  reflection, so Native AOT needs nothing (the smoke test's `aot` consumer calls one directly, through
  generic code and in a checked context).
- **Never on a validated value object.** An operator cannot return a `Result`, and a sum outside the range
  `Create` accepts would have to throw or skip `Create`. The declared interface stays unimplemented and the
  build fails with CS0535, as it did before. So does an interface the wrapped type does not match:
  `DateTime`, `TimeSpan`, `DateOnly` and `Guid` implement none of the operator interfaces (measured in the
  .NET 10 reference assemblies: they were dropped before .NET 7 shipped).
- **A hand-written operator is a seam.** One the value object declares for the same operands, of either kind,
  whatever it returns, or an explicit implementation of the interface, its own, a base record's or an interface's, is kept,
  and neither form of that operator is generated; the operators not written are. Decision 28 would make it
  CMTK1011, but code that implemented these interfaces by hand on 2.3 must build unchanged on 2.4, and
  decision 34 lets no minor report more under an Error. Nothing generated depends on an arithmetic operator,
  so a kept one disagrees with nothing, which is what a seam needs. Its `checked` form goes with it: a
  generated `checked +` beside a hand-written `+` did the wrapped type's arithmetic in a checked context and
  the user's everywhere else. The other way round, a hand-written `checked +` without its `+` is CS9025, and
  generating the `+` would have built it, adding by the wrapped type's rules outside a checked context and
  the user's inside one; it is kept failing. A public operator generated beside a base record's explicit implementation
  would not implement the interface, which the base does: generic code would reach the base's, `a + b`
  ours. One beside an interface's explicit implementation, the `+` of an `IMoneyMath<TSelf>` that every type
  declaring it reaches by default, replaced it: generic addition read 3 where the interface's answered 1 (an
  outside review of #188).
- **One operator per signature.** C# allows one `operator /(Money, Money)`, so of two interfaces that ask for
  it, the first declared gets it and the second fails the build (CS0738), to be implemented explicitly by hand.
- **A record class's operand of its own type** is checked for `null` and throws `ArgumentNullException`
  naming it, where dereferencing it threw `NullReferenceException` inside generated code.
- **In an EF Core query** an operator between two value objects is translated as the wrapped type's
  arithmetic over the columns and the database computes it (`ef_add` on SQLite, measured); EF never runs
  the operator. One with another operand or result throws there, since EF applies the value object's
  converter to the other side ([#186](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/186)),
  and a hand-written operator would run as the database's arithmetic, not as written
  ([#187](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/187)).
- **Left out:** `++` and `--`, the bitwise and shift operators, and `T * Money` (C# cannot declare it on
  `Money`, whose interface's first type argument must be itself).

`ArithmeticParityTests` holds it as a differential oracle: 22 declarations (every numeric wrapped type from
`sbyte` to `BigInteger`, `char`, `Half`, `float`, `double`, `decimal`, both record shapes, nested and
internal) against the wrapped type's own operators, unchecked and checked, over a catalogue of bounds, zero,
one, minus one and the floating-point specials. `ArithmeticTests` holds the seam and the other operand forms,
`ArithmeticBuildTests` the CS0535, CS0738 and CS9025 cases, and `ArithmeticQueryTests` the EF Core translation.
