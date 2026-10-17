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
    /// invariant, so what it writes has to read back the same wherever the process runs, and with the
    /// current culture "1.5" was parsed as 15 in de-DE. It is as lossless as the wrapped type's own
    /// default text: <c>TimeOnly</c>, <c>DateTime</c> and <c>DateTimeOffset</c> lose seconds or a
    /// fraction in it. A provider that is given is used as given.
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
            ValueParseStrategy.SpanParsable => InnerSpanParse(input),
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
            ValueParseStrategy.SpanParsable => SpanTryParse(input, "innerValue"),
            // Enum.TryParse answers false for null, an unknown name and an empty string.
            ValueParseStrategy.Enum => $"global::System.Enum.TryParse<{valueType}>({input}, out var innerValue)",
            ValueParseStrategy.Uri => $"global::System.Uri.TryCreate({input}, global::System.UriKind.RelativeOrAbsolute, out var innerValue)",
            _ => null
        };

        return parsed is null ? null : $"({parsed} && {TryFromText}(innerValue, out result))";
    }

    /// <summary>
    /// C# that turns the characters <c>s</c> into the wrapped type through its own
    /// <c>ISpanParsable.TryParse</c>, or throws <c>GeneratedParsing.Unreadable</c>, as
    /// <see cref="InnerParse"/> does for text. The span <c>Parse</c> of every value object over a
    /// span-parsable type, whatever its strategy for text: a <see cref="string"/>'s parses its text as it
    /// is, and its characters through <c>string</c>'s own span parsing.
    /// </summary>
    public string InnerSpanParse(string input = "s") =>
        $"({SpanTryParse(input, "__parsed")} ? __parsed : throw global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<{ValueObjectTypes.SourceName(ValueObjectType)}, {ValueObjectTypes.SourceName(ValueType)}>())";

    /// <summary>C# for the span <c>TryParse</c>: the wrapped type's own, then <c>__TryFromText</c>, so nothing throws.</summary>
    public string InnerSpanTryParse(string input = "s") => $"({SpanTryParse(input, "innerValue")} && {TryFromText}(innerValue, out result))";

    private string SpanTryParse(string input, string parsed) =>
        $"global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseSpan<{ValueObjectTypes.SourceName(ValueType)}>({input}, {Provider}, out var {parsed})";
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

        var supportsSpan = SupportsSpan(valueType);
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

        // By the wrapped type, not the strategy: the string strategy, picked first, dropped string's own
        // ISpanParsable (found by the prober, claim C11, 2026-10-17).
        if (supportsSpan)
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

    /// <summary>Whether <c>ISpanParsable</c> is generated: the wrapped type implements it, whatever its strategy for text.</summary>
    internal static bool SupportsSpan(INamedType valueType) =>
        valueType.IsConvertibleTo(typeof(ISpanParsable<>).ToNamedType().MakeGenericInstance(valueType));

    /// <summary>Whether <c>IUtf8SpanParsable</c> is generated: the wrapped type implements it.</summary>
    internal static bool SupportsUtf8(INamedType valueType) =>
        valueType.IsConvertibleTo(typeof(IUtf8SpanParsable<>).ToNamedType().MakeGenericInstance(valueType));

    /// <summary>How the wrapped type is parsed, which also decides what is generated (<see cref="ValueObjectDeclaration"/> reads it too).</summary>
    internal static ValueParseStrategy ResolveStrategy(INamedType valueType)
    {
        if (valueType.SpecialType == SpecialType.String)
            return ValueParseStrategy.String;

        // ISpanParsable extends IParsable, so probe for it first.
        if (SupportsSpan(valueType))
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

        // Only a type the generated call can make: an abstract class's public constructor is for the
        // classes derived from it (LAMA0611, CS0144).
        if (!valueType.IsAbstract && valueType.Constructors.Any(constructor => constructor is { Accessibility: Accessibility.Public, Parameters: [{ } text] } && TakesText(text)))
            return ValueParseStrategy.StringConstructor;

        // Callable as the generated code calls it, Type.Parse(text), and giving the wrapped type back. Not
        // a generic one, such as Enum.Parse<TEnum>(string), whose type argument the generated call cannot
        // infer (LAMA0611, CS0411); not one that returns a type the wrapped one cannot be assigned from (a
        // static int Parse(string) on a class failed as CS0029); not a static virtual one of an interface,
        // which only a type parameter can call (CS8926). Assignable as the generated lambda's return is, a
        // user-defined implicit operator included: the default conversion kind, C#'s `is`, left it out, and a
        // Parse giving a Circle that converts to Shape lost IParsable where 2.0.0 generated it (CS0117 in a
        // consumer's Parse call, reported 2026-10-14).
        if (valueType.Methods.Any(method => method is { Name: "Parse", IsStatic: true, IsVirtual: false, Accessibility: Accessibility.Public, TypeParameters.Count: 0, Parameters: [{ } text] }
                                         && TakesText(text)
                                         && method.ReturnType.IsConvertibleTo(valueType, ConversionKind.Implicit)))
            return ValueParseStrategy.StaticParse;

        return ValueParseStrategy.Unsupported;
    }

    /// <summary>
    /// A string parameter the generated call can pass the text to as it is: not <c>ref</c> or
    /// <c>out</c>, which failed inside the generated code (LAMA0611, CS1620).
    /// </summary>
    private static bool TakesText(IParameter parameter) =>
        parameter is { Type.SpecialType: SpecialType.String, RefKind: RefKind.None or RefKind.In };
}
