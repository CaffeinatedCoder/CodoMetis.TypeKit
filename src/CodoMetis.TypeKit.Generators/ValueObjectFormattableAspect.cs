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

        // The formatting seam: a hand-written ToString() is kept, and nothing here is generated. The
        // interfaces would reach past it: interpolation, string.Format and Convert.ToString call
        // IFormattable.ToString(format, provider) or TryFormat when they exist, which printed the
        // wrapped value of a type whose own ToString() hid it ("***"), and the override below replaced
        // the hand-written ToString() outright, without a word. JSON and the type converter write the
        // wrapped value itself, so they are unaffected.
        if (state.DeclaresToString)
        {
            builder.SkipAspect();
            return;
        }

        var valueType    = state.ValueType.GetTarget();
        var strategy     = ResolveStrategy(valueType);
        var supportsUtf8 = SupportsUtf8(valueType);

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

        // A hand-written one is CMTK1011, so Fail is a backstop, never a silent keep.
        builder.IntroduceMethod(
            nameof(FormattableToStringTemplate),
            IntroductionScope.Instance,
            OverrideStrategy.Fail,
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
                OverrideStrategy.Fail,
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
                OverrideStrategy.Fail,
                m =>
                {
                    m.Name = nameof(IUtf8SpanFormattable.TryFormat);
                    m.AddAttribute(CodeAnnotations.CompilerGenerated);
                }
            );
        }
    }

    /// <summary>
    /// C# that formats the wrapped value <paramref name="value"/> with <paramref name="format"/> and
    /// <paramref name="provider"/>, as the generated <c>ToString(format, provider)</c> does. The type
    /// converter writes the wrapped value through it too, so it keeps doing so for a value object that
    /// declares its own <c>ToString()</c> and gets no formatting interfaces.
    /// </summary>
    internal static string WrappedText(INamedType valueType, string value, string format, string provider) =>
        ResolveStrategy(valueType) switch
        {
            ValueFormatStrategy.String => $"({value} ?? string.Empty)",
            ValueFormatStrategy.Formattable or ValueFormatStrategy.SpanFormattable => $"global::CodoMetis.TypeKit.CompilerServices.GeneratedFormatting.ToString({value}, {format}, {provider})",
            // No formatting knowledge: the format and the provider have nothing to apply to.
            _ => $"({value}.ToString() ?? string.Empty)"
        };

    /// <summary>Whether <c>IUtf8SpanFormattable</c> is generated: the wrapped type implements it.</summary>
    internal static bool SupportsUtf8(INamedType valueType) => valueType.IsConvertibleTo(typeof(IUtf8SpanFormattable));

    /// <summary>How the wrapped value is formatted, which also decides what is generated (<see cref="ValueObjectDeclaration"/> reads it too).</summary>
    internal static ValueFormatStrategy ResolveStrategy(INamedType valueType)
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
