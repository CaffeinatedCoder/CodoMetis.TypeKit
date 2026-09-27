using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Calls the wrapped type's <c>Parse</c>/<c>TryParse</c> through a type parameter, for the parsing
/// that CodoMetis.TypeKit.Generators generates.
/// </summary>
/// <remarks>
/// A type may implement <see cref="IParsable{TSelf}"/> explicitly, as <see cref="bool"/> and
/// <see cref="char"/> do. Then <c>bool.Parse(s, provider)</c> does not compile, and the member can
/// only be reached through a type parameter constrained to the interface, which is what these do.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedParsing
{
    /// <summary><see cref="IParsable{TSelf}.Parse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="s">The text.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>The parsed value.</returns>
    public static T Parse<T>(string s, IFormatProvider? provider) where T : IParsable<T> => T.Parse(s, provider);

    /// <summary><see cref="IParsable{TSelf}.TryParse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="s">The text.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <param name="result">The parsed value, if parsing succeeded.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>Whether parsing succeeded.</returns>
    public static bool TryParse<T>([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
        where T : IParsable<T> =>
        T.TryParse(s, provider, out result);

    /// <summary><see cref="ISpanParsable{TSelf}.Parse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="s">The characters.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>The parsed value.</returns>
    public static T ParseSpan<T>(ReadOnlySpan<char> s, IFormatProvider? provider) where T : ISpanParsable<T> => T.Parse(s, provider);

    /// <summary><see cref="ISpanParsable{TSelf}.TryParse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="s">The characters.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <param name="result">The parsed value, if parsing succeeded.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>Whether parsing succeeded.</returns>
    public static bool TryParseSpan<T>(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T> =>
        T.TryParse(s, provider, out result);

    /// <summary><see cref="IUtf8SpanParsable{TSelf}.Parse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="utf8Text">The UTF-8 text.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>The parsed value.</returns>
    public static T ParseUtf8<T>(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) where T : IUtf8SpanParsable<T> =>
        T.Parse(utf8Text, provider);

    /// <summary><see cref="IUtf8SpanParsable{TSelf}.TryParse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="utf8Text">The UTF-8 text.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <param name="result">The parsed value, if parsing succeeded.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>Whether parsing succeeded.</returns>
    public static bool TryParseUtf8<T>(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
        where T : IUtf8SpanParsable<T> =>
        T.TryParse(utf8Text, provider, out result);

    /// <summary>
    /// The <see cref="TypeConverter"/> of <typeparamref name="T"/>, for a wrapped type that parses only
    /// through its <see cref="TypeConverterAttribute"/> (NodaTime's types do).
    /// </summary>
    /// <remarks>
    /// Resolved through <see cref="TypeDescriptor.RegisterType{T}"/> and
    /// <see cref="TypeDescriptor.GetConverterFromRegisteredType(Type)"/>, the trim-safe path, once per
    /// type. <see cref="TypeDescriptor.GetConverter(Type)"/> requires unreferenced code, so trimming
    /// and Native AOT warned about the generated parsing that called it.
    /// </remarks>
    /// <typeparam name="T">The wrapped type.</typeparam>
    /// <returns>The converter.</returns>
    public static TypeConverter TypeConverterOf<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() => TypeConverters<T>.Converter;

    private static class TypeConverters<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    {
        public static readonly TypeConverter Converter = Register();

        private static TypeConverter Register()
        {
            TypeDescriptor.RegisterType<T>();
            return TypeDescriptor.GetConverterFromRegisteredType(typeof(T));
        }
    }
}
