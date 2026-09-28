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

    /// <summary>
    /// The wrapped value's conversion, except the identity: <c>Convert.ChangeType(q, typeof(Quantity))</c>
    /// reaches here and threw <c>InvalidCastException</c>, and a conversion to <see cref="object"/>
    /// returned the wrapped value rather than the value object. A conversion to <see cref="string"/> is
    /// <see cref="IConvertible.ToString(IFormatProvider)"/>'s, so it honours a hand-written <c>ToString()</c>.
    /// </summary>
    [Template]
    public object ToTypeTemplate(Type conversionType, IFormatProvider? provider, [CompileTime] INamedType valueObjectType, [CompileTime] bool declaresToString)
    {
        string valueObject = meta.CompileTime(ValueObjectTypes.SourceName(valueObjectType));

        if ((bool)ExpressionFactory.Parse($"(conversionType == typeof({valueObject}) || conversionType == typeof(object))", TypeFactory.GetType(SpecialType.Boolean), false).Value!)
            return meta.This;

        if (declaresToString)
        {
            if (conversionType == typeof(string))
                return meta.This.ToString();
        }

        return ExpressionFactory.Parse($"((global::System.IConvertible)this.Value).ToType(conversionType, {Provider})", TypeFactory.GetType(SpecialType.Object), false).Value!;
    }

    [Template]
    public string ToStringTemplate(IFormatProvider? provider, [CompileTime] bool declaresToString)
    {
        if (declaresToString)
            return meta.This.ToString();

        return (string)ExpressionFactory.Parse($"((global::System.IConvertible)this.Value).ToString({Provider})", TypeFactory.GetType(SpecialType.String), false).Value!;
    }

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
