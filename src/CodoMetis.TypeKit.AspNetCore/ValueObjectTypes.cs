using System.Collections.Concurrent;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Which CLR types are value objects, and what they wrap: read from the
/// <see cref="GeneratedValueObjectAttribute"/> every value object carries, whose type arguments are
/// constrained to <see cref="ValueObjects.IValueObject{TSelf,T}"/>. Never by name or assembly.
/// </summary>
/// <remarks>
/// Not by <see cref="Type.GetInterfaces"/>, which trimming breaks: under Native AOT the interface is
/// removed from a value object when nothing uses it, and every value object was described as <c>{}</c>
/// (measured 2026-09-27). Custom attributes are kept.
/// </remarks>
internal static class ValueObjectTypes
{
    private static readonly ConcurrentDictionary<Type, Type?> WrappedTypes = new();

    /// <summary>
    /// The wrapped type <c>T</c> of a value object, or of a nullable one, or <see langword="null"/>
    /// for any other type.
    /// </summary>
    public static Type? WrappedType(Type? type) =>
        type is null
            ? null
            : WrappedTypes.GetOrAdd(Nullable.GetUnderlyingType(type) ?? type, static candidate =>
                candidate.GetCustomAttribute<GeneratedValueObjectAttribute>(inherit: false) is { } valueObject && valueObject.ValueObjectType == candidate
                    ? valueObject.WrappedType
                    : null);

    public static bool IsValueObject(Type type) => WrappedType(type) is not null;
}
