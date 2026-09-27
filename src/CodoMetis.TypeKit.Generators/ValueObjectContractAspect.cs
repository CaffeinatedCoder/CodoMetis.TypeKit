using System.Numerics;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.DeclarationBuilders;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// <c>IValueObject&lt;TSelf, T&gt;</c> and <c>Value</c>; <c>From</c> for a plain value object,
/// <c>TryFrom</c> and <c>FromKnownGood</c> for a validated one; and the equality-operator interface.
/// </summary>
internal sealed partial class ValueObjectContractAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType = state.ValueType.GetTarget();

        builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IValueObject<,>)).MakeGenericInstance(builder.Target, valueType));

        // How run-time code that holds only a Type recognises the value object. Trimming removes
        // IValueObject<,> from a type's interfaces when nothing uses it, and keeps its attributes.
        builder.IntroduceAttribute(AttributeConstruction.Create(
            TypeFactory.GetNamedType(typeof(GeneratedValueObjectAttribute<,>)).MakeGenericInstance(builder.Target, valueType)));

        builder.IntroduceProperty(
            nameof(Value), IntroductionScope.Instance,
            buildProperty: property =>
            {
                property.Accessibility = Accessibility.Public;
                property.Type          = valueType;
                property.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        if (state.Kind == ValueObjectKind.Plain)
        {
            ImplementValueWrapper(builder, valueType, state.PrivateConstructor.GetTarget());
        }
        else
        {
            IntroduceTryFrom(builder, valueType);
            IntroduceFromKnownGood(builder, valueType);
        }

        builder.ImplementInterface(
            typeof(IEqualityOperators<,,>).ToNamedType().MakeGenericInstance(builder.Target, builder.Target, typeof(bool).ToNamedType()),
            OverrideStrategy.Ignore
        );
    }

    private static void ImplementValueWrapper(IAspectBuilder<INamedType> builder, IType valueType, IConstructor privateConstructor)
    {
        builder.IntroduceMethod(
            nameof(From),
            buildMethod: method =>
            {
                method.Accessibility      = Accessibility.Public;
                method.ReturnType         = builder.Target;
                method.IsStatic           = true;
                method.Parameters[0].Type = valueType;
                method.AddAttribute(CodeAnnotations.AggressiveInlining);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { constructor = privateConstructor, refusesNull = valueType.IsReferenceType == true }
        );

        builder.ImplementInterface(TypeFactory.GetNamedType(typeof(IPlainValueObject<,>)).MakeGenericInstance(builder.Target, valueType));
    }

    /// <summary>
    /// <c>TryFrom</c> in terms of the type's own <c>Create</c>, so it cannot drift from the rules.
    /// </summary>
    /// <remarks>
    /// <c>OverrideStrategy.Ignore</c>: a type that declares its own <c>TryFrom</c> keeps it, and this
    /// one is not generated.
    /// </remarks>
    private static void IntroduceTryFrom(IAspectBuilder<INamedType> builder, IType valueType) =>
        builder.IntroduceMethod(
            nameof(TryFromTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            buildMethod: method =>
            {
                method.Name               = "TryFrom";
                method.Accessibility      = Accessibility.Public;
                method.ReturnType         = TypeFactory.GetNamedType(typeof(Option<>)).MakeGenericInstance(builder.Target);
                method.Parameters[0].Type = valueType;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { target = builder.Target }
        );

    /// <summary>
    /// The factory for a caller that owns its input, so a refusal is the caller's own bug: a literal
    /// in source, or a value the caller has just produced.
    /// </summary>
    /// <remarks>
    /// Derived from <c>Create</c> like <c>TryFrom</c>, so the fault reaches the exception message.
    /// The second parameter is filled in by the C# compiler with the caller's argument expression,
    /// which lets the message name the offending expression without ever containing the value.
    /// </remarks>
    private static void IntroduceFromKnownGood(IAspectBuilder<INamedType> builder, IType valueType) =>
        builder.IntroduceMethod(
            nameof(FromKnownGoodTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            buildMethod: method =>
            {
                method.Name               = "FromKnownGood";
                method.Accessibility      = Accessibility.Public;
                method.ReturnType         = builder.Target;
                method.Parameters[0].Type = valueType;
                method.Parameters[1].AddAttribute(CodeAnnotations.CallerArgumentExpressionOfValue);
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            },
            args: new { target = builder.Target }
        );
}
