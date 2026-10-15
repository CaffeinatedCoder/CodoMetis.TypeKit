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

        if (_direct is null && options.ReferenceHandler is { } handler && handler != ReferenceHandler.IgnoreCycles && MayHoldReferences(options, typeInfo, []))
            throw new NotSupportedException(
                $"A value object over {typeof(T)} cannot be written or read under {nameof(JsonSerializerOptions.ReferenceHandler)}.{nameof(ReferenceHandler.Preserve)}: "
              + $"the serializer numbers references per call, and the value object's converter reaches the {typeof(T)} through a call of its own, so its \"$id\" and \"$ref\" "
              + $"would not be the document's. Register a JsonConverter<{typeof(T)}> on the options, or serialize without preserving references.");
    }

    /// <summary>
    /// Whether the serializer may write <c>$id</c> and <c>$ref</c> inside a <typeparamref name="T"/> under
    /// a preserving reference handler: an object or a collection of a reference type, an <c>object</c> the
    /// serializer's own converter writes, which holds anything, or a struct with one among the properties it
    /// writes or reads, through the converter in charge of each, or among the elements it holds as a
    /// collection, a nullable struct as the struct it holds. A struct has no <c>$id</c> of its own, and a
    /// value converter writes none, nor does a converter of one's own.
    /// </summary>
    /// <remarks>
    /// The serializer keeps the references of one call, and a nested call starts again: under
    /// <see cref="ReferenceHandler.Preserve"/> a value object over a class wrote <c>"$id":"1"</c> inside a
    /// document that already had one, and two of them around one instance wrote it twice where the
    /// wrapped type writes a <c>$ref</c>, which the value object's converter could not read back
    /// (measured 2026-10-14). There is no way to reach the caller's references from a converter. A
    /// cycle under <see cref="ReferenceHandler.IgnoreCycles"/> fails at the depth limit instead, which is
    /// loud, so that handler is left alone.
    /// <para>
    /// A nullable struct's contract lists none of the properties of the struct it holds, which the
    /// serializer's converter for it writes, so a class there wrote its <c>$id</c> twice unseen; a converter
    /// of its own, which the contract has from a resolver or the options, writes none. Which one writes it is
    /// the contract's word, not the options': a resolver's own was refused, and the serializer's put in
    /// charge over one in the options let an <c>$id</c> through twice (both reported 2026-10-14). An ignored
    /// property, which has neither getter nor setter
    /// in the contract, is neither written nor read, so a class there refused a struct the serializer
    /// writes without one (both reported 2026-10-14). The serializer's converter of a nullable struct is
    /// judged by the struct's contract, since no public API names the converter it was built over: a resolver
    /// that builds the nullable contract by hand over a converter that writes no <c>$id</c> is refused, a
    /// known limit that is loud, and a converter of the nullable type itself lifts it.
    /// </para>
    /// </remarks>
    private static bool MayHoldReferences(JsonSerializerOptions options, JsonTypeInfo typeInfo, HashSet<Type> visited)
    {
        if (Nullable.GetUnderlyingType(typeInfo.Type) is { } held && GeneratedJson.IsNullableAdapter(typeInfo.Converter, typeInfo.Type))
            return MayHoldReferences(options, options.GetTypeInfo(held), visited);

        if (typeInfo.Type == typeof(object)) return GeneratedJson.IsTheSerializers(typeInfo.Converter);
        if (typeInfo.Kind == JsonTypeInfoKind.None) return false;
        if (!typeInfo.Type.IsValueType) return true;
        if (!visited.Add(typeInfo.Type)) return false;

        // A struct collection has no $id of its own either; its elements may (dictionary keys are names).
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return typeInfo.ElementType is not { } element || MayHoldReferences(options, options.GetTypeInfo(element), visited);

        foreach (var property in typeInfo.Properties)
        {
            if (property is { Get: not null } or { Set: not null } && MayHoldReferences(options, property, visited)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the converter in charge of <paramref name="property"/> may write <c>$id</c> and <c>$ref</c>: the
    /// contract's, or a converter of the property's own. One of one's own writes none. One of the serializer's,
    /// a factory among them, writes as the serializer's contract of the type it converts, so that contract
    /// judges it; where that contract has a converter of one's own instead, which says nothing of how the
    /// serializer's would write, it is refused. A converter named on a nullable property is the serializer's
    /// adapter of the one named, so the named one is judged, as a schema description reads it.
    /// </summary>
    private static bool MayHoldReferences(JsonSerializerOptions options, JsonPropertyInfo property, HashSet<Type> visited)
    {
        var contract = options.GetTypeInfo(property.PropertyType);

        if (property.CustomConverter is not { } own || own == contract.Converter) return MayHoldReferences(options, contract, visited);

        own = JsonSchemaDescription.PropertyConverter(own, property, property.PropertyType, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, options) ?? own;
        if (!GeneratedJson.IsTheSerializers(own)) return false;

        var converted = own.Type is { } type ? options.GetTypeInfo(type) : contract;

        return !GeneratedJson.IsTheSerializers(converted.Converter) || MayHoldReferences(options, converted, visited);
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
    /// <exception cref="NotSupportedException">The options preserve references, and <typeparamref name="T"/> may hold some.</exception>
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
