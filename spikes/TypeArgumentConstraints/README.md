# Spike: can an attribute on a type parameter constrain its arguments where C# cannot?

**Status: outline, not yet run.** Proposed 2026-10-13, from the "Constraints a type parameter
cannot declare" part of phase 13 (2.0.0) in [docs/plan.md](../../docs/plan.md) §2, decision 43, and
the CMTK0010 row of §10.

**Question.** C# has `where T : class`, `struct`, `new()` and a base type, but no `where T :
abstract`, `sealed` or `record`. A generic base whose type argument must be an abstract record gets
no help from the compiler, and the closed type that violates it compiles. Can attributes on the
parameter, `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` in the base package, and one
analyzer rule, CMTK0010, make the violation an error at every closed use, in every consumer,
without Metalama?

An analyzer rather than an aspect, because an aspect on the parameter sees the declaration and
never the use, and the use site need not compile with Metalama (§3: the base package is
Metalama-free, and discovery is by the attribute symbol).

**Answer: pending.**

## The candidate (planned)

The layout follows [spikes/FabricSpike](../FabricSpike/README.md): an analyzer project
(`Candidate`, netstandard2.0, the rule as `.Analyzers` would ship it), a library declaring the
attributes and a `Base<[Abstract] TCommand, [Record] TEvent>`, and a consumer project with one
seeded violation per claim, each on its own line, so `dotnet build` output is the measurement: the
set of reported lines must equal the set of seeded lines.

## Findings

Each row is a claim the spike must settle. A seed that breaks the guard proves each one, as in
`.claude/skills/verify-the-guard`.

| # | Claim | Result |
|---|---|---|
| 1 | A base type `: Base<ConcreteCommand, …>` is reported at the type argument, naming the parameter, the argument and the constraint | |
| 2 | An implemented generic interface, a field, a property, a parameter, a return type and a local are each reported | |
| 3 | A `typeof(Base<ConcreteCommand, …>)` and a generic method call with explicit type arguments are reported | |
| 4 | A generic method call with inferred type arguments is reported, at the call, since no argument is written | |
| 5 | Forwarding: `class Sub<U> : Base<U, …>` is reported unless `U` carries `[Abstract]` itself; with it, `Sub<ConcreteCommand>` is reported at its own closed use | |
| 6 | A constraint on a type parameter of a generic method (`void M<[Sealed] T>()`) is checked at its calls | |
| 7 | One site is reported once, where a type appears in both syntax and a symbol action | |
| 8 | `[Record]` accepts a record class and a record struct; `[Abstract]` accepts an abstract class or record and refuses an interface; `[Sealed]` accepts a sealed class, a struct and a record struct; `[Concrete]` refuses an abstract type and an interface | |
| 9 | The four attributes compose: `[Abstract, Record]` requires both | |
| 10 | The open generic itself (`Base<TCommand, TEvent>` inside its own body) is never reported | |
| 11 | A closed use in a Razor component is reported (§10, generated code with a mapped location) | |
| 12 | `MakeGenericType` at run time is out of reach, stated in the README, and the attribute carries no run-time check | |
| 13 | The attributes have `AttributeTargets.GenericParameter` only, so a misplaced one is CS0592 | |
