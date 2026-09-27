using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Spike.Abstractions;

namespace Spike.ValueObjects;

[RunTimeOrCompileTime]
public enum ValueObjectKind { SimpleValue, Validated }

[CompileTime]
internal static class ValueObjectTypes
{
    public static ValueObjectKind? GetKind(INamedType t)
    {
        if (t.ImplementedInterfaces.Any(i => i.IsConvertibleTo(typeof(IValue<>), ConversionKind.TypeDefinition))) return ValueObjectKind.SimpleValue;
        if (t.ImplementedInterfaces.Any(i => i.IsConvertibleTo(typeof(IValidatedValue<,,>), ConversionKind.TypeDefinition))) return ValueObjectKind.Validated;
        return null;
    }

    public static INamedType? GetUnderlyingType(INamedType t)
    {
        var i1 = t.ImplementedInterfaces.SingleOrDefault(i => i.IsConvertibleTo(typeof(IValue<>), ConversionKind.TypeDefinition));
        if (i1 != null) return i1.TypeArguments[0] as INamedType;
        var i2 = t.ImplementedInterfaces.SingleOrDefault(i => i.IsConvertibleTo(typeof(IValidatedValue<,,>), ConversionKind.TypeDefinition));
        return i2?.TypeArguments[1] as INamedType;
    }
}
