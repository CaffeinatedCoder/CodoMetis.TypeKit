# 0079. `[Distinct]` and `[Unrelated]` constrain two type parameters against each other, under CMTK0010

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, after [decision 78](0078-fault-sets-compose-side-by-side.md) shipped `Fault<T1, T2>`.
Measured on .NET SDK 10.0.402 against the built base package: `Fault<A, A>`, one set twice, compiles as a
type, and every use of it fails, the conversion from `Fault<A>` with CS0457 (ambiguous user-defined
conversion) and `Fault<A, A>.Of(A.X)` with CS0121; the satellite README said so in a sentence. The four
constraint attributes of [decision 43](0043-exceptions-type-argument-constraints-constructor-guarantee.md),
`[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]`, each constrain one parameter; nothing constrains two
against each other, and C# cannot: `Result<string, string>`, `Either<Pet, Dog>` and `Mapping<Dog, Dog>` all
compile. A survey of the ecosystem the same day (AdvancedGenericTypeConstraints 0.4.1, RossLean.GenericsAnalyzer
1.4.0, Metalama's `TypeParameterAspect`, csharplang discussions 1004, 742, 3579, 9641 and 3013) found
no library or language feature for either the unary four or a relational constraint.

**Two attributes, not one with a mode.** `[Distinct]` on each of two or more parameters of one generic says
their arguments are different types; `[Unrelated]` says neither derives from, implements, boxes to or is the
other, which implies `[Distinct]`. A pair carrying one of each is held to the stricter. A parameter carrying
neither is in no pair, and one parameter alone with an attribute constrains nothing. The vocabulary stays
one attribute per constraint, as `[Abstract, Record]` composes today, so each gets its own row in the
README's table and its own forwarding rule. Rejected: a bool, `[Distinct(Unrelated = true)]`, one concept
with a mode; a generic attribute naming the sibling, `[DistinctFrom<T2>]`, which C# refuses, since an
attribute on a type parameter cannot use the containing generic's other parameters as type arguments; and
`[DistinctFrom(nameof(T2))]`, a rename hazard the group semantics make unnecessary.

**Under CMTK0010, not a new id.** The rule's contract is a type argument that breaks a constraint its
parameter declares by attribute; this is one more such constraint, reported once per pair at the later
argument, so a breach is one error and not two. The rule ships at Error already, and nothing fires until a
declaration opts in, so [decision 34](0034-errors-fixed-within-major-version-warnings.md) is kept: no new
rule at Error in a minor. The one declaration this record opts in is the base package's own, below.

**Same means one symbol; related means an implicit identity, reference or boxing conversion either way.**
`SymbolEqualityComparer.Default` ignores nullable annotations, so `Pair<string, string?>` is one type twice,
as the compiler's ambiguity does not care about the annotation either; `dynamic` is `object`. Related is
read from `CSharpCompilation.ClassifyConversion` in both directions: a base and a derived type, an interface
and an implementer, `object` and anything, a struct and `ValueType` or an interface it implements. A numeric
or a nullable conversion is no relation, so `int` beside `long` or `int?` is unrelated; two interfaces are
unrelated even though one type may implement both, which no declaration can decide, and the README says so.

**Forwarding is a breach unless proved apart, as for the unary four.** Two type parameters are told apart
when both carry the attribute (for `[Unrelated]`, that attribute) in one generic, whose closed uses are then
checked in turn; otherwise `Wrap<U, V> : Pair<U, V>` would end the requirement. C# constraints prove it
where they admit no common type: for `[Distinct]`, `struct` or `unmanaged` against `class` or a class
bound, two class bounds neither deriving from the other, and against a type, constraints the type breaks
(read as far as Roslyn exposes them; `new()` and a bound that is itself a parameter are taken as met,
since Roslyn has no public constraint check, dotnet/roslyn#46998). For `[Unrelated]`, `struct` against a
class, or a class bound, other than `object`, `ValueType` and `Enum`, which a value type boxes to, and
two class bounds of different chains; `class` alone proves nothing, since `object` satisfies it and is
related to everything. Measured in `TypeArgumentConstraintAnalyzerTests`: 14 forwarding forms for
`[Distinct]` and 15 for `[Unrelated]`, each proof silent and each unprovable form reported.

**`Fault<T1, T2>` and `Fault<T1, T2, T3>` declare `[Distinct]`.** A declaration of `Fault<A, A>` compiled
before and becomes CMTK0010 in a minor; no working consumer code had one, since every use of it was CS0457
or CS0121. `Result<T, TError>` stays unmarked: `Result<string, string>` works through `Result.Success` and
`Result.Error`, only its bare conversions are ambiguous, consumers have written it, and an Error there is a
breaking change in a minor. A nudge for it would be a separate Warning rule, a decision of its own, not
taken.

**Considered next, not built.** The same mechanism carries more constraints the language lacks, each one
attribute and one `Satisfies` case: `[Interface]`, for a proxy's, a mock's or a REST client's contract, where
`DispatchProxy.Create` fails at run time (csharplang discussion 3579 asks for it); `[Unsealed]`, a type a
proxy can derive from; `[ValueObject]` and `[ClosedSet]`, a TypeKit type of any wrapped type, which
`where T : IValue<?>` cannot say; `[FlagsEnum]`; `[ReadOnlyStruct]`. `[Static]` is impossible, a static
class is no type argument (CS0718). Listed in the [roadmap](../roadmap.md) for a decision by demand.

Additive, in 2.1.0.
