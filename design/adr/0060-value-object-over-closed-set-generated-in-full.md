# 0060. A value object over a closed set is generated in full wherever the set is declared

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; [Aspects](../aspects.md), [Closed sets](../closed-sets.md).

`ClosedSetAspect` ran last in `AspectOrder`, after the value-object aspects, which therefore did not
see what it introduces into a set of their own project. Over `sealed partial class SetA :
IClosedSet<SetA>` declared there, `readonly partial record struct OverSet : IValue<SetA>` was
generated without `Parse`, `TryParse` and `IParsable<OverSet>`, with the base `TypeConverter` rather
than its own, and with a hand-written `Parse` kept silently, while its JSON worked (measured). Over the
same set in a referenced project it got all of them.

`ClosedSetAspect` runs first now. The value-object aspects see the set's `IParsable`, `Parse` and
`[TypeConverter]` and build the value object's parsing and type converter on them, so a value object
over a set is the same wherever the set is declared: a struct or a record class, over a nested set,
validated through `Create`, ordinal, refusing without quoting the input, CMTK1011 on a hand-written
`Parse` (`ClosedSetWrappingTests`, `BuildOutcomeTests`; with the old order they fail). The closed-set
aspect reads only the set's own declaration (fields, constructors, initializer text, methods,
attributes, interfaces), none of which the value-object aspects introduce, so its output does not
change.

The alternative was to refuse the declaration with CMTK1005, as a value object over a value object
of its own project is refused. That refusal exists because one aspect class never sees what its own
sibling instances introduce, which no order can change. A closed set is a different aspect class, so
an order fixes it, and refusing a declaration that can be generated in full would have taken a
working use (an identifier that is one of a set's members) for no reason the user could act on.
