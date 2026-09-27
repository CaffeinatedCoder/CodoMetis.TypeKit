using System.Collections.Concurrent;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Which CLR types are value objects, and what they wrap. Recognised by <see cref="IValueObject{TValueObject,T}"/>,
/// never by name or assembly.
/// </summary>
internal static class ValueObjectTypes
{
    private static readonly ConcurrentDictionary<Type, Type?> UnderlyingTypes = new();

    /// <summary>The wrapped type <c>T</c> of an <c>IValueObject&lt;TSelf, T&gt;</c>, or <see langword="null"/> for any other type.</summary>
    public static Type? UnderlyingType(Type type) =>
        UnderlyingTypes.GetOrAdd(type, static candidate =>
            candidate.GetInterfaces()
                     .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>) && i.GetGenericArguments()[0] == candidate)
                    ?.GetGenericArguments()[1]);

    public static bool IsValueObject(Type type) => UnderlyingType(type) is not null;
}
