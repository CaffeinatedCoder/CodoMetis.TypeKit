using CodoMetis.TypeKit.Generators;
using Metalama.Framework.Aspects;

// ClosedSetAspect runs first, so the value-object aspects see what it introduces into a closed set of
// the same project: a value object over one gets the set's IParsable, Parse and type converter to build
// on, as over a set from a referenced project. Last, it ran after them, and such a value object was
// generated without parsing and with the base TypeConverter (decision 60). One aspect class never sees
// what its own sibling instances introduce, which is why a value object over a value object is refused
// instead; the closed-set aspect reads nothing the value-object aspects introduce.
// Of the value-object aspects, ValueObjectAspect runs first and leaves ValueObjectAspectState for the
// others. An aspect missing from this list would run in an unspecified order, find no state, and
// generate nothing.
[assembly: AspectOrder(
    AspectOrderDirection.CompileTime,
    typeof(ClosedSetAspect),
    typeof(ValueObjectAspect),
    typeof(ValueObjectContractAspect),
    typeof(ValueObjectJsonAspect),
    typeof(ValueObjectParsableAspect),
    typeof(ValueObjectFormattableAspect),
    typeof(ValueObjectComparableAspect),
    typeof(ValueObjectMinMaxValueAspect),
    typeof(ValueObjectArithmeticAspect),
    typeof(ValueObjectTypeConverterAspect),
    typeof(ValueObjectConvertibleAspect),
    typeof(ValueObjectCompanionAspect)
)]
