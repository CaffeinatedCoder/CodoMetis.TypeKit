using System.Numerics;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

[CompileTime]
internal sealed class MinMaxValueImplementationArguments
{
    public required INamedType   ValueType       { get; init; }
    public required INamedType   ValueObjectType { get; init; }
    public required IConstructor Constructor     { get; init; }
}

internal sealed partial class ValueObjectMinMaxValueAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType          = state.ValueType.GetTarget();
        var privateConstructor = state.PrivateConstructor.GetTarget();

        // Opportunistic: skipped when T has no MinValue/MaxValue, which is normal for string, Guid,
        // Uri and the like. Also skipped for a validated value object: T.MinValue is rarely a value
        // its Create accepts, and wrapping it through the private constructor would bypass the rules.
        if (state.Kind == ValueObjectKind.Validated || !HasMinMaxValue(valueType))
        {
            builder.SkipAspect();
            return;
        }

        builder.Tags = new MinMaxValueImplementationArguments
        {
            ValueType       = valueType,
            ValueObjectType = builder.Target,
            Constructor     = privateConstructor
        };

        builder.ImplementInterface(
            typeof(IMinMaxValue<>).ToNamedType().MakeGenericInstance(builder.Target),
            OverrideStrategy.Ignore
        );

        builder.IntroduceProperty(
            nameof(MinValueTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            p =>
            {
                p.Name = nameof(IMinMaxValue<int>.MinValue);
                p.Type = builder.Target;
                p.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        builder.IntroduceProperty(
            nameof(MaxValueTemplate),
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            p =>
            {
                p.Name = nameof(IMinMaxValue<int>.MaxValue);
                p.Type = builder.Target;
                p.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );
    }

    /// <summary>
    /// Whether the wrapped type has a static <c>MinValue</c> and <c>MaxValue</c> of its own type that the
    /// generated code can read. A member of that name of another type (<c>public const decimal
    /// MinValue</c> on a struct that is not a decimal) or one the value object cannot reach (private)
    /// failed inside the generated code (LAMA0611, CS1503 and CS0122).
    /// </summary>
    internal static bool HasMinMaxValue(INamedType valueType)
    {
        bool Reachable(IMember member) =>
            member.Accessibility == Accessibility.Public
         || (member.Accessibility is Accessibility.Internal or Accessibility.ProtectedInternal && member.BelongsToCurrentProject);

        bool HasMember(string name) =>
            valueType.Properties.Any(p => p.Name == name && p.IsStatic && p.Type.Equals(valueType) && p.GetMethod is { } getter && Reachable(p) && Reachable(getter)) ||
            valueType.Fields.Any(f => f.Name == name && f.IsStatic && f.Type.Equals(valueType) && Reachable(f));

        return HasMember(nameof(IMinMaxValue<int>.MinValue)) && HasMember(nameof(IMinMaxValue<int>.MaxValue));
    }
}
