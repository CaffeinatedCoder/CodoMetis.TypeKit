# 0080. One mapping for every fault set, a reader for a composed fault, and the bare name under a key of the host's

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, the second round of the pilot that [decision 78](0078-fault-sets-compose-side-by-side.md)
names, which converted its slice to `Fault<T1, T2>` and listed what it still wrote by hand. Decision 78
records the first round as 8 lines shorter, the spike's count (651 to 643); counted the pilot's way, the
slice went from 677 lines to 664 in the first round and to 616 in the second. The spike predicted 23 %
shorter (75 lines to 58); the real slice is 9 % shorter, since most of an endpoint and a service has
nothing to do with faults.

**One mapping for every fault set.** A host mapped each set with `AddFaultResponses<TSet>(…)`, the same
mapping each time: 24 calls once every slice is converted, and one forgotten stopped the host at start.
`AddFaultResponses()` maps every fault set an endpoint answers with by its kind, and
`AddFaultResponses(Func<IFault, FaultResponse>)` with the host's own mapping, which reads a member's kind
and description through `IFault`. A set mapped on its own keeps its mapping. An enum, or a closed set that
is no fault set, has no kind and still needs its own, and the startup check still refuses one left
unmapped. Each set is mapped the first time the startup check, the document or a response asks for it,
every member at once, so a member of no kind or a status that is no error stops the start as it fails
`AddFaultResponses<TSet>()`. The satellite holds the fault type only as an unconstrained type argument
there, so it reaches the set's members through `GeneratedClosedSetAttribute.Accept`, as the document's
closed-set schemas do, never through `MakeGenericType`. The default is given once; a second call throws.
Rejected: *every closed set by default, with no call*, which would change the wire of a host that has not
asked; *a mapping per kind*, `Func<FaultKind, FaultResponse>`, which cannot give a member its own problem
type from its description.

**A reader for a composed fault, in Testing.** `Fault<T1, T2>` has no public content, so a test matched it
to read the reason, and every test project carried the helper. `ShouldHold(reason)` fails unless the
composed fault holds a fault with that reason, of whichever type, and hands back the `Fault<TFault>`,
detail and rule intact; it fails on a `default` one, naming it. The reason's type picks the overload, one
per position, so a reason of no composed type is a compile error. Measured: `ShouldBeOf<TFault>()` in a
C# 14 `extension<T1, T2>` block, the shape that reads best, is CS1061 at `fault.ShouldBeOf<TeamFault>()`,
since C# does not infer the block's type parameters from the receiver while the call names the method's.

**The bare name under a key of the host's.** A host on `QualifiedFaultNames` whose clients predate it wrote
the bare name into its own member in `CustomizeProblemDetails`, and its document did not describe it.
`FaultResponseOptions.BareFaultNameMember` names that member: every problem answering a fault writes the
bare name under it, whatever `QualifiedFaultNames` says, and the document describes it beside `fault`. A
member a problem has already, ASP.NET's (`type`, `title`, `status`, `detail`, `instance`, `traceId`,
`errors`) or this package's (`fault`, `rule`), compared ignoring case, or a blank one, stops the start.

All of it is additive and joins 2.1.0.
