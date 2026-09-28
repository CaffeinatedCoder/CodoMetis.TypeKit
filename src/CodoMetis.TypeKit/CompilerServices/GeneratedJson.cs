using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// What the JSON converter that CodoMetis.TypeKit.Generators generates calls, kept in ordinary C# so
/// it is written once and testable without a generator. The wrapped value itself is read and written
/// through <see cref="GeneratedJsonPlan{T}"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedJson
{
    /// <summary>
    /// The UTC <see cref="DateTime"/> a value object writes and reads, without ever consulting the
    /// server's time zone for a value that does not name one.
    /// </summary>
    /// <remarks>
    /// A <see cref="DateTimeKind.Local"/> value is an instant in this machine's zone and converts
    /// exactly. An <see cref="DateTimeKind.Unspecified"/> one (an offset-less JSON string, a column
    /// without a time zone) names no zone, and <see cref="DateTime.ToUniversalTime"/> would read it as
    /// server-local, so 12:00 became 10:00Z on a server at +02:00 and stayed 12:00Z in UTC. It is taken
    /// as UTC instead, keeping its digits.
    /// </remarks>
    /// <param name="value">The value to normalise.</param>
    /// <returns><paramref name="value"/> as a <see cref="DateTimeKind.Utc"/> value.</returns>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    /// <summary>
    /// What a value object's converter throws for JSON its wrapped type cannot read: a
    /// <see cref="JsonException"/> naming both types, never the input, with no inner exception, and
    /// ending in a link to the README's explanation.
    /// </summary>
    /// <remarks>
    /// The converters' own exceptions may quote the input: NodaTime's ("Value being parsed: '…'"), and
    /// for a dictionary key the serializer's own message, whose path is the key. The serializer still
    /// sets <see cref="JsonException.Path"/> on this one, and for a key the path contains it.
    /// </remarks>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <typeparam name="T">The wrapped type.</typeparam>
    /// <returns>The exception, to throw.</returns>
    public static JsonException Unreadable<TValueObject, T>() =>
        new($"{typeof(TValueObject).Name} could not read the JSON value as {typeof(T).Name}.{Refusals.UnreadableInput}");

    /// <summary>
    /// The refusal of a JSON null where a value object is declared: a null is not a value object, and a
    /// string-backed one would otherwise wrap it.
    /// </summary>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <returns>The exception, to throw.</returns>
    public static JsonException NullToken<TValueObject>() =>
        new($"{typeof(TValueObject).Name} cannot be read from a JSON null. Where the value can be absent, declare the property as {typeof(TValueObject).Name}?.{Refusals.NullToValueObject}");

    /// <summary>
    /// What a converter throws for input it cannot read, as opposed to a configuration the serializer
    /// refuses (<see cref="NotSupportedException"/>), which is left alone.
    /// </summary>
    internal static bool IsRefusal(Exception exception) =>
        exception is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException;

    /// <summary>
    /// The JSON contract of the wrapped type <typeparamref name="T"/> under <paramref name="options"/>,
    /// through which the generated converter reads and writes the wrapped value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options' own contract when their resolver has one, so the value is read and written exactly
    /// as the serializer does it for this host (number handling, a registered converter).
    /// </para>
    /// <para>
    /// A source-generated <see cref="JsonSerializerContext"/>, the only resolver under Native AOT, has
    /// no contract for a type it never saw, and it never sees what a value object wraps: the value
    /// object's <c>[JsonConverter]</c> hides it. The contract is then made the way the serializer
    /// would make it, in its order: a converter registered on the options, the type's own
    /// <see cref="JsonConverterAttribute"/>, the serializer's built-in converter
    /// (<paramref name="builtIn"/>). Only a type with none of these is refused, with the serializer's
    /// own message naming the context.
    /// </para>
    /// <para>
    /// Earlier, the generated code serialized the wrapped value through the options alone, which
    /// requires reflection (so trimming and AOT warned), and a context without the wrapped type
    /// refused a <see cref="decimal"/> value object outright.
    /// </para>
    /// </remarks>
    /// <param name="options">The options the value object is being read or written with.</param>
    /// <param name="builtIn">The serializer's converter for <typeparamref name="T"/>, or <see langword="null"/> where it has none.</param>
    /// <typeparam name="T">The wrapped type.</typeparam>
    /// <returns>The contract.</returns>
    /// <exception cref="NotSupportedException">Nothing can read or write <typeparamref name="T"/> under <paramref name="options"/>.</exception>
    public static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions options, JsonConverter<T>? builtIn)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Not an object or collection contract where a built-in converter is at hand: that is the
        // reflection resolver walking a type it has no converter for, as it does for a NodaTime type
        // when the options were not configured for NodaTime, and the value object writes NodaTime's
        // own format there, not {}.
        if (options.TryGetTypeInfo(typeof(T), out var resolved) && (resolved.Kind == JsonTypeInfoKind.None || builtIn is null))
            return (JsonTypeInfo<T>)resolved;

        return Contracts<T>.Made.GetValue(options, created => CreateContract(created, builtIn)
                                                           ?? (JsonTypeInfo<T>)created.GetTypeInfo(typeof(T)));
    }

    private static JsonTypeInfo<T>? CreateContract<T>(JsonSerializerOptions options, JsonConverter<T>? builtIn)
    {
        var converter = options.Converters.FirstOrDefault(candidate => candidate.CanConvert(typeof(T)))
                     ?? AttributeConverter(typeof(T))
                     ?? builtIn;

        if (converter is JsonConverterFactory factory) converter = factory.CreateConverter(typeof(T), options);

        return converter is null ? null : JsonMetadataServices.CreateValueInfo<T>(options, converter);
    }

    /// <summary>
    /// The converter a type's own <see cref="JsonConverterAttribute"/> names, created through the
    /// public parameterless constructor the attribute keeps under trimming.
    /// </summary>
    private static JsonConverter? AttributeConverter(Type type) =>
        type.GetCustomAttribute<JsonConverterAttribute>(inherit: false) is { } attribute
            ? attribute.ConverterType is { } converterType
                  ? (JsonConverter?)Activator.CreateInstance(converterType)
                  : attribute.CreateConverter(type)
            : null;

    /// <summary>Contracts made here, one per options instance, which outlive neither.</summary>
    private static class Contracts<T>
    {
        public static readonly ConditionalWeakTable<JsonSerializerOptions, JsonTypeInfo<T>> Made = new();
    }
}
