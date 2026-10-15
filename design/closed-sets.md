# Closed sets

A closed set is a `sealed partial class` implementing `IClosedSet<TSelf>` (or `IFaultSet<TSelf>`),
whose members are its `public static readonly` fields of its own type. `ClosedSetAspect`, applied
by the same transitive fabric as the value-object aspects and first in `AspectOrder`, generates the
rest. Measured in [spikes/ClosedSets](../spikes/ClosedSets/README.md); shipped in 2.0.0
([decision 42](adr/0042-2-0-0-next-release-bundles.md), [decision 44](adr/0044-2-0-0-net10-0-metalama.md)).
The consumer's guide is [docs/closed-sets.md](../docs/closed-sets.md).

## What is generated, and what must stay true

- **Generated:** `Name`, `QualifiedName` (the nesting chain with dots, `Order.Status.Draft`), `All`
  in declaration order, `Match` with one required delegate per member, `TryParse`/`Parse` (ordinal,
  each with an `ignoreCase` overload), `IParsable<TSelf>` explicitly, `ToString()` unless written by
  hand (the one seam), a nested JSON converter and type converter, `GeneratedClosedSetAttribute<TSet>`
  for run-time discovery, and a private constructor where none is declared.
- **Members are singletons.** Equality is reference equality, `Parse` answers the member itself, and
  nothing outside the type can make an instance (CMTK1016, CMTK1017). A record is refused (CMTK1020):
  `with` would copy a member into an instance that is none.
- **Names come after the field initializers.** An initializer injected before the static constructor
  (`InitializerKind.BeforeTypeConstructor`) assigns each member's name and builds `All`, so a member
  needs no `nameof`, and the parts of a set split across files are all initialised when it runs.
  The cost is a window: a static field initializer of the set itself runs before it, and anything
  generated that it reads there is refused rather than answered from a half-built set. A member
  written in the static constructor's body would hold an instance already named as another, and one
  written in a field initializer would give one instance two names, all of which compiles: the
  analyzer refuses every write to a member outside its declaration (CMTK0013, decided 2026-10-14
  before the tag, after a review found `Open = Closed` turning `Closed.Match` to the open branch).
- **`All` is a wrapper, never the array.** Cast to `IList<T>` or `T[]`, the array was writable, and
  a member replaced there was listed while `Parse` answered the original.
- **Ordinal unless asked** ([decision 51](adr/0051-closed-set-parses-ignoring-case-only.md)): JSON,
  the EF column, binding and `IParsable` read the name as stored or sent; `ignoreCase: true` is an
  overload the caller opts into. Two members whose names differ only in case are refused (CMTK1021),
  since they are one name to that overload and, where the first letter differs, one `Match`
  parameter. CMTK1021 compares by each transform itself, ordinal ignoring case for the overload and
  the parameters' own lower-casing of the first letter for `Match`: they differ for the Kelvin sign
  (U+212A), which only the second takes for `K` (decided 2026-10-13, after a LAMA0611).
- **No refusal quotes the input** ([decision 27](adr/0027-no-refusal-quotes-input.md)): an unknown
  name is a `FormatException` or a `JsonException` naming the set, and its message links the
  README's entry ([decision 37](adr/0037-refusal-s-message-landing-page.md)).

## Where each satellite meets a set

| Where | How | Held by |
|---|---|---|
| JSON | The name, as a value and as a dictionary key; no naming policy renames it, since a renamed name would not read back | `ClosedSetTests` |
| EF Core | A text column of the name through `ClosedSetConverter<TSet>`, found by the type-mapping plugin through the discovery attribute, as a value object is ([EF Core](ef-core.md)); an unknown stored name throws when read | `ClosedSetMappingTests` |
| OpenAPI | A string enum of the names, as a component named after the nesting chain ([decision 24](adr/0024-nested-value-objects-named-after-nesting.md)), in every position a parameter or a property can take; a nullable property admits null, inlined too ([decision 36](adr/0036-nullable-value-object-admits-null-openapi.md)) | `ClosedSetSchemaTests`, `ClosedSetBindingTests` |
| Fault responses | An `IFaultSet<TSelf>` member carries a `FaultKind` and an optional description, and `AddFaultResponses<TSet>()` maps every member by kind ([decision 49](adr/0049-fault-responses-client-can-act.md)); sets compose side by side, `Fault<TSet, TShared>`, each answered as it is mapped, and a route leaves out the members it never answers with ([decision 78](adr/0078-fault-sets-compose-side-by-side.md)); `AddFaultResponses()` maps every set with no mapping of its own, reached through `GeneratedClosedSetAttribute.Accept` ([decision 80](adr/0080-second-pilot-one-mapping-held-fault-bare-name.md)) | `FaultResponseTests`, `ComposedFaultResponseTests`, `FaultExclusionTests`, `FaultSetDefaultTests` |
| Serilog | Logged as what it prints, never destructured into its constant data ([decision 41](adr/0041-sensitive-value-object-declares-tostring-serilog.md)) | the Serilog tests |

## Diagnostics

CMTK1013 to CMTK1021, all Errors, each reported on the declaration it names; the table with the
fixes is in the [Generators README](../src/CodoMetis.TypeKit.Generators/README.md). A set the aspect
cannot complete is never left half-generated.

## Wrapped by a value object

A value object over a set, `IValue<OrderStatus>`, is generated in full wherever the set is declared
([decision 60](adr/0060-value-object-over-closed-set-generated-in-full.md)): `ClosedSetAspect` runs
first, so the value-object aspects see the set's `IParsable`, `Parse` and `[TypeConverter]` and build
the value object's parsing and type converter on them. Last, it ran after them, and over a set of the
same project the value object had no parsing and the base `TypeConverter`. Held by
`ClosedSetWrappingTests` (a struct, a record class, a nested set, a validated one, and a set of a
referenced project) and by CMTK1011 still refusing a hand-written `Parse` there.

## Open

- A stable external name for a member, apart from its C# identifier, waits for a spike
  ([roadmap](roadmap.md)): parsing, JSON, EF, OpenAPI and the fault identifiers would change at once.
