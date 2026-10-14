# 0081. Six more kinds a type parameter demands of its argument: `[Interface]`, `[Unsealed]`, `[ValueObject]`, `[ClosedSet]`, `[FlagsEnum]`, `[ReadOnlyStruct]`

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the candidates [decision 79](0079-distinct-and-unrelated-type-parameters.md) listed as
"considered next", built the same day at the user's request for 2.1.0. Each is one attribute in the base
package and one `Satisfies` case in `TypeArgumentConstraintAnalyzer`, under CMTK0010 as the unary four of
[decision 43](0043-exceptions-type-argument-constraints-constructor-guarantee.md) are; nothing fires until a
declaration opts in, so no consumer's build changes on upgrade ([decision 34](0034-errors-fixed-within-major-version-warnings.md)).
The survey of 2026-10-14 found none of them in any library: AdvancedGenericTypeConstraints 0.4.1 and
RossLean.GenericsAnalyzer 1.4.0 constrain by open-generic implementation, attribute presence, name and
permitted lists, not by kind.

**What each accepts, and why that boundary.**

- `[Interface]`: `TypeKind.Interface`, nothing else. A class implementing the interface is what
  `DispatchProxy.Create<TContract, TProxy>`, a mock or a REST client refuses at run time; csharplang
  discussion 3579 asks for `where T : interface` and has no answer.
- `[Unsealed]`: a class or record class that is neither sealed nor static, abstract included. An interface
  is implemented, not derived from, so it is refused and `[Interface]` names it; a struct is sealed. This
  is the complement of `[Sealed]` within classes, the type a Castle, NSubstitute or EF lazy-loading proxy
  can subclass.
- `[ValueObject]`: a type for which `TypeKitSymbols.FindMarker` finds `IValue<T>` or
  `IValidatedValue<TSelf, T, TFault>` through any interface, struct or class alike. `[ClosedSet]`: one for
  which `FindClosedSetMarker` finds `IClosedSet<TSelf>`, a fault set included. Both say what no C# bound
  can, the marker with any type argument, so a generic over every value object has had no constraint at
  all. Discovery stays by interface symbol, the package's own metadata names resolved once per
  compilation, as every rule's.
- `[FlagsEnum]`: an enum carrying `System.FlagsAttribute`, the symbol resolved once by metadata name
  beside the package's own. `where T : Enum` admits a plain enum, whose values do not combine.
- `[ReadOnlyStruct]`: `INamedTypeSymbol { TypeKind: Struct, IsReadOnly: true }`, a `readonly struct` or
  `readonly record struct`; the primitives are declared so and pass. An enum is no struct declaration and
  is refused, as a mutable struct is, which `where T : struct` admits and an `in` parameter then copies.

**Forwarding, as decision 43 decided it.** A forwarding type parameter is a breach unless it carries the
attribute or a C# constraint proves it. `[ValueObject]` and `[ClosedSet]` are proved by a bound that is or
implements the marker, `where T : IValue<Guid>`, `where T : AccountId`, or a bound parameter so bound,
which `FindMarker` already read from constraints for CMTK0001. Nothing but the attribute proves the other
four: an interface bound admits its implementers, `class` and a class bound admit a sealed subclass, `Enum`
a plain enum, `struct` and `unmanaged` a mutable struct. Measured in
`TypeArgumentConstraintAnalyzerTests`: 11 proofs silent, 8 unprovable forms reported.

**Not built.** `[Static]`: a static class is no type argument (CS0718), so there is nothing to constrain.
`[Enum]`, `[Delegate]`, `[Unmanaged]`: the language has them. `[Immutable]`: undecidable from a
declaration. `[Public]`, `[Nested]`, `[Positional]`: no demand found.

Additive, in 2.1.0.
