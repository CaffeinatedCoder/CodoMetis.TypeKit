using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

// A null provider means the invariant culture, as in the generated formatting and parsing:
// Convert.ToString(amount) passes null.
internal sealed partial class ValueObjectConvertibleAspect
{
    // C# text: in a template, CultureInfo.InvariantCulture beside a run-time operand of ?? counts as
    // a compile-time value (LAMA0200).
    private const string Provider = "(provider ?? global::System.Globalization.CultureInfo.InvariantCulture)";

    [Template]
    public TypeCode GetTypeCodeTemplate() =>
        ((IConvertible)meta.This.Value).GetTypeCode();

    [Template]
    public object ToTypeTemplate(Type conversionType, IFormatProvider? provider) =>
        ExpressionFactory.Parse($"((global::System.IConvertible)this.Value).ToType(conversionType, {Provider})", TypeFactory.GetType(SpecialType.Object), false).Value!;

    [Template]
    public string ToStringTemplate(IFormatProvider? provider) =>
        (string)ExpressionFactory.Parse($"((global::System.IConvertible)this.Value).ToString({Provider})", TypeFactory.GetType(SpecialType.String), false).Value!;

    [Template]
    public dynamic TypedConversionTemplate(
        IFormatProvider?     provider,
        [CompileTime] string methodName,
        [CompileTime] IType  returnType
    )
    {
        return ExpressionFactory.Parse(
            $"((global::System.IConvertible)this.Value).{methodName}({Provider})",
            returnType,
            false
        ).Value!;
    }
}
