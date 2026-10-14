# 0084. CMTK0010's proofs read what a closed use can be, not the constraint as written

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, two independent reviews of the unreleased 2.1.0, an outside one and the
code-analyst agent, each finding reproduced before it was fixed; refines the forwarding proofs of
[decision 79](0079-distinct-and-unrelated-type-parameters.md) and [decision 83](0083-the-contract-s-converter-first-and-enum-proves-nothing-against-struct.md).
CMTK0010's pair checks and its six new kinds ship at Error in 2.1.0, and an Error that later caught
more would break builds within the major ([decision 34](0034-errors-fixed-within-major-version-warnings.md)),
so each is settled before the tag. Measured in `TypeArgumentConstraintAnalyzerTests`.

**A bound built from a type parameter is met by what the parameter is at each use.** A forwarding
parameter was proved apart from a type when the type broke one of its bounds, each compared as written.
`where T : IEquatable<T>` is met by `int`, which implements `IEquatable<int>`, not `IEquatable<T>`, so
`Equatable<T> : Pair<T, int>` passed, and `Equatable<int>` is `Pair<int, int>`; so did
`where TSet : class, IFaultSet<TSet>` against `Refusal` in `Fault<TSet, Refusal>`, `where T : Node<T>`
against `Leaf : Node<Leaf>`, and `where TB : IEnumerable<TA>` against `List<int>`. An interface bound
built from a parameter is now taken as met, as a bound that is a parameter already was. A class bound
built from one is met by a type whose base chain holds its definition, since a class converts to a
class only along its chain, so `where T : Node<T>` against `PlaceOrder` is still proved. Two class
bounds, or a class bound and a type, are compared by their definitions when either is built from a
parameter: `Node<TA>` and `Node<TB>` are one chain, and were proved apart for both `[Distinct]` and
`[Unrelated]`; `Node<TA>` and `Command` are two. Rejected: substituting the parameter into its own bound
before comparing, which covers `IEquatable<T>` and not a bound that names another parameter,
`IEnumerable<TA>`, and adds a type rewriter for no case the rule above misses.
