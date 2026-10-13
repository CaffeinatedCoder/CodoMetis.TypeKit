using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.TypeKit.ClosedSets;

/// <summary>
/// Declares a closed set, a type whose every instance is a named member declared as a
/// <c>public static readonly</c> field of the type:
/// <c>public sealed partial class OrderStatus : IClosedSet&lt;OrderStatus&gt; { public static readonly OrderStatus Open = new(); }</c>
/// </summary>
/// <remarks>
/// <para>
/// CodoMetis.TypeKit.Generators generates everything else: <see cref="Name"/> from the field's name,
/// <see cref="QualifiedName"/> (<c>OrderStatus.Open</c>), <see cref="All"/> in declaration order,
/// <c>Match</c> with one required delegate per member named after it, <c>Parse</c> and
/// <see cref="TryParse"/> by name, <c>ToString()</c>, a JSON converter that writes the name and reads
/// the member back, and a private parameterless constructor where the type declares none. A member
/// may carry constant data through a private constructor the type declares, as
/// <c>new(FaultKind.NotFound, "The order does not exist.")</c>.
/// </para>
/// <para>
/// Unlike an <c>enum</c>, a closed set's <c>switch</c> is complete by construction: <c>Match</c> takes
/// a delegate per member, so a member added later fails every call site until it is handled. The
/// type is a sealed class, so members are singletons and equality is reference equality.
/// </para>
/// <para>
/// The type must be a sealed partial class (CMTK1013, CMTK1014) with at least one member (CMTK1015),
/// no public constructor (CMTK1016), and no other public static member of its own type (CMTK1017),
/// since every one of those would be a member the generators cannot see as one.
/// </para>
/// <para>
/// Without a reference to CodoMetis.TypeKit.Generators nothing is generated, and the analyzer
/// reports CMTK0002 on the declaration.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The set itself.</typeparam>
public interface IClosedSet<TSelf> where TSelf : class, IClosedSet<TSelf>
{
    /// <summary>The member's name: the name of the field that declares it.</summary>
    string Name { get; }

    /// <summary>The set's name and the member's, <c>OrderStatus.Open</c>, so two sets that share a member name stay apart.</summary>
    string QualifiedName { get; }

    /// <summary>Every member, in declaration order.</summary>
    static abstract IReadOnlyList<TSelf> All { get; }

    /// <summary>The member of that name, if any. Names compare ordinally.</summary>
    /// <param name="name">The name.</param>
    /// <param name="member">The member, or null.</param>
    /// <returns>Whether a member has that name.</returns>
    static abstract bool TryParse(string? name, [MaybeNullWhen(false)] out TSelf member);
}
