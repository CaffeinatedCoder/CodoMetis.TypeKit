using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

// A null provider means the invariant culture, as in the generated formatting and parsing:
// Convert.ToString(amount) passes null.
//
// Over a reference type (a string) every member goes through System.Convert with the wrapped value as
// an object, which for a value is the value's own IConvertible and for the null a default instance holds
// is Convert's answer for null: "" for text, the target type's default for a number. Cast to
// IConvertible, that null threw NullReferenceException, where ToString() answered "" (decided
// 2026-10-13). A struct is cast, as before: it is never null, and Convert would box it the same way.
internal sealed partial class ValueObjectConvertibleAspect
{
    // C# text: in a template, CultureInfo.InvariantCulture beside a run-time operand of ?? counts as
    // a compile-time value (LAMA0200).
    private const string Provider = "(provider ?? global::System.Globalization.CultureInfo.InvariantCulture)";

    [Template]
    public TypeCode GetTypeCodeTemplate([CompileTime] bool referenceType)
    {
        if (referenceType)
            return (TypeCode)ExpressionFactory.Parse("global::System.Convert.GetTypeCode((object?)this.Value)", TypeFactory.GetType(typeof(TypeCode)), false).Value!;

        return ((IConvertible)meta.This.Value).GetTypeCode();
    }

    /// <summary>
    /// The wrapped value's conversion, except the identity: <c>Convert.ChangeType(q, typeof(Quantity))</c>
    /// reaches here and threw <c>InvalidCastException</c>, and a conversion to <see cref="object"/>
    /// returned the wrapped value rather than the value object. A conversion to <see cref="string"/> is
    /// <see cref="IConvertible.ToString(IFormatProvider)"/>'s, so it honours a hand-written <c>ToString()</c>.
    /// </summary>
    [Template]
    public object ToTypeTemplate(Type conversionType, IFormatProvider? provider, [CompileTime] INamedType valueObjectType, [CompileTime] bool declaresToString, [CompileTime] bool referenceType)
    {
        string valueObject = meta.CompileTime(ValueObjectTypes.SourceName(valueObjectType));

        if ((bool)ExpressionFactory.Parse($"(conversionType == typeof({valueObject}) || conversionType == typeof(object))", TypeFactory.GetType(SpecialType.Boolean), false).Value!)
            return meta.This;

        if (declaresToString)
        {
            if (conversionType == typeof(string))
                return meta.This.ToString();
        }
        else if (referenceType)
        {
            // Text is the same as IConvertible.ToString(provider)'s, "" for a default instance, where
            // Convert.ChangeType answers null for it.
            if (conversionType == typeof(string))
                return ExpressionFactory.Parse($"global::System.Convert.ToString((object?)this.Value, {Provider})", TypeFactory.GetType(SpecialType.Object), false).Value!;
        }

        if (referenceType)
            return ExpressionFactory.Parse($"global::System.Convert.ChangeType((object?)this.Value, conversionType, {Provider})!", TypeFactory.GetType(SpecialType.Object), false).Value!;

        return ExpressionFactory.Parse($"((global::System.IConvertible)this.Value).ToType(conversionType, {Provider})", TypeFactory.GetType(SpecialType.Object), false).Value!;
    }

    [Template]
    public string ToStringTemplate(IFormatProvider? provider, [CompileTime] bool declaresToString, [CompileTime] bool referenceType)
    {
        if (declaresToString)
            return meta.This.ToString();

        if (referenceType)
            return (string)ExpressionFactory.Parse($"global::System.Convert.ToString((object?)this.Value, {Provider})", TypeFactory.GetType(SpecialType.String), false).Value!;

        return (string)ExpressionFactory.Parse($"((global::System.IConvertible)this.Value).ToString({Provider})", TypeFactory.GetType(SpecialType.String), false).Value!;
    }

    [Template]
    public dynamic TypedConversionTemplate(
        IFormatProvider?     provider,
        [CompileTime] string methodName,
        [CompileTime] IType  returnType,
        [CompileTime] bool   referenceType
    )
    {
        return ExpressionFactory.Parse(
            referenceType
                ? $"global::System.Convert.{methodName}((object?)this.Value, {Provider})"
                : $"((global::System.IConvertible)this.Value).{methodName}({Provider})",
            returnType,
            false
        ).Value!;
    }
}
