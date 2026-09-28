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
    Uri,
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
    /// C# that turns the text <c>s</c> (and <c>provider</c>) into the wrapped type, or throws
    /// <c>GeneratedParsing.Unreadable</c>: a <c>FormatException</c> naming the value object and the
    /// wrapped type, never the input, and with no inner exception. The wrapped type's own
    /// <c>Parse</c> quoted the input ("The input string 'SECRET' was not in a correct format."), so it
    /// is never called where a <c>TryParse</c> exists. That is called through <c>GeneratedParsing</c>,
    /// because it may be an explicit interface implementation that <c>T.TryParse(...)</c> cannot reach
    /// (<c>bool</c> is one).
    /// </summary>
    public string InnerParse(string input = "s")
    {
        var valueType   = ValueObjectTypes.SourceName(ValueType);
        var unreadable  = $"throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<{ValueObjectTypes.SourceName(ValueObjectType)}, {valueType}>()";
        const string at = "global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing";

        return Strategy switch
        {
            ValueParseStrategy.String => input,
            ValueParseStrategy.Parsable => $"({at}.TryParse<{valueType}>({input}, {Provider}, out var __parsed) ? __parsed : {unreadable})",
            ValueParseStrategy.SpanParsable => $"({at}.TryParseSpan<{valueType}>({input}, {Provider}, out var __parsed) ? __parsed : {unreadable})",
            // By name, as Enum.Parse does, but with the exception IParsable.Parse documents.
            ValueParseStrategy.Enum => $"(global::System.Enum.TryParse<{valueType}>({input}, out var __parsed) ? __parsed : {unreadable})",
            // Relative or absolute, as the serializer reads a Uri. The constructor took "/orders/7" for
            // file:///orders/7 on macOS and Linux and refused "orders/7", which JSON reads as relative.
            ValueParseStrategy.Uri => $"(global::System.Uri.TryCreate({input}, global::System.UriKind.RelativeOrAbsolute, out var __parsed) ? __parsed : {unreadable})",
            // Through GeneratedParsing, whose lookup is the trim-safe one: TypeDescriptor.GetConverter
            // requires unreferenced code, so trimming and Native AOT warned about this call.
            ValueParseStrategy.TypeConverter => $"{at}.ConvertFromString<{ValueObjectTypes.SourceName(ValueObjectType)}, {valueType}>({input}, provider)",
            ValueParseStrategy.StaticParse => $"{at}.Guarded<{ValueObjectTypes.SourceName(ValueObjectType)}, {valueType}>({input}, static __text => {valueType}.Parse(__text))",
            ValueParseStrategy.StringConstructor => $"{at}.Guarded<{ValueObjectTypes.SourceName(ValueObjectType)}, {valueType}>({input}, static __text => new {valueType}(__text))",
            _ => throw new NotSupportedException(Strategy.ToString())
        };
    }

    /// <summary>
    /// C# for <c>TryParse</c>: the wrapped type's own <c>TryParse</c> where it has one, then
    /// <c>__TryFromText</c>, so nothing throws. <see langword="null"/> for a strategy without one, whose
    /// parse is caught instead.
    /// </summary>
    public string? InnerTryParse(string input = "s")
    {
        var valueType   = ValueObjectTypes.SourceName(ValueType);
        const string at = "global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing";

        var parsed = Strategy switch
        {
            ValueParseStrategy.Parsable => $"{at}.TryParse<{valueType}>({input}, {Provider}, out var innerValue)",
            ValueParseStrategy.SpanParsable => $"{at}.TryParseSpan<{valueType}>({input}, {Provider}, out var innerValue)",
            // Enum.TryParse answers false for null, an unknown name and an empty string.
            ValueParseStrategy.Enum => $"global::System.Enum.TryParse<{valueType}>({input}, out var innerValue)",
            ValueParseStrategy.Uri => $"global::System.Uri.TryCreate({input}, global::System.UriKind.RelativeOrAbsolute, out var innerValue)",
            _ => null
        };

        return parsed is null ? null : $"({parsed} && {TryFromText}(innerValue, out result))";
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

        var supportsUtf8 = SupportsUtf8(valueType);

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

    // A hand-written Parse or TryParse with these signatures is CMTK1011, so Fail is a backstop, never a
    // silent keep of an entry point that need not apply Create.
    private static void IntroduceParse(IAspectBuilder<INamedType> builder, string template, string name) =>
        builder.IntroduceMethod(
            template,
            IntroductionScope.Static,
            OverrideStrategy.Fail,
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
            OverrideStrategy.Fail,
            method =>
            {
                method.Name                      = name;
                method.Parameters["result"].Type = builder.Target;
                method.AddAttribute(CodeAnnotations.CompilerGenerated);
            });

    /// <summary>Whether <c>IUtf8SpanParsable</c> is generated: the wrapped type implements it.</summary>
    internal static bool SupportsUtf8(INamedType valueType) =>
        valueType.IsConvertibleTo(typeof(IUtf8SpanParsable<>).ToNamedType().MakeGenericInstance(valueType));

    /// <summary>How the wrapped type is parsed, which also decides what is generated (<see cref="ValueObjectDeclaration"/> reads it too).</summary>
    internal static ValueParseStrategy ResolveStrategy(INamedType valueType)
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

        // Before the string constructor, which Uri has too: relative or absolute, as JSON reads it.
        if (valueType.Equals(typeof(Uri)))
            return ValueParseStrategy.Uri;

        if (valueType.Attributes.Any(attribute => attribute.Type.IsConvertibleTo(typeof(TypeConverterAttribute))))
            return ValueParseStrategy.TypeConverter;

        if (valueType.Constructors.Any(constructor => constructor is { Accessibility: Accessibility.Public, Parameters: [{ Type.SpecialType: SpecialType.String }] }))
            return ValueParseStrategy.StringConstructor;

        if (valueType.Methods.Any(method => method is { Name: "Parse", IsStatic: true, Accessibility: Accessibility.Public, Parameters: [{ Type.SpecialType: SpecialType.String }] }))
            return ValueParseStrategy.StaticParse;

        return ValueParseStrategy.Unsupported;
    }
}
