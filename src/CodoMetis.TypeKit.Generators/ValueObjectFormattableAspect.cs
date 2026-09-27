using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

[CompileTime]
internal enum ValueFormatStrategy
{
    String,
    SpanFormattable,
    Formattable,
    FallbackToString
}

[CompileTime]
internal sealed class FormattableImplementationArguments
{
    public required INamedType          ValueType        { get; init; }
    public required INamedType          ValueObjectType  { get; init; }
    public required ValueFormatStrategy Strategy         { get; init; }
    public required bool                SupportsUtf8Span { get; init; }
}

internal sealed partial class ValueObjectFormattableAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType    = state.ValueType.GetTarget();
        var strategy     = ResolveStrategy(valueType);
        var supportsUtf8 = valueType.IsConvertibleTo(typeof(IUtf8SpanFormattable));

        builder.Tags = new FormattableImplementationArguments
                       {
                           ValueType        = valueType,
                           ValueObjectType  = builder.Target,
                           Strategy         = strategy,
                           SupportsUtf8Span = supportsUtf8
                       };

        // Always implement IFormattable — even FallbackToString gets
        // a well-behaved implementation that simply ignores the args.
        builder.ImplementInterface(typeof(IFormattable), OverrideStrategy.Ignore);

        builder.IntroduceMethod(
            nameof(FormattableToStringTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Ignore,
            m =>
            {
                m.Name = nameof(IFormattable.ToString);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // Override the parameterless ToString() so it stays consistent
        // with the formattable overload. Records already generate one,
        // so OverrideStrategy.Override is intentional here.
        builder.IntroduceMethod(
            nameof(ToStringOverrideTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Override,
            m =>
            {
                m.Name = nameof(IFormattable.ToString);
                m.AddAttribute(CodeAnnotations.CompilerGenerated);
            }
        );

        // Mirror the IParsable → ISpanParsable layering pattern exactly.
        if (strategy == ValueFormatStrategy.SpanFormattable)
        {
            builder.ImplementInterface(typeof(ISpanFormattable), OverrideStrategy.Ignore);

            builder.IntroduceMethod(
                nameof(TryFormatTemplate),
                IntroductionScope.Instance,
                OverrideStrategy.Ignore,
                m =>
                {
                    m.Name = nameof(ISpanFormattable.TryFormat);
                    m.AddAttribute(CodeAnnotations.CompilerGenerated);
                }
            );
        }

        if (supportsUtf8)
        {
            builder.ImplementInterface(typeof(IUtf8SpanFormattable), OverrideStrategy.Ignore);

            builder.IntroduceMethod(
                nameof(TryFormatUtf8Template),
                IntroductionScope.Instance,
                OverrideStrategy.Ignore,
                m =>
                {
                    m.Name = nameof(IUtf8SpanFormattable.TryFormat);
                    m.AddAttribute(CodeAnnotations.CompilerGenerated);
                }
            );
        }
    }

    private static ValueFormatStrategy ResolveStrategy(INamedType valueType)
    {
        if (valueType.SpecialType == SpecialType.String)
            return ValueFormatStrategy.String;

        // ISpanFormattable extends IFormattable, so probe for it first.
        if (valueType.IsConvertibleTo(typeof(ISpanFormattable)))
            return ValueFormatStrategy.SpanFormattable;

        if (valueType.IsConvertibleTo(typeof(IFormattable)))
            return ValueFormatStrategy.Formattable;

        return ValueFormatStrategy.FallbackToString;
    }
}
