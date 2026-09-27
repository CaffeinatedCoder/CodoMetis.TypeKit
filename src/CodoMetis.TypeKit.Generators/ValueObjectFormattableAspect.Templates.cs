using System.Globalization;
using Metalama.Framework.Aspects;

namespace CodoMetis.TypeKit.Generators;

internal sealed partial class ValueObjectFormattableAspect
{
    [Template]
    public string FormattableToStringTemplate(string? format, IFormatProvider? formatProvider)
    {
        var tag = (FormattableImplementationArguments)meta.Tags.Source!;

        if (meta.CompileTime(tag.Strategy == ValueFormatStrategy.String))
            return meta.This.Value ?? string.Empty;

        if (meta.CompileTime(tag.Strategy is ValueFormatStrategy.Formattable or ValueFormatStrategy.SpanFormattable))
            return ((IFormattable)meta.This.Value).ToString(format, formatProvider);

        // FallbackToString: T has no formatting knowledge, so ignore args gracefully.
        return meta.This.Value.ToString() ?? string.Empty;
    }

    // Overrides the record-generated ToString() to stay consistent.
    // Uses InvariantCulture to match standard library conventions for
    // ToString() — callers who need locale control use the IFormattable overload.
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
        return ((ISpanFormattable)meta.This.Value).TryFormat(destination, out charsWritten, format, provider);
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
        return ((IUtf8SpanFormattable)meta.This.Value).TryFormat(utf8Destination, out bytesWritten, format, provider);
    }
}
