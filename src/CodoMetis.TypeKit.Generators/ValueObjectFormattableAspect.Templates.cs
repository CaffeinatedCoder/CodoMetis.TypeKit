using System.Globalization;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Code.SyntaxBuilders;

namespace CodoMetis.TypeKit.Generators;

// A null IFormatProvider means the invariant culture in every generated format, as it does in the
// generated parsing (ParsableImplementationArguments.Provider). Interpolation, string.Format and
// Convert.ToString pass null, and with the current culture $"{amount}" was "1,5" in de-DE while
// amount.ToString() was "1.5", and Parse($"{amount}", null) read it back as 15. A provider that is
// given is used as given.
internal sealed partial class ValueObjectFormattableAspect
{
    /// <summary>
    /// C# text: in a template, <c>CultureInfo.InvariantCulture</c> beside a run-time operand of
    /// <c>??</c> counts as a compile-time value (LAMA0200).
    /// </summary>
    private const string InvariantIfNull = "?? global::System.Globalization.CultureInfo.InvariantCulture";

    private static IExpression Formatted(string call, SpecialType returnType) =>
        ExpressionFactory.Parse(call, TypeFactory.GetType(returnType), false);

    [Template]
    public string FormattableToStringTemplate(string? format, IFormatProvider? formatProvider)
    {
        var tag = (FormattableImplementationArguments)meta.Tags.Source!;

        if (meta.CompileTime(tag.Strategy == ValueFormatStrategy.String))
            return meta.This.Value ?? string.Empty;

        if (meta.CompileTime(tag.Strategy is ValueFormatStrategy.Formattable or ValueFormatStrategy.SpanFormattable))
            return (string)Formatted($"((global::System.IFormattable)this.Value).ToString(format, formatProvider {InvariantIfNull})", SpecialType.String).Value!;

        // FallbackToString: T has no formatting knowledge, so ignore args gracefully.
        return meta.This.Value.ToString() ?? string.Empty;
    }

    // Overrides the record-generated ToString() with the invariant culture. The BCL's own ToString()
    // uses the current culture; this one does not, because a value object's text is an identifier
    // or a wire value more often than a display string, and the generated Parse treats a null
    // provider as invariant too, so Parse(x.ToString(), null) round-trips. Callers who need a
    // culture pass it: x.ToString(format, culture), or string.Create(culture, $"{x}").
    [Template]
    public string ToStringOverrideTemplate() => meta.This.ToString(null, CultureInfo.InvariantCulture);

    // Satisfies ISpanFormattable.TryFormat — only emitted when T : ISpanFormattable.
    [Template]
    public bool TryFormatTemplate(
        Span<char>         destination,
        out int            charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider?   provider
    )
    {
        charsWritten = 0;

        return (bool)Formatted($"((global::System.ISpanFormattable)this.Value).TryFormat(destination, out charsWritten, format, provider {InvariantIfNull})", SpecialType.Boolean).Value!;
    }

    // Satisfies IUtf8SpanFormattable.TryFormat — only emitted when T : IUtf8SpanFormattable.
    // Delegates directly, preserving the allocation-free UTF-8 path end-to-end.
    [Template]
    public bool TryFormatUtf8Template(
        Span<byte>         utf8Destination,
        out int            bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider?   provider
    )
    {
        bytesWritten = 0;

        return (bool)Formatted($"((global::System.IUtf8SpanFormattable)this.Value).TryFormat(utf8Destination, out bytesWritten, format, provider {InvariantIfNull})", SpecialType.Boolean).Value!;
    }
}
