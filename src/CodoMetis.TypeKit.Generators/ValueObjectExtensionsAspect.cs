using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// A <c>{TSelf}Extensions</c> class beside the value object with <c>GetValue()</c> and
/// <c>ValueOrNull()</c>, both marked <c>[TranslatedAsUnderlyingValue]</c> so a query translates them
/// to the column itself.
/// </summary>
/// <remarks>
/// Extension methods need a namespace-level class, so a nested value object's class is named after
/// its whole nesting chain: <c>Order.Id</c> gets <c>OrderIdExtensions</c>, and <c>Customer.Id</c> in
/// the same namespace no longer asks for the same name. Two <c>IdExtensions</c> crashed Metalama
/// (LAMA0001), and a declared type of that name failed the aspect (LAMA0041). A name that is taken
/// is CMTK1007 now, naming what takes it.
/// </remarks>
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

        var className = ExtensionClassName(builder.Target);

        if (NameOwner(builder.Target, className) is { } owner)
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

    /// <summary><c>OrderIdExtensions</c> for <c>OrderId</c>, and for <c>Order.Id</c>.</summary>
    private static string ExtensionClassName(INamedType type)
    {
        var name = type.Name;

        for (var container = type.DeclaringType; container is not null; container = container.DeclaringType)
            name = container.Name + name;

        return name + "Extensions";
    }

    /// <summary>
    /// What already has <paramref name="className"/> in the value object's namespace: a declared type,
    /// or another value object whose own extension class would get the same name. Every aspect
    /// instance runs this same check, so both value objects of a clash report it.
    /// </summary>
    private static string? NameOwner(INamedType valueObject, string className)
    {
        var @namespace = valueObject.ContainingNamespace;

        if (@namespace.Types.OfName(className).FirstOrDefault() is { } declared)
            return $"the type '{declared.ToDisplayString()}'";

        var rival = AllTypes(@namespace.Types)
            .FirstOrDefault(type => !type.Equals(valueObject)
                                 && type.TypeKind is TypeKind.Struct or TypeKind.Class
                                 && !type.IsAbstract
                                 && ValueObjectTypes.Markers(type).Count > 0
                                 && NamespaceLevelAccessibility(type) is not null
                                 && ExtensionClassName(type) == className);

        return rival is null ? null : $"the extension class of '{rival.ToDisplayString()}'";
    }

    private static IEnumerable<INamedType> AllTypes(IEnumerable<INamedType> types) =>
        types.SelectMany(type => AllTypes(type.Types).Prepend(type));

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
