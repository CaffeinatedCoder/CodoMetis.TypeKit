using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// A <c>{TSelf}Extensions</c> class beside the value object with <c>GetValue()</c> and
/// <c>ValueOrNull()</c>, both marked <c>[TranslatedAsUnderlyingValue]</c> so a query translates them
/// to the column itself.
/// </summary>
internal sealed class ValueObjectExtensionsAspect : TypeAspect
{
    [Template]
    public static dynamic? GetValueExtension(dynamic? instance) => instance?.Value;

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        // The class sits at namespace level, so it can neither be more visible than the value object
        // (CS0051) nor refer to one nested as private or protected (CS0122).
        if (NamespaceLevelAccessibility(builder.Target) is not { } accessibility)
        {
            builder.SkipAspect();
            return;
        }

        var valueType = state.ValueType.GetTarget();

        var extensionClass = builder.With(builder.Target.ContainingNamespace)
                                    .IntroduceClass(
                                        $"{builder.Target.Name}Extensions",
                                        buildType: type =>
                                        {
                                            type.Accessibility = accessibility;
                                            type.IsStatic      = true;
                                            type.IsPartial     = true;
                                            type.AddAttribute(CodeAnnotations.CompilerGenerated);
                                        });

        extensionClass.IntroduceMethod(
            nameof(GetValueExtension), IntroductionScope.Static,
            buildMethod: method =>
            {
                method.Name                 = "GetValue";
                method.Accessibility        = accessibility;
                method.ReturnType           = valueType.ToNonNullable();
                method.Parameters[0].Type   = builder.Target.ToNonNullable();
                method.Parameters[0].IsThis = true;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
                method.AddAttribute(CodeAnnotations.TranslatedAsUnderlyingValue);
            }
        );

        extensionClass.IntroduceMethod(
            nameof(GetValueExtension), IntroductionScope.Static,
            buildMethod: method =>
            {
                // Not "GetValueOrDefault": on a struct value object the parameter is Nullable<T>,
                // whose own instance method of that name always wins over an extension, so the
                // companion would be uncallable.
                method.Name                 = "ValueOrNull";
                method.Accessibility        = accessibility;
                method.ReturnType           = valueType.ToNullable();
                method.Parameters[0].Type   = builder.Target.ToNullable();
                method.Parameters[0].IsThis = true;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
                method.AddAttribute(CodeAnnotations.TranslatedAsUnderlyingValue);
            }
        );
    }

    /// <summary>
    /// Public when the value object and every type it is nested in are public, internal when any of
    /// them is internal, and <see langword="null"/> when one is private or protected.
    /// </summary>
    private static Accessibility? NamespaceLevelAccessibility(INamedType type)
    {
        var result = Accessibility.Public;

        for (INamedType? current = type; current is not null; current = current.DeclaringType)
        {
            switch (current.Accessibility)
            {
                case Accessibility.Public:
                    break;
                case Accessibility.Internal:
                    result = Accessibility.Internal;
                    break;
                default:
                    return null;
            }
        }

        return result;
    }
}
