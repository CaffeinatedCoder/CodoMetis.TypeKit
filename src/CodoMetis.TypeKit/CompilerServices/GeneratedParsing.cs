using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Calls the wrapped type's <c>Parse</c>/<c>TryParse</c> through a type parameter, for the parsing
/// that CodoMetis.TypeKit.Generators generates.
/// </summary>
/// <remarks>
/// <para>
/// A type may implement <see cref="IParsable{TSelf}"/> explicitly, as <see cref="bool"/> and
/// <see cref="char"/> do. Then <c>bool.Parse(s, provider)</c> does not compile, and the member can
/// only be reached through a type parameter constrained to the interface, which is what these do.
/// </para>
/// <para>
/// The generated <c>Parse</c> never calls the wrapped type's <c>Parse</c>: that one's message quotes
/// the input ("The input string 'SECRET' was not in a correct format."), and input can be a secret. It
/// calls <c>TryParse</c> and throws <see cref="Unreadable{TValueObject, T}"/>, whose message names the
/// two types and never the input. A wrapped type without <c>TryParse</c> is parsed through
/// <see cref="ConvertFromString{TValueObject, T}"/> or <see cref="Guarded{TValueObject, T}"/>, which
/// replace its exception with that one, and drop it as the inner exception, since it carries the input too.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedParsing
{
    /// <summary>
    /// The exception the generated <c>Parse</c> of <typeparamref name="TValueObject"/> throws for text
    /// that <typeparamref name="T"/> cannot read: a <see cref="FormatException"/> naming both types,
    /// never the input, with no inner exception, and ending in a link to the README's explanation.
    /// </summary>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <typeparam name="T">The wrapped type.</typeparam>
    /// <returns>The exception, to throw.</returns>
    public static FormatException Unreadable<TValueObject, T>() =>
        new($"{typeof(TValueObject).Name} could not read the input as {typeof(T).Name}.{Refusals.UnreadableInput}");

    /// <summary><see cref="IParsable{TSelf}.TryParse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="s">The text.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <param name="result">The parsed value, if parsing succeeded.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>Whether parsing succeeded.</returns>
    public static bool TryParse<T>([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
        where T : IParsable<T> =>
        T.TryParse(s, provider, out result);

    /// <summary><see cref="ISpanParsable{TSelf}.TryParse"/> of <typeparamref name="T"/>.</summary>
    /// <param name="s">The characters.</param>
    /// <param name="provider">Culture-specific formatting information.</param>
    /// <param name="result">The parsed value, if parsing succeeded.</param>
    /// <typeparam name="T">The type to parse.</typeparam>
    /// <returns>Whether parsing succeeded.</returns>
    public static bool TryParseSpan<T>(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T> =>
        T.TryParse(s, provider, out result);

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
    /// <paramref name="s"/> read as <typeparamref name="T"/> through the type converter of
    /// <typeparamref name="T"/> (<see cref="TypeConverterOf{T}"/>), for a wrapped type that parses only
    /// through its <see cref="TypeConverterAttribute"/>. Its exception is replaced by
    /// <see cref="Unreadable{TValueObject, T}"/>: NodaTime's quotes the input ("Value being parsed: '…'").
    /// </summary>
    /// <param name="s">The text.</param>
    /// <param name="provider">Culture-specific formatting information; a <see cref="CultureInfo"/> reaches the converter.</param>
    /// <typeparam name="TValueObject">The value object type, which the exception names.</typeparam>
    /// <typeparam name="T">The wrapped type.</typeparam>
    /// <returns>The parsed value.</returns>
    /// <exception cref="FormatException"><typeparamref name="T"/> could not read <paramref name="s"/>.</exception>
    public static T ConvertFromString<TValueObject, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string s, IFormatProvider? provider)
    {
        object? converted;

        try
        {
            converted = TypeConverterOf<T>().ConvertFromString(null, provider as CultureInfo ?? CultureInfo.InvariantCulture, s);
        }
        catch (Exception exception) when (IsRefusal(exception))
        {
            throw Unreadable<TValueObject, T>();
        }

        return converted is T value ? value : throw Unreadable<TValueObject, T>();
    }

    /// <summary>
    /// <paramref name="s"/> read as <typeparamref name="T"/> by <paramref name="parse"/>, a wrapped type's
    /// static <c>Parse(string)</c> or string constructor, which has no <c>TryParse</c> to call instead.
    /// Its exception is replaced by <see cref="Unreadable{TValueObject, T}"/>, since it may quote the input.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <param name="parse">The wrapped type's own parsing, as a static lambda.</param>
    /// <typeparam name="TValueObject">The value object type, which the exception names.</typeparam>
    /// <typeparam name="T">The wrapped type.</typeparam>
    /// <returns>The parsed value.</returns>
    /// <exception cref="FormatException"><typeparamref name="T"/> could not read <paramref name="s"/>.</exception>
    public static T Guarded<TValueObject, T>(string s, Func<string, T> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);

        try
        {
            return parse(s);
        }
        catch (Exception exception) when (IsRefusal(exception))
        {
            throw Unreadable<TValueObject, T>();
        }
    }

    /// <summary>
    /// What a parser throws for text it cannot read, as opposed to a failure of the process, which is
    /// left alone.
    /// </summary>
    private static bool IsRefusal(Exception exception) =>
        exception is FormatException or ArgumentException or OverflowException or InvalidOperationException or NotSupportedException;

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
