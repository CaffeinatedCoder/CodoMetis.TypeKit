using System.ComponentModel;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Calls the wrapped type's formatting through a type parameter, for the formatting that
/// CodoMetis.TypeKit.Generators generates.
/// </summary>
/// <remarks>
/// <para>
/// Casting a struct to <see cref="ISpanFormattable"/> boxes it. Measured on the generated
/// <c>TryFormat</c> of a Guid value object: 32 bytes and three and a half times the time of
/// <see cref="Guid.TryFormat(Span{char}, out int, ReadOnlySpan{char})"/>, on every call. A call through
/// a type parameter constrained to the interface is a constrained call, which never boxes.
/// </para>
/// <para>
/// It also reaches an explicit implementation, as <see cref="GeneratedParsing"/> does for parsing.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedFormatting
{
    /// <summary><see cref="IFormattable.ToString(string?, IFormatProvider?)"/> of <typeparamref name="T"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The format.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <typeparam name="T">The type to format.</typeparam>
    /// <returns>The formatted text.</returns>
    public static string ToString<T>(T value, string? format, IFormatProvider? provider) where T : IFormattable =>
        value.ToString(format, provider);

    /// <summary><see cref="ISpanFormattable.TryFormat"/> of <typeparamref name="T"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="destination">Where the characters go.</param>
    /// <param name="charsWritten">How many characters were written.</param>
    /// <param name="format">The format.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <typeparam name="T">The type to format.</typeparam>
    /// <returns>Whether the value fitted into <paramref name="destination"/>.</returns>
    public static bool TryFormat<T>(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        where T : ISpanFormattable =>
        value.TryFormat(destination, out charsWritten, format, provider);

    /// <summary><see cref="IUtf8SpanFormattable.TryFormat"/> of <typeparamref name="T"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="utf8Destination">Where the UTF-8 bytes go.</param>
    /// <param name="bytesWritten">How many bytes were written.</param>
    /// <param name="format">The format.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <typeparam name="T">The type to format.</typeparam>
    /// <returns>Whether the value fitted into <paramref name="utf8Destination"/>.</returns>
    public static bool TryFormatUtf8<T>(T value, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        where T : IUtf8SpanFormattable =>
        value.TryFormat(utf8Destination, out bytesWritten, format, provider);
}
