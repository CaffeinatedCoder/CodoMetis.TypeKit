using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectInterfaceAspect
{
    [Template] public dynamic Value => meta.This._value;

    [Template]
    public static dynamic From(dynamic? value, [CompileTime] IConstructor constructor) => constructor.Invoke(value)!;

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
            $"global::CodoMetis.TypeKit.ValueObjects.KnownGood.OrThrow({ValueObjectTypes.SourceName(target)}.Create(value), source)"
        ).Value!;
}
