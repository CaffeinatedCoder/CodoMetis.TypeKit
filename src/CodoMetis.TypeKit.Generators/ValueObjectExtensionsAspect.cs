using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// A <c>{TSelf}Extensions</c> class beside the value object with <c>GetValue()</c> and
/// <c>ValueOrNull()</c>, both marked <c>[TranslatedAsUnderlyingValue]</c> so a query translates them
/// to the column itself.
/// </summary>
/// <remarks>
/// <para>
/// Extension methods need a namespace-level class, so a nested value object's class is named after
/// its whole nesting chain: <c>Order.Id</c> gets <c>OrderIdExtensions</c>, and <c>Customer.Id</c> in
/// the same namespace no longer asks for the same name. Two <c>IdExtensions</c> crashed Metalama
/// (LAMA0001), and a declared type of that name failed the aspect (LAMA0041). A name that is taken
/// is CMTK1007 now, naming what takes it.
/// </para>
/// <para>
/// Who takes it is answered by the fabric (<see cref="CompanionClass.NameOwner"/>) and arrives in the
/// aspect state. Asked here, the answer raced the sibling instances introducing their classes into
/// the same namespace, and one build in nine introduced a class beside a declared one.
/// </para>
/// </remarks>
internal sealed class ValueObjectExtensionsAspect : TypeAspect
{
    [Template]
    public static dynamic? GetValueExtension(dynamic? instance) => instance?.Value;

    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        if (CompanionClass.NamespaceLevelAccessibility(builder.Target) is not { } accessibility)
        {
            builder.SkipAspect();
            return;
        }

        var className = state.ExtensionClassName;

        if (state.ExtensionClassNameOwner is { } owner)
        {
            builder.Diagnostics.Report(AspectDiagnostics.ExtensionClassNameTaken.WithArguments((builder.Target, className, owner)));
            builder.SkipAspect();
            return;
        }

        var valueType = state.ValueType.GetTarget();

        var extensionClass = builder.With(builder.Target.ContainingNamespace)
                                    .IntroduceClass(
                                        className,
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

}
