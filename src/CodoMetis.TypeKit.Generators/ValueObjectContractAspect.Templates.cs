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
                throw new ArgumentNullException("value");
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

    /// <summary><c>Create</c> with the fault carried into the throw.</summary>
    [Template]
    public static dynamic FromKnownGoodTemplate(dynamic? value, [CompileTime] INamedType target, string? source = null) =>
        ExpressionFactory.Parse(
            $"global::CodoMetis.TypeKit.CompilerServices.GeneratedFactories.OrInvalidOperationException({ValueObjectTypes.SourceName(target)}.Create(value), source)"
        ).Value!;
}
