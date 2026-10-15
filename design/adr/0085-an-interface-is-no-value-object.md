# 0085. An interface is no value object to `[ValueObject]`

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the reviews of the unreleased 2.1.0 that led to [decision 84](0084-cmtk0010-proofs-read-what-a-closed-use-can-be.md);
refines `[ValueObject]` of [decision 81](0081-six-more-kinds-of-type-argument.md), whose argument was "a type for
which `FindMarker` finds `IValue<T>` or `IValidatedValue<TSelf, T, TFault>` through any interface". `FindMarker`
reads a type's `AllInterfaces`, which hold an interface's bases and not the interface itself, so
`Keyed<IDerivedValue>`, `interface IDerivedValue : IValue<int>`, was accepted, and `Keyed<IValue<int>>`
refused. A bound reads the marker from the constraints, the interface itself included, so `where T : IValue<int>`
proved `[ValueObject]`, and `Bound<IValue<int>>()` reached `Keyed<IValue<int>>` silently. CMTK0010 ships at Error
in 2.1.0, so the boundary is settled before the tag ([decision 34](0034-errors-fixed-within-major-version-warnings.md)).

**An interface is no value object.** A value object is a struct or a class the generators complete, with a
`From`, a converter and a `Value`; an interface deriving from the marker is none of them, and a generic over
value objects that is handed one gets nothing it relies on. `[ValueObject]` now refuses every interface. A
forwarding parameter bound to the marker proves `[ValueObject]` only beside a constraint no interface meets:
`struct` (which `unmanaged` sets too), `new()`, a class bound, or a bound parameter that admits no interface.
`class` is not one: an interface is a reference type, and `where T : class, IValue<int>` took `IValue<int>`
(measured with the .NET 10.0.4xx SDK). Rejected: accepting every interface that derives from the marker,
the other way to be consistent, which admits a type no generator completes.

**`[ClosedSet]` needs no change.** `IClosedSet<TSelf>` declares static abstract members, so an interface carrying
it is no type argument at all (CS8920, measured on `interface ISet : IClosedSet<ISet>`, whose own declaration is
refused), and a bound to the marker admits no interface either. A check for it in the analyzer would be dead code.

Measured in `TypeArgumentConstraintAnalyzerTests`: `IValue<int>` and an interface deriving from it refused; four
proofs beside `struct`, `unmanaged`, `new()` and a class bound, and two through a bound parameter, silent;
the marker alone, beside `class`, through a derived interface and through an unconstrained parameter, reported.
