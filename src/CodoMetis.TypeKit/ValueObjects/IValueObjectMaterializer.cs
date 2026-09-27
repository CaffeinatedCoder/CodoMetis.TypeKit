namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// Rebuilds a value object from a value the application wrote itself, <b>without validation</b>.
/// </summary>
/// <remarks>
/// <para>
/// This exists for persistence: CodoMetis.TypeKit.EntityFrameworkCore reads a column back into the
/// value object it was written from. A rule added to <c>Create</c> later must not make existing rows
/// unreadable, so this path does not apply the rules.
/// </para>
/// <para>
/// <b>Never call it on input.</b> Anything a user, a request or another system supplied goes through
/// <c>Create</c>, <c>TryFrom</c> or <c>From</c>. Calling <see cref="Materialize"/> anywhere but the
/// EF Core satellite is a bug.
/// </para>
/// <para>
/// CodoMetis.TypeKit.Generators implements it explicitly, so it is not part of the value object's
/// public surface and can only be reached through a type parameter constrained to this interface.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The value object type itself.</typeparam>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public interface IValueObjectMaterializer<TSelf, T>
    where TSelf : IValueObjectMaterializer<TSelf, T>
    where T : notnull
{
    /// <summary>Wraps <paramref name="value"/> without applying the value object's rules.</summary>
    /// <param name="value">A value the application itself wrote, for instance to a database column.</param>
    /// <returns>A value object wrapping <paramref name="value"/>.</returns>
    abstract static TSelf Materialize(T value);
}
