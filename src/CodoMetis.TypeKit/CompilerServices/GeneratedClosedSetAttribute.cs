using System.ComponentModel;
using CodoMetis.TypeKit.ClosedSets;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// What the generators put on every closed set, so run-time code that holds only a <see cref="Type"/>
/// recognises one and reaches it typed, through <see cref="Accept{TResult}"/>, without reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class GeneratedClosedSetAttribute : Attribute
{
    private protected GeneratedClosedSetAttribute()
    {
    }

    /// <summary>The set.</summary>
    public abstract Type SetType { get; }

    /// <summary>The members' names, in declaration order.</summary>
    public abstract IReadOnlyList<string> Names { get; }

    /// <summary>Calls <paramref name="visitor"/> with the set as a type argument.</summary>
    /// <param name="visitor">The visitor.</param>
    /// <typeparam name="TResult">What the visitor makes.</typeparam>
    /// <returns>What the visitor made.</returns>
    public abstract TResult Accept<TResult>(IClosedSetVisitor<TResult> visitor);
}

/// <summary>The closed-set attribute for <typeparamref name="TSet"/>.</summary>
/// <typeparam name="TSet">The set.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedClosedSetAttribute<TSet> : GeneratedClosedSetAttribute
    where TSet : class, IClosedSet<TSet>
{
    /// <inheritdoc/>
    public override Type SetType => typeof(TSet);

    /// <inheritdoc/>
    public override IReadOnlyList<string> Names => field ??= [.. TSet.All.Select(member => member.Name)];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IClosedSetVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit<TSet>();
    }
}

/// <summary>What <see cref="GeneratedClosedSetAttribute.Accept{TResult}"/> calls with the set as a type argument.</summary>
/// <typeparam name="TResult">What the visitor makes.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IClosedSetVisitor<out TResult>
{
    /// <summary>Called with the set.</summary>
    /// <typeparam name="TSet">The set.</typeparam>
    /// <returns>What the visitor makes.</returns>
    TResult Visit<TSet>() where TSet : class, IClosedSet<TSet>;
}
