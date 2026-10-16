using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// The serializer's own converter for a primitive, a string, a <see cref="Guid"/>, a date, a
/// <see cref="Uri"/> or a <see cref="Version"/>, for a type a source-generated context never saw.
/// The generated value-object converters pass the one for their wrapped type at compile time; a
/// generic type over any argument picks it here.
/// </summary>
internal static class BuiltInConverters
{
    /// <summary>The built-in converter for <typeparamref name="T"/>, or null where the serializer has none.</summary>
    /// <typeparam name="T">The type.</typeparam>
    public static JsonConverter<T>? For<T>() => (JsonConverter<T>?)Untyped<T>();

    // A chain of returns, not one conditional expression: Rider's type inference walks a nested
    // conditional again at every level, and 26 levels froze its backend until it crashed. Each
    // typeof(T) test stays in a generic method, so the JIT and Native AOT still drop every arm but one.
    private static JsonConverter? Untyped<T>()
    {
        if (typeof(T) == typeof(string))         return JsonMetadataServices.StringConverter;
        if (typeof(T) == typeof(int))            return JsonMetadataServices.Int32Converter;
        if (typeof(T) == typeof(long))           return JsonMetadataServices.Int64Converter;
        if (typeof(T) == typeof(bool))           return JsonMetadataServices.BooleanConverter;
        if (typeof(T) == typeof(decimal))        return JsonMetadataServices.DecimalConverter;
        if (typeof(T) == typeof(double))         return JsonMetadataServices.DoubleConverter;
        if (typeof(T) == typeof(float))          return JsonMetadataServices.SingleConverter;
        if (typeof(T) == typeof(Guid))           return JsonMetadataServices.GuidConverter;
        if (typeof(T) == typeof(DateTime))       return JsonMetadataServices.DateTimeConverter;
        if (typeof(T) == typeof(DateTimeOffset)) return JsonMetadataServices.DateTimeOffsetConverter;
        if (typeof(T) == typeof(DateOnly))       return JsonMetadataServices.DateOnlyConverter;
        if (typeof(T) == typeof(TimeOnly))       return JsonMetadataServices.TimeOnlyConverter;
        if (typeof(T) == typeof(TimeSpan))       return JsonMetadataServices.TimeSpanConverter;
        if (typeof(T) == typeof(short))          return JsonMetadataServices.Int16Converter;
        if (typeof(T) == typeof(ushort))         return JsonMetadataServices.UInt16Converter;
        if (typeof(T) == typeof(uint))           return JsonMetadataServices.UInt32Converter;
        if (typeof(T) == typeof(ulong))          return JsonMetadataServices.UInt64Converter;
        if (typeof(T) == typeof(byte))           return JsonMetadataServices.ByteConverter;
        if (typeof(T) == typeof(sbyte))          return JsonMetadataServices.SByteConverter;
        if (typeof(T) == typeof(char))           return JsonMetadataServices.CharConverter;
        if (typeof(T) == typeof(Half))           return JsonMetadataServices.HalfConverter;
        if (typeof(T) == typeof(Int128))         return JsonMetadataServices.Int128Converter;
        if (typeof(T) == typeof(UInt128))        return JsonMetadataServices.UInt128Converter;
        if (typeof(T) == typeof(Uri))            return JsonMetadataServices.UriConverter;
        if (typeof(T) == typeof(Version))        return JsonMetadataServices.VersionConverter;
        if (typeof(T) == typeof(byte[]))         return JsonMetadataServices.ByteArrayConverter;

        return null;
    }
}
