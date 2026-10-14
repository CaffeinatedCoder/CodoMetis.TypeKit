using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectContractAspect
{
    [Template] public dynamic Value => meta.This._value;

    /// <summary>
    /// Wraps any value but null, as <c>Option.Some</c> does: <c>notnull</c> is only an annotation, and
    /// a value object over a reference type would wrap a null that <c>Value</c> promises it never
    /// holds. The check is compiled in only where the wrapped type can be null.
    /// </summary>
    [Template]
    public static dynamic From(dynamic? value, [CompileTime] IConstructor constructor, [CompileTime] bool refusesNull)
    {
        if (refusesNull)
        {
            if ((bool)ExpressionFactory.Parse("value is null", TypeFactory.GetType(SpecialType.Boolean), false).Value!)
                throw (Exception)ExpressionFactory.Parse(
                    $"global::CodoMetis.TypeKit.CompilerServices.GeneratedFactories.NullRefused<{ValueObjectTypes.SourceName(constructor.DeclaringType)}>(\"value\")").Value!;
        }

        return constructor.Invoke(value)!;
    }

    /// <summary>
    /// <c>Create</c> with the fault dropped. <c>ToOption</c> is called in its static form, so the
    /// generated code compiles whatever the consumer's file imports.
    /// </summary>
    [Template]
    public static dynamic TryFromTemplate(dynamic? value, [CompileTime] INamedType target) =>
        ExpressionFactory.Parse($"global::CodoMetis.TypeKit.Result.ToOption({ValueObjectTypes.SourceName(target)}.Create(value))").Value!;

    /// <summary><c>Create</c> of the value this instance holds.</summary>
    [Template]
    public dynamic RevalidateTemplate([CompileTime] INamedType target) =>
        ExpressionFactory.Parse($"{ValueObjectTypes.SourceName(target)}.Create(this.Value)").Value!;

    /// <summary>
    /// A deeper base's <c>Equals(TBase)</c>, declared by the value object so that its listed
    /// <c>IReimplementedEquatable&lt;TBase&gt;</c> maps to it: <c>Equals(object)</c>, as the one it hides does.
    /// </summary>
    [Template]
    public bool ReimplementedEqualsTemplate(dynamic? other) =>
        (bool)ExpressionFactory.Parse("this.Equals((object?)other)").Value!;

    /// <summary>The wrapped values compared, as the record's synthesized equality compares a field.</summary>
    [Template]
    public bool EqualsTemplate(dynamic? other, [CompileTime] INamedType valueType) =>
        (bool)ExpressionFactory.Parse(
            $"global::System.Object.ReferenceEquals(this, other) || (other is not null && global::System.Collections.Generic.EqualityComparer<{ValueObjectTypes.SourceName(valueType)}>.Default.Equals(this._value, other._value))"
        ).Value!;

    /// <summary>The wrapped value's hash code, which a default instance over a reference type has as 0.</summary>
    [Template]
    public int GetHashCodeTemplate([CompileTime] INamedType valueType) =>
        (int)ExpressionFactory.Parse(
            $"global::System.Collections.Generic.EqualityComparer<{ValueObjectTypes.SourceName(valueType)}>.Default.GetHashCode(this._value!)"
        ).Value!;

    /// <summary><c>Create</c> with the fault carried into the throw.</summary>
    [Template]
    public static dynamic FromKnownGoodTemplate(dynamic? value, [CompileTime] INamedType target, string? source = null) =>
        ExpressionFactory.Parse(
            $"global::CodoMetis.TypeKit.CompilerServices.GeneratedFactories.OrInvalidOperationException({ValueObjectTypes.SourceName(target)}.Create(value), source)"
        ).Value!;
}
