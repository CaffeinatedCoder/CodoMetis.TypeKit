using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectConvertibleAspect
{
    [Template]
    public TypeCode GetTypeCodeTemplate() =>
        ((IConvertible)meta.This.Value).GetTypeCode();

    [Template]
    public object ToTypeTemplate(Type conversionType, IFormatProvider? provider) =>
        ((IConvertible)meta.This.Value).ToType(conversionType, provider);

    [Template]
    public string ToStringTemplate(IFormatProvider? provider) =>
        ((IConvertible)meta.This.Value).ToString(provider);

    [Template]
    public dynamic TypedConversionTemplate(
        IFormatProvider?     provider,
        [CompileTime] string methodName,
        [CompileTime] IType  returnType
    )
    {
        return ExpressionFactory.Parse(
            $"((global::System.IConvertible)this.Value).{methodName}(provider)",
            returnType,
            false
        ).Value!;
    }
}
