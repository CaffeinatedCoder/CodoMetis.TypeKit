# 0089. CMTK0010 checks a method bound by pattern, for the kinds 2.1.0 adds; 2.0's four join at 3.0.0

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the pre-release audit of the unreleased 2.1.0. Measured in `TypeArgumentConstraintAnalyzerTests`
and with Microsoft.CodeAnalysis 5.0.0.

**What was silent.** CMTK0010 read written type arguments, calls, method groups, properties and operators. A
method the compiler binds by pattern has its type arguments inferred and appears in no call: a `foreach`'s
`GetEnumerator`, an `await`'s `GetAwaiter`, a `Deconstruct` in an assignment, a `foreach` or a positional
pattern, and a collection expression's builder or `Add`. With `GetEnumerator<[Concrete] T>(this Bag<T>)`,
`foreach (var item in bag)` over a `Bag<Handler>` was silent where `Patterns.GetEnumerator(bag)` was reported, and
`docs/type-constraints.md` said every inferred use is checked.

**Decision.** Each is now read from the semantic model (`GetForEachStatementInfo`, `GetAwaitExpressionInfo`,
`GetDeconstructionInfo`, a recursive pattern's `DeconstructSymbol`, a collection expression's `ConstructMethod`)
and checked as an inferred call, reported where the pattern is used. Roslyn exposes no `Add` of a collection
expression, so `default(T).Add(element)` is bound speculatively where an `Add` that could carry a constraint is
in scope, as the compiler binds the call it makes.

At these uses, only the kinds 2.1.0 adds are checked, `[Distinct]` and `[Unrelated]` among them. CMTK0010 is an
Error and shipped in 2.0.0 with `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]`; checking those four here
would report more under an Error within major 2, which [decision 34](0034-errors-fixed-within-major-version-warnings.md)
reserves for a major version. They join at 3.0.0, on the roadmap. The guide and the analyzer README say so.

Rejected: checking all kinds now, which breaks a 2.0 consumer's build in a minor version; a separate Warning
for the four, a second id for one rule's gap that 3.0.0 closes anyway.
