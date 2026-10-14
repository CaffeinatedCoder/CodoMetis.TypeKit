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
    public static JsonConverter<T>? For<T>() => (JsonConverter<T>?)(object?)(
        typeof(T) == typeof(string)         ? JsonMetadataServices.StringConverter
      : typeof(T) == typeof(int)            ? JsonMetadataServices.Int32Converter
      : typeof(T) == typeof(long)           ? JsonMetadataServices.Int64Converter
      : typeof(T) == typeof(bool)           ? JsonMetadataServices.BooleanConverter
      : typeof(T) == typeof(decimal)        ? JsonMetadataServices.DecimalConverter
      : typeof(T) == typeof(double)         ? JsonMetadataServices.DoubleConverter
      : typeof(T) == typeof(float)          ? JsonMetadataServices.SingleConverter
      : typeof(T) == typeof(Guid)           ? JsonMetadataServices.GuidConverter
      : typeof(T) == typeof(DateTime)       ? JsonMetadataServices.DateTimeConverter
      : typeof(T) == typeof(DateTimeOffset) ? JsonMetadataServices.DateTimeOffsetConverter
      : typeof(T) == typeof(DateOnly)       ? JsonMetadataServices.DateOnlyConverter
      : typeof(T) == typeof(TimeOnly)       ? JsonMetadataServices.TimeOnlyConverter
      : typeof(T) == typeof(TimeSpan)       ? JsonMetadataServices.TimeSpanConverter
      : typeof(T) == typeof(short)          ? JsonMetadataServices.Int16Converter
      : typeof(T) == typeof(ushort)         ? JsonMetadataServices.UInt16Converter
      : typeof(T) == typeof(uint)           ? JsonMetadataServices.UInt32Converter
      : typeof(T) == typeof(ulong)          ? JsonMetadataServices.UInt64Converter
      : typeof(T) == typeof(byte)           ? JsonMetadataServices.ByteConverter
      : typeof(T) == typeof(sbyte)          ? JsonMetadataServices.SByteConverter
      : typeof(T) == typeof(char)           ? JsonMetadataServices.CharConverter
      : typeof(T) == typeof(Half)           ? JsonMetadataServices.HalfConverter
      : typeof(T) == typeof(Int128)         ? JsonMetadataServices.Int128Converter
      : typeof(T) == typeof(UInt128)        ? JsonMetadataServices.UInt128Converter
      : typeof(T) == typeof(Uri)            ? JsonMetadataServices.UriConverter
      : typeof(T) == typeof(Version)        ? JsonMetadataServices.VersionConverter
      : typeof(T) == typeof(byte[])         ? JsonMetadataServices.ByteArrayConverter
      : (JsonConverter?)null);
}
