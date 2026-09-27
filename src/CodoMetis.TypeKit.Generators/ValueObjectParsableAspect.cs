using System.ComponentModel;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

[CompileTime]
internal enum ValueParseStrategy
{
    String,
    SpanParsable,
    Parsable,
    Enum,
    TypeConverter,
    StaticParse,
    StringConstructor,
    Unsupported
}

[CompileTime]
internal sealed class ParsableImplementationArguments
{
    public required INamedType         ValueType        { get; init; }
    public required INamedType         ValueObjectType  { get; init; }
    public required ValueParseStrategy Strategy         { get; init; }
    public required bool               SupportsUtf8Span { get; init; }

    /// <summary>The name of <see cref="ValueObjectAspectState.FromText"/>.</summary>
    public required string FromText { get; init; }

    /// <summary>The name of <see cref="ValueObjectAspectState.TryFromText"/>.</summary>
    public required string TryFromText { get; init; }

    /// <summary>
    /// The provider the generated parsing hands to the wrapped type. A null provider means the
    /// invariant culture, not the current one as in the BCL: the generated <c>ToString()</c> is
    /// invariant, so <c>Parse(x.ToString(), null)</c> has to round-trip, and with the current
    /// culture it parsed "1.5" as 15 in de-DE. A provider that is given is used as given.
    /// </summary>
    public const string Provider = "(provider ?? global::System.Globalization.CultureInfo.InvariantCulture)";

    /// <summary>
    /// C# that turns the text <c>s</c> (and <c>provider</c>) into the wrapped type. The wrapped
    /// type's own <c>Parse</c> is called through <c>GeneratedParsing</c>, because it may be an
    /// explicit interface implementation that <c>T.Parse(...)</c> cannot reach (<c>bool</c> is one).
    /// </summary>
    public string InnerParse(string input = "s")
    {
        var valueType = ValueObjectTypes.SourceName(ValueType);

        return Strategy switch
        {
            ValueParseStrategy.String => input,
            ValueParseStrategy.Parsable => $"global::CodoMetis.TypeKit.ValueObjects.GeneratedParsing.Parse<{valueType}>({input}, {Provider})",
            ValueParseStrategy.SpanParsable => $"global::CodoMetis.TypeKit.ValueObjects.GeneratedParsing.ParseSpan<{valueType}>({input}, {Provider})",
            // By name, as Enum.Parse does, but with the exception IParsable.Parse documents. The
            // refused text stays out of the message, as in every other refusal.
            ValueParseStrategy.Enum =>
                $"(global::System.Enum.TryParse<{valueType}>({input}, out var __parsed) ? __parsed : throw new global::System.FormatException(\"The input is not a name of {ValueType.Name}.\"))",
            ValueParseStrategy.TypeConverter =>
                $"({valueType})global::System.ComponentModel.TypeDescriptor.GetConverter(typeof({valueType}))"
              + $".ConvertFromString(null, provider as global::System.Globalization.CultureInfo ?? global::System.Globalization.CultureInfo.InvariantCulture, {input})!",
            ValueParseStrategy.StaticParse => $"{valueType}.Parse({input})",
            ValueParseStrategy.StringConstructor => $"new {valueType}({input})",
            _ => throw new NotSupportedException(Strategy.ToString())
        };
    }
}

/// <summary>
/// <c>IParsable&lt;TSelf&gt;</c>, and <c>ISpanParsable</c>/<c>IUtf8SpanParsable</c> where the wrapped
/// type has them, by parsing the wrapped type and wrapping the result.
/// </summary>
/// <remarks>
/// The wrapping goes through <see cref="ValueObjectAspectState.FromText"/> and
/// <see cref="ValueObjectAspectState.TryFromText"/>, so a validated value object applies
/// <c>Create</c>: <c>Parse</c> throws <c>FormatException</c> and <c>TryParse</c> returns
/// <see langword="false"/> on a refusal.
/// </remarks>
internal sealed partial class ValueObjectParsableAspect : TypeAspect
{
    public override void BuildAspect(IAspectBuilder<INamedType> builder)
    {
        if (!builder.AspectInstance.Predecessors.TryGetState<ValueObjectAspectState>(out var state))
            return;

        var valueType = state.ValueType.GetTarget();
        var strategy  = ResolveStrategy(valueType);

        if (strategy is ValueParseStrategy.Unsupported)
        {
            builder.SkipAspect();
            return;
        }

        var supportsUtf8 = valueType.IsConvertibleTo(typeof(IUtf8SpanParsable<>).ToNamedType().MakeGenericInstance(valueType));

        builder.Tags = new ParsableImplementationArguments
        {
            ValueType        = valueType,
            ValueObjectType  = builder.Target,
            Strategy         = strategy,
            SupportsUtf8Span = supportsUtf8,
            FromText         = state.FromText.GetTarget().Name,
            TryFromText      = state.TryFromText.GetTarget().Name
        };

        builder.ImplementInterface(typeof(IParsable<>).ToNamedType().MakeGenericInstance(builder.Target), OverrideStrategy.Ignore);
        IntroduceParse(builder, nameof(ParseTemplate), nameof(IParsable<>.Parse));
        IntroduceTryParse(builder, nameof(TryParseTemplate), nameof(IParsable<>.TryParse));

        if (strategy == ValueParseStrategy.SpanParsable)
        {
            builder.ImplementInterface(typeof(ISpanParsable<>).ToNamedType().MakeGenericInstance(builder.Target), OverrideStrategy.Ignore);
            IntroduceParse(builder, nameof(SpanParseTemplate), nameof(ISpanParsable<>.Parse));
            IntroduceTryParse(builder, nameof(SpanTryParseTemplate), nameof(ISpanParsable<>.TryParse));
        }

        if (supportsUtf8)
        {
            builder.ImplementInterface(typeof(IUtf8SpanParsable<>).ToNamedType().MakeGenericInstance(builder.Target), OverrideStrategy.Ignore);
            IntroduceParse(builder, nameof(Utf8SpanParseTemplate), nameof(IUtf8SpanParsable<>.Parse));
            IntroduceTryParse(builder, nameof(Utf8SpanTryParseTemplate), nameof(IUtf8SpanParsable<>.TryParse));
        }
    }

    private static void IntroduceParse(IAspectBuilder<INamedType> builder, string template, string name) =>
        builder.IntroduceMethod(
            template,
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            method =>
            {
                method.Name       = name;
                method.ReturnType = builder.Target;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            });

    private static void IntroduceTryParse(IAspectBuilder<INamedType> builder, string template, string name) =>
        builder.IntroduceMethod(
            template,
            IntroductionScope.Static,
            OverrideStrategy.Ignore,
            method =>
            {
                method.Name                      = name;
                method.Parameters["result"].Type = builder.Target;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            });

    private static ValueParseStrategy ResolveStrategy(INamedType valueType)
    {
        if (valueType.SpecialType == SpecialType.String)
            return ValueParseStrategy.String;

        // ISpanParsable extends IParsable, so probe for it first.
        if (valueType.IsConvertibleTo(typeof(ISpanParsable<>).ToNamedType().MakeGenericInstance(valueType)))
            return ValueParseStrategy.SpanParsable;

        if (valueType.IsConvertibleTo(typeof(IParsable<>).ToNamedType().MakeGenericInstance(valueType)))
            return ValueParseStrategy.Parsable;

        // An enum is IConvertible, but Convert.ChangeType cannot make one from a string: a strategy
        // built on it threw for every input while the type advertised IParsable. Nothing else that is
        // convertible lacks ISpanParsable, so enums are the only type that gets here this way.
        if (valueType.TypeKind == TypeKind.Enum)
            return ValueParseStrategy.Enum;

        if (valueType.Attributes.Any(attribute => attribute.Type.IsConvertibleTo(typeof(TypeConverterAttribute))))
            return ValueParseStrategy.TypeConverter;

        if (valueType.Constructors.Any(constructor => constructor is { Accessibility: Accessibility.Public, Parameters: [{ Type.SpecialType: SpecialType.String }] }))
            return ValueParseStrategy.StringConstructor;

        if (valueType.Methods.Any(method => method is { Name: "Parse", IsStatic: true, Accessibility: Accessibility.Public, Parameters: [{ Type.SpecialType: SpecialType.String }] }))
            return ValueParseStrategy.StaticParse;

        return ValueParseStrategy.Unsupported;
    }
}
