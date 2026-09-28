using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// How a generated JSON converter reads and writes the wrapped type <typeparamref name="T"/> under one
/// options instance: exactly as the serializer reads and writes a <typeparamref name="T"/> there, byte
/// for byte, as a value and as a dictionary key.
/// </summary>
/// <remarks>
/// <para>
/// The converter keeps the plan for the options it last saw in a field of its own, which is every call
/// in an application with one set of options, and makes a new one (<see cref="For"/>) when the options
/// change. Kept in a static field per wrapped type, a string's plan cost a lookup through the shared
/// generic code on every call, and a value object's JSON took 8% longer to write than its wrapped
/// type's (measured 2026-09-28).
/// </para>
/// <para>
/// The wrapped value goes through the contract's converter directly where that writes exactly what the
/// serializer would, and through the serializer where the options' number handling changes the output
/// of a number (<see cref="JsonNumberHandling.WriteAsString"/>, named floating-point literals), which
/// only the serializer applies. A nested <c>JsonSerializer.Serialize</c> per value made a value
/// object's JSON 40% slower than its wrapped type's (measured 2026-09-28).
/// </para>
/// </remarks>
/// <typeparam name="T">The wrapped type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedJsonPlan<T>
{
    /// <summary>
    /// The types the serializer applies number handling to, and only through its own converters for
    /// them. For any other type the converter is called directly whatever the options say, which is
    /// what the serializer does: a Guid under <see cref="JsonSerializerDefaults.Web"/> went through a
    /// nested deserialization for nothing.
    /// </summary>
    private static readonly bool IsNumber =
        typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort)
     || typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong)
     || typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(decimal) || typeof(T) == typeof(Half)
     || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128);

    private readonly JsonTypeInfo<T>   _typeInfo;
    private readonly JsonConverter<T>? _direct;
    private readonly JsonConverter<T>? _keyConverter;
    private readonly bool              _writesAsIs;
    private readonly bool              _readsAsIs;

    private GeneratedJsonPlan(JsonSerializerOptions options, JsonTypeInfo<T> typeInfo)
    {
        Options   = options;
        _typeInfo = typeInfo;

        var handling = typeInfo.NumberHandling ?? options.NumberHandling;

        // Only a value converter is called directly: an object or collection contract is the
        // serializer's to walk. Number handling is applied by the serializer alone, and only these
        // flags change what is written; reading a number from a string is decided per token.
        _direct       = typeInfo.Kind == JsonTypeInfoKind.None ? typeInfo.Converter as JsonConverter<T> : null;
        _keyConverter = typeInfo.Converter as JsonConverter<T>;
        _writesAsIs   = !IsNumber || (handling & (JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowNamedFloatingPointLiterals)) == 0;
        _readsAsIs    = !IsNumber || handling == JsonNumberHandling.Strict;
    }

    /// <summary>The options this plan was made for.</summary>
    public JsonSerializerOptions Options { get; }

    /// <summary>
    /// The plan for <paramref name="options"/>, through the wrapped type's contract under them
    /// (<see cref="GeneratedJson.TypeInfo{T}"/>).
    /// </summary>
    /// <param name="options">The caller's options.</param>
    /// <param name="builtIn">The converter for <typeparamref name="T"/> where the options have none: the serializer's built-in one, or NodaTime's.</param>
    /// <returns>The plan.</returns>
    public static GeneratedJsonPlan<T> For(JsonSerializerOptions options, JsonConverter<T>? builtIn)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new GeneratedJsonPlan<T>(options, GeneratedJson.TypeInfo(options, builtIn));
    }

    /// <summary>Writes the wrapped value <paramref name="value"/> as the serializer writes a <typeparamref name="T"/>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The wrapped value.</param>
    /// <param name="options">The caller's options, which this plan was made for.</param>
    public void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (_direct is { } converter && _writesAsIs) converter.Write(writer, value, options);
        else JsonSerializer.Serialize(writer, value, _typeInfo);
    }

    /// <summary>
    /// Reads a wrapped value as the serializer reads a <typeparamref name="T"/>, and refuses what it
    /// cannot read without quoting it.
    /// </summary>
    /// <remarks>
    /// Through the contract's converter directly unless the token is a string, the wrapped type a
    /// number, and the options' number handling may read a number from a string, which only the
    /// serializer applies.
    /// </remarks>
    /// <param name="reader">The reader, on the value.</param>
    /// <param name="options">The caller's options, which this plan was made for.</param>
    /// <typeparam name="TValueObject">The value object type, which a refusal names.</typeparam>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="JsonException">The value is not a <typeparamref name="T"/> (<see cref="GeneratedJson.Unreadable{TValueObject, T}"/>).</exception>
    public T Read<TValueObject>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        T? value;

        try
        {
            value = _direct is { } converter && (reader.TokenType != JsonTokenType.String || _readsAsIs)
                        ? converter.Read(ref reader, typeof(T), options)
                        : JsonSerializer.Deserialize(ref reader, _typeInfo);
        }
        catch (Exception exception) when (GeneratedJson.IsRefusal(exception))
        {
            throw GeneratedJson.Unreadable<TValueObject, T>();
        }

        return value is null ? throw GeneratedJson.Unreadable<TValueObject, T>() : value;
    }

    /// <summary>
    /// Writes the wrapped value <paramref name="value"/> as a property name, as the serializer writes a
    /// dictionary key of type <typeparamref name="T"/>: through the converter it uses for the type, so a
    /// converter registered on the options, the <see cref="JsonSerializerOptions.DictionaryKeyPolicy"/>
    /// (which the string converter applies) and the built-in formats (<c>03:04:05</c>, not
    /// <c>03:04:05.0000000</c>) all apply.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The wrapped value.</param>
    /// <param name="options">The caller's options, which this plan was made for.</param>
    /// <exception cref="NotSupportedException">The converter for <typeparamref name="T"/> cannot write it as a property name.</exception>
    public void WriteKey(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        // No null check of the value: it would box a struct, and a key is never null.
        KeyConverter.WriteAsPropertyName(writer, value!, options);

    /// <summary>
    /// Reads a property name as the serializer reads a dictionary key of type <typeparamref name="T"/>,
    /// and refuses what it cannot read without quoting it.
    /// </summary>
    /// <param name="reader">The reader, on the property name.</param>
    /// <param name="options">The caller's options, which this plan was made for.</param>
    /// <typeparam name="TValueObject">The value object type, which a refusal names.</typeparam>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="JsonException">The name is not a <typeparamref name="T"/> (<see cref="GeneratedJson.Unreadable{TValueObject, T}"/>).</exception>
    /// <exception cref="NotSupportedException">The converter for <typeparamref name="T"/> cannot read it as a property name.</exception>
    public T ReadKey<TValueObject>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var converter = KeyConverter;
        T value;

        try
        {
            value = converter.ReadAsPropertyName(ref reader, typeof(T), options);
        }
        catch (Exception exception) when (GeneratedJson.IsRefusal(exception))
        {
            throw GeneratedJson.Unreadable<TValueObject, T>();
        }

        return value is null ? throw GeneratedJson.Unreadable<TValueObject, T>() : value;
    }

    /// <summary>The converter the serializer uses for a dictionary key of type <typeparamref name="T"/>: the contract's own, as for the value.</summary>
    private JsonConverter<T> KeyConverter =>
        _keyConverter ?? throw new NotSupportedException($"The converter for {typeof(T)} under these options cannot read or write it as a dictionary key.");
}
