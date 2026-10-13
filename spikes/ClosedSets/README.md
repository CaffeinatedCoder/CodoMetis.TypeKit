# Spike: can a closed set be a generated type with static members, on Metalama 2026.1 and .NET 10?

**Status: run 2026-10-13, answered.** From the "closed-set (smart enum) family" part of phase 13
(2.0.0) in [docs/plan.md](../../docs/plan.md) §2. Decision 44 pins 2.0.0 to `net10.0` and Metalama
2026.1, so the family is a generated type with static members, not a C# 15 union; this spike is the
measurement that nothing in it needs anything newer.

**Question.** A member is a `public static readonly` field of the set's own type, with constant data
through a private constructor the author writes, or none. Can a Metalama 2026.1 aspect, applied by
a transitive fabric as the value-object aspects are, derive the rest: `Name` from the field, `All`
in declaration order, `Match` with one required delegate per member named after it, `TryParse` and
`Parse` by name, `ToString`, `QualifiedName`, a JSON converter writing the name and reading it back
as the member itself, a private constructor where none is declared, and the interface's static
abstract members satisfied so generic code can use any set?

**Answer: yes, all of it, on .NET SDK 10.0.401 and Metalama 2026.1.29**, with one workaround. The
names are assigned by an initializer injected before the static constructor
(`InitializerKind.BeforeTypeConstructor`), which runs after the static field initializers, so a
member's `Name` needs no `nameof` at the declaration. `Match`'s parameters are added per member in
`buildMethod`; `Func<TResult>` cannot be built from the method builder's own type parameter
(Metalama asserts "the reference to ITypeSymbol being null is not supported yet"), so the template
declares one `Func<TResult>` parameter, whose bound type the other members' parameters copy, and the
first is renamed. Everything else is the plain advising API the value-object aspects already use.

## The candidate

`Spike.Generators` holds `IClosedSet<TSelf>` (the contract), `ClosedSetAspect` and a transitive
fabric; `Spike.App` declares `OrderStatus` (no data, no constructor) and `OrderFault` (a kind and a
remedy per member through a private constructor) and runs the claims. `dotnet build ClosedSets.slnx`
then `dotnet run --no-build --project Spike.App`.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | `All` holds the members themselves, in declaration order | measured: `Open,Shipped,Cancelled`, `All[1]` is `Shipped` |
| 2 | `Name` comes from the field, assigned after the static field initializers; `QualifiedName` and `ToString` follow | measured: `OrderFault.NotFound`, `Cancelled` |
| 3 | Constant data per member through the author's private constructor, optional | measured: a kind and a remedy, one remedy null |
| 4 | `Match<TResult>` has one required delegate per member, named after it in camel case | measured: `Match(open:, shipped:, cancelled:)` picks the branch; built through the template-parameter workaround above |
| 5 | `TryParse` and `Parse` by name; an unknown name is `false` or a `FormatException` naming the type, never the input | measured |
| 6 | JSON writes the name and reads the member itself back; an unknown name is a `JsonException` naming the type, never the input | measured, through a nested converter class and `[JsonConverter]` introduced by the aspect |
| 7 | Members are singletons: `==` is reference equality and `Parse` returns the member | measured |
| 8 | The interface's static abstract members are generated, so `T.All` works in generic code | measured: `Names<OrderFault>()` |
| 9 | A set without a constructor gets a private one, so `= new()` in a member compiles and nothing outside can construct one | measured: `OrderStatus` builds and runs |
| 10 | Diagnostics: not partial, not sealed, no member, a public constructor, a public static property of the set's type | written (SPIKE001–005); held by the generators' tests once ported |

**What the port adds beyond this spike:** the fault bits (a kind per member, an optional sentence,
read by the fault responses of step 8), the discovery attribute the satellites read, the OpenAPI
string enum and the EF column by name, and the CMTK ids for the diagnostics.
