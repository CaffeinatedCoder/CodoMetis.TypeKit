using System.Collections.Concurrent;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Which CLR types are value objects, and what they wrap: read from the
/// <see cref="GeneratedValueObjectAttribute"/> every value object carries, whose type arguments are
/// constrained to <see cref="ValueObjects.IValueObject{TSelf,T}"/>. Never by name or assembly.
/// </summary>
/// <remarks>
/// Not by <see cref="Type.GetInterfaces"/>, which trimming breaks: under Native AOT the interface is
/// removed from a value object when nothing uses it (measured 2026-09-27). Custom attributes are kept.
/// </remarks>
internal static class ValueObjectTypes
{
    private static readonly ConcurrentDictionary<Type, GeneratedValueObjectAttribute?> Descriptions = new();

    /// <summary>The value object's attribute, or <see langword="null"/> for any other type.</summary>
    public static GeneratedValueObjectAttribute? Describe(Type type) =>
        Descriptions.GetOrAdd(type, static candidate =>
            candidate.GetCustomAttribute<GeneratedValueObjectAttribute>(inherit: false) is { } valueObject && valueObject.ValueObjectType == candidate
                ? valueObject
                : null);

    public static bool IsValueObject(Type type) => Describe(type) is not null;
}
