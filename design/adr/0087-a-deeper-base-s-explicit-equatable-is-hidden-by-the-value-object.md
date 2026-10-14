# 0087. A deeper base's explicit `IEquatable` is taken over by a `new Equals(TBase)` the value object declares

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the pre-release audit of the unreleased 2.1.0; refines
[decision 77](0077-third-review-refines-74-to-76.md), which listed `IReimplementedEquatable<TBase>` for every base
and rejected a public `new Equals(TBase)` per base.

**Listing the interface takes over only the direct base's.** The runtime maps a listed interface to a method the
class declares, and otherwise keeps the mapping it inherits. The compiler synthesizes the value object's
`Equals(TBase)` for its direct base alone, so only there is the implementation the value object's own. Over
`DeepL1 : DeepL0`, with `DeepL0` implementing `IEquatable<DeepL0>` explicitly by reference, a `HashSet<DeepL0>`
held two value objects holding 42 and `IEquatable<DeepL0>.Equals` answered false, though the value object listed
`IReimplementedEquatable<DeepL0>` and the README promised every base. The case decision 77 named as a known limit,
a base between that implements the deeper one's `IEquatable` explicitly, is the same mapping and is closed with it.

**Decision.** For a base deeper than the direct one whose `IEquatable<TBase>` some type from the direct base down
to it implements explicitly, the value object declares `public new bool Equals(TBase? other)`, which calls
`Equals(object)` as the member it hides does, so the listed interface maps to it. Nowhere else: the member is
added only where CMTK1023 already names the explicit implementation, and it behaves as the inherited one did for
a caller, so decision 77's objection, members a value object does not declare, holds everywhere it cost nothing
to keep. A value object that declares that `Equals(TBase)` itself keeps it, which C# maps instead.

Rejected: the `new Equals(TBase)` for every deeper base (decision 77's objection); a default implementation in
`IReimplementedEquatable<T>`, which loses to the class mapping the base passes down; documenting the limit,
which leaves a `HashSet` of the base holding equal values twice.
