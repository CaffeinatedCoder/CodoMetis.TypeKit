using System.Diagnostics.CodeAnalysis;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectParsableAspect
{
    // The inner parse is built as C# text (ParsableImplementationArguments.InnerParse): TryParse
    // has an out parameter no Metalama invocation API can pass, and the wrapped type's own members
    // are called through GeneratedParsing, which also reaches an explicit implementation.

    [Template]
    public static dynamic ParseTemplate(string s, IFormatProvider? provider)
    {
        var tag = (ParsableImplementationArguments)meta.Tags.Source!;

        // IParsable.Parse takes a non-null string. Without this, a string-backed value object
        // would wrap null.
        ArgumentNullException.ThrowIfNull(s);

        return ExpressionFactory.Parse($"{meta.CompileTime(tag.FromText)}({meta.CompileTime(tag.InnerParse())})", tag.ValueObjectType, false).Value!;
    }

    [Template]
    public static bool TryParseTemplate(
        [NotNullWhen(true)] string?        s,
        IFormatProvider?                   provider,
        [MaybeNullWhen(false)] out dynamic result
    )
    {
        var tag = (ParsableImplementationArguments)meta.Tags.Source!;

        result = meta.Default(tag.ValueObjectType);

        if (meta.CompileTime(tag.InnerTryParse() is not null))
            return (bool)ExpressionFactory.Parse(tag.InnerTryParse()!, TypeFactory.GetType(SpecialType.Boolean), false).Value!;

        if (meta.CompileTime(tag.Strategy == ValueParseStrategy.String))
        {
            return (bool)ExpressionFactory.Parse($"(s is not null && {tag.TryFromText}(s, out result))", TypeFactory.GetType(SpecialType.Boolean), false).Value!;
        }

        // The remaining strategies have no TryParse of their own: their inner parse throws.
        if (s is null) return false;

        try
        {
            return (bool)ExpressionFactory.Parse($"{tag.TryFromText}({tag.InnerParse()}, out result)", TypeFactory.GetType(SpecialType.Boolean), false).Value!;
        }
        catch
        {
            result = meta.Default(tag.ValueObjectType);
            return false;
        }
    }

    [Template]
    public static dynamic SpanParseTemplate(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        var tag = (ParsableImplementationArguments)meta.Tags.Source!;

        return ExpressionFactory.Parse($"{meta.CompileTime(tag.FromText)}({meta.CompileTime(tag.InnerParse())})", tag.ValueObjectType, false).Value!;
    }

    [Template]
    public static bool SpanTryParseTemplate(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out dynamic result)
    {
        var tag = (ParsableImplementationArguments)meta.Tags.Source!;

        result = meta.Default(tag.ValueObjectType);

        // Only generated for the span-parsable strategy, which has a TryParse.
        return (bool)ExpressionFactory.Parse(tag.InnerTryParse()!, TypeFactory.GetType(SpecialType.Boolean), false).Value!;
    }

    [Template]
    public static dynamic Utf8SpanParseTemplate(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        var tag = (ParsableImplementationArguments)meta.Tags.Source!;
        string wrapped    = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));
        string unreadable = meta.CompileTime($"global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.Unreadable<{ValueObjectTypes.SourceName(tag.ValueObjectType)}, {wrapped}>()");

        // TryParse and a throw of our own: the wrapped type's Parse quotes the input in its message.
        return ExpressionFactory.Parse(
            $"{tag.FromText}(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseUtf8<{wrapped}>(utf8Text, {ParsableImplementationArguments.Provider}, out var __parsed) ? __parsed : throw {unreadable})",
            tag.ValueObjectType,
            false
        ).Value!;
    }

    [Template]
    public static bool Utf8SpanTryParseTemplate(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out dynamic result)
    {
        var tag = (ParsableImplementationArguments)meta.Tags.Source!;
        string wrapped = meta.CompileTime(ValueObjectTypes.SourceName(tag.ValueType));

        result = meta.Default(tag.ValueObjectType);

        return (bool)ExpressionFactory.Parse(
            $"(global::CodoMetis.TypeKit.CompilerServices.GeneratedParsing.TryParseUtf8<{wrapped}>(utf8Text, {ParsableImplementationArguments.Provider}, out var innerValue) && {tag.TryFromText}(innerValue, out result))",
            TypeFactory.GetType(SpecialType.Boolean),
            false
        ).Value!;
    }
}
