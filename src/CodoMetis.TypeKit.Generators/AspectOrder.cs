using CodoMetis.TypeKit.Generators;
using Metalama.Framework.Aspects;

// The implementation aspect runs first and leaves ValueObjectAspectState for the others. An aspect
// missing from this list would run in an unspecified order, find no state, and generate nothing.
[assembly: AspectOrder(
    AspectOrderDirection.CompileTime,
    typeof(ValueObjectImplementationAspect),
    typeof(ValueObjectInterfaceAspect),
    typeof(ValueObjectJsonAspect),
    typeof(ValueObjectParsableAspect),
    typeof(ValueObjectFormattableAspect),
    typeof(ValueObjectComparableAspect),
    typeof(ValueObjectMinMaxValueAspect),
    typeof(ValueObjectTypeConverterAspect),
    typeof(ValueObjectConvertibleAspect),
    typeof(ValueObjectExtensionsAspect)
)]
