# 0060. A value object over a closed set of its own project is refused (CMTK1005)

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; [Aspects](../aspects.md), [Closed sets](../closed-sets.md).

`ClosedSetAspect` runs last in `AspectOrder`, and the value-object aspects do not see what it
introduces. Over `sealed partial class SetA : IClosedSet<SetA>` declared in the same project,
`readonly partial record struct OverSet : IValue<SetA>` was generated without `Parse`, `TryParse`
and `IParsable<OverSet>`, and with the base `TypeConverter` rather than its own, while its JSON
worked (measured). Over the same set in a referenced project it got all of them. That is the
layout-dependent surface CMTK1005 already refuses for a value object over a value object of its own
project, and it is refused the same way and with the same id: the fabric answers it, since it reads
the wrapped type, and the message names the set and says to declare it in a referenced project or to
wrap its name. A set from a referenced project stays allowed and generated in full. A refusal can be
lifted later, by ordering the aspects so that the value-object aspects see the set's members; a
silent half-generation could not be taken back. 2.0.0 is unreleased, and closed sets are new in it,
so no 1.x declaration meets the refusal.
