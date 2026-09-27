using System.ComponentModel;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Put on every value object by CodoMetis.TypeKit.Generators: what the value object wraps, readable
/// from its <see cref="Type"/> alone, also in a trimmed or Native AOT application.
/// </summary>
/// <remarks>
/// <para>
/// Run-time code that is handed a <see cref="Type"/> (the EF Core satellite mapping a property, the
/// OpenAPI satellite describing a schema) recognises a value object by this attribute, whose type
/// arguments are constrained to <see cref="IValueObject{TSelf,T}"/>: the interface decides
/// what a value object is, the attribute is how a type says so at run time. Never by a name.
/// </para>
/// <para>
/// Not by <see cref="Type.GetInterfaces"/>: trimming removes an interface nothing in the application
/// uses, and nothing uses <c>IValueObject&lt;OrderId, Guid&gt;</c>. Measured under Native AOT on
/// 2026-09-27, a value object reached through a property kept only the framework interfaces used
/// elsewhere, and the OpenAPI satellite described every value object as <c>{}</c>. Custom attributes
/// are kept on every type the application keeps.
/// </para>
/// <para>
/// Nothing to write by hand, and nothing to call: read it as
/// <c>type.GetCustomAttribute&lt;GeneratedValueObjectAttribute&gt;()</c>.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class GeneratedValueObjectAttribute : Attribute
{
    /// <summary>Only <see cref="GeneratedValueObjectAttribute{TValueObject,T}"/> derives from it.</summary>
    private protected GeneratedValueObjectAttribute()
    {
    }

    /// <summary>The value object type.</summary>
    public abstract Type ValueObjectType { get; }

    /// <summary>The type the value object wraps.</summary>
    public abstract Type WrappedType { get; }

    /// <summary>
    /// Calls <paramref name="visitor"/> with the value object and its wrapped type as type arguments:
    /// code that needs them as type parameters gets them without
    /// <see cref="Type.MakeGenericType"/>, which Native AOT cannot always honour.
    /// </summary>
    /// <param name="visitor">What to do with the two types.</param>
    /// <typeparam name="TResult">What <paramref name="visitor"/> returns.</typeparam>
    /// <returns>What <paramref name="visitor"/> returned.</returns>
    public abstract TResult Accept<TResult>(IValueObjectVisitor<TResult> visitor);
}

/// <inheritdoc cref="GeneratedValueObjectAttribute"/>
/// <typeparam name="TValueObject">The value object type.</typeparam>
/// <typeparam name="T">The type the value object wraps.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedValueObjectAttribute<TValueObject, T> : GeneratedValueObjectAttribute
    where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
    where T : notnull
{
    /// <inheritdoc/>
    public override Type ValueObjectType => typeof(TValueObject);

    /// <inheritdoc/>
    public override Type WrappedType => typeof(T);

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IValueObjectVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.Visit<TValueObject, T>();
    }
}

/// <summary>
/// Code that needs a value object and its wrapped type as type parameters, handed them by
/// <see cref="GeneratedValueObjectAttribute.Accept{TResult}"/>.
/// </summary>
/// <typeparam name="TResult">What the visit produces.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IValueObjectVisitor<out TResult>
{
    /// <summary>Does the work for one value object type.</summary>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <typeparam name="T">The type the value object wraps.</typeparam>
    /// <returns>The result.</returns>
    TResult Visit<TValueObject, T>()
        where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
        where T : notnull;
}
