# Spike: can an attribute on a type parameter constrain its arguments where C# cannot?

**Status: run 2026-10-13, answered.** Proposed 2026-10-13, from the "Constraints a type parameter
cannot declare" part of phase 13 (2.0.0) in [the phase log](../../design/history.md), [decision 43](../../design/adr/0043-exceptions-type-argument-constraints-constructor-guarantee.md), and
the CMTK0010 row of §10.

**Question.** C# has `where T : class`, `struct`, `new()` and a base type, but no `where T :
abstract`, `sealed` or `record`. A generic base whose type argument must be an abstract record gets
no help from the compiler, and the closed type that violates it compiles. Can attributes on the
parameter, `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` in the base package, and one
analyzer rule, CMTK0010, make the violation an error at every closed use, in every consumer,
without Metalama?

An analyzer rather than an aspect, because an aspect on the parameter sees the declaration and
never the use, and the use site need not compile with Metalama ([§3](../../design/discovery.md): the base package is
Metalama-free, and discovery is by the attribute symbol).

**Answer: yes.** Measured 2026-10-13 on .NET SDK 10.0.401 with the rule as `.Analyzers` ships it
(`TypeArgumentConstraintAnalyzer`), over a library declaring the generics, a consumer of it and a
Razor class library. Every seeded line is reported, with the expected count where a line breaks
two constraints, and nothing else is: one report per written argument, one per inferred call, none
for the open generic in its own body or an unbound `typeof`. A forwarding type parameter is a breach
unless it carries the attribute, or `struct`/`new()` proves it. Shipped in 2.0.0 as CMTK0010 with
`TypeArgumentConstraintAnalyzerTests` holding each row.

## The candidate

`Library/` declares `Decider<[Abstract, Record] TCommand, [Abstract, Record] TEvent>`,
`Box<[Sealed] T>`, `Factory<[Concrete] T>` and `Runner.Run<[Abstract] T>` against the real base
package; `Consumer/Seeds.cs` has one breach per claim, each on a line ending in `// CMTK0010`, so
the build output is the measurement; `Component/Probe.razor` is claim 11. The rule is the real one,
referenced from `src/`, not a stand-in. To repeat:

```bash
dotnet build spikes/TypeArgumentConstraints/Consumer --no-incremental 2>&1 | grep -o -E "Seeds.cs\([0-9]+,[0-9]+\): error CMTK0010" | sort -u
grep -n "// CMTK0010" spikes/TypeArgumentConstraints/Consumer/Seeds.cs | cut -d: -f1
dotnet build spikes/TypeArgumentConstraints/Component --no-incremental 2>&1 | grep -o "Probe.razor([0-9]*,[0-9]*): error CMTK0010"
```

## Findings

Each row is a claim the spike must settle. A seed that breaks the guard proves each one, as in
`.claude/skills/verify-the-guard`.

| # | Claim | Result |
|---|---|---|
| 1 | A base type `: Base<ConcreteCommand, …>` is reported at the type argument, naming the parameter, the argument and the constraint | measured: `Decider<PlaceOrder, OrderPlaced>` reports at each argument, naming `TCommand`/`TEvent` of `Decider<TCommand, TEvent>` and the breach |
| 2 | An implemented generic interface, a field, a property, a parameter, a return type and a local are each reported | measured: an implemented `IComparer<Box<Handler>>`, a field, a property, a return type, a parameter and a local each report once |
| 3 | A `typeof(Base<ConcreteCommand, …>)` and a generic method call with explicit type arguments are reported | measured: `typeof(Box<Handler>)` and `Runner.Run<PlaceOrder>(…)` report at the argument |
| 4 | A generic method call with inferred type arguments is reported, at the call, since no argument is written | measured: `Runner.Run(new PlaceOrder())` reports at the call, once; the written form is not reported twice |
| 5 | Forwarding: `class Sub<U> : Base<U, …>` is reported unless `U` carries `[Abstract]` itself; with it, `Sub<ConcreteCommand>` is reported at its own closed use | measured: `Leaky<TCommand> : Decider<TCommand, …>` reports `TCommand` for each attribute it lacks; `Sound<[Abstract, Record] TCommand>` is silent and `Closed : Sound<PlaceOrder>` reports at its own use |
| 6 | A constraint on a type parameter of a generic method (`void M<[Sealed] T>()`) is checked at its calls | measured: `Boxed<T>() where T : struct` is silent (struct proves sealed); `Unproven<T>() where T : class` reports `T` |
| 7 | One site is reported once, where a type appears in both syntax and a symbol action | measured: the reported line counts equal the seeded ones, 2 where two arguments break, 3 for the forwarding line with two attributes and a sealed event |
| 8 | `[Record]` accepts a record class and a record struct; `[Abstract]` accepts an abstract class or record and refuses an interface; `[Sealed]` accepts a sealed class, a struct and a record struct; `[Concrete]` refuses an abstract type and an interface | measured: a sealed record, a struct and an enum pass `[Sealed]`, an interface fails it and `[Abstract]`; an abstract class fails `[Record]` and `[Concrete]`; a struct passes `[Concrete]` |
| 9 | The four attributes compose: `[Abstract, Record]` requires both | measured: `[Abstract, Record]` reports each attribute broken, so an abstract class reports `is not a record` alone |
| 10 | The open generic itself (`Base<TCommand, TEvent>` inside its own body) is never reported | measured: `Decider<TCommand, TEvent>` inside its own body and `typeof(Decider<,>)` report nothing; the library builds clean |
| 11 | A closed use in a Razor component is reported ([§10](../../design/analyzers.md), generated code with a mapped location) | measured: `Probe.razor(4,9): error CMTK0010` in a Razor class library |
| 12 | `MakeGenericType` at run time is out of reach, stated in the README, and the attribute carries no run-time check | by design: no run-time check, the README says `MakeGenericType` is out of reach |
| 13 | The attributes have `AttributeTargets.GenericParameter` only, so a misplaced one is CS0592 | measured in `TypeArgumentConstraintAnalyzerTests`: `[Abstract]` on a class is CS0592 |
