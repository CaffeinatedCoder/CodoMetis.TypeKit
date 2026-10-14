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
/// <see cref="TryParse"/> by name, <see cref="IParsable{TSelf}"/> explicitly, <c>ToString()</c>, a JSON
/// converter that writes the name and reads the member back, a type converter for what reads a string
/// through <c>TypeDescriptor</c> (the configuration binder, MVC), and a private parameterless
/// constructor where the type declares none. A member
/// may carry constant data through a private constructor the type declares, as
/// <c>new(FaultKind.NotFound, "The order does not exist.")</c>.
/// </para>
/// <para>
/// Unlike an <c>enum</c>, a closed set's <c>switch</c> is complete by construction: <c>Match</c> takes
/// a delegate per member, so a member added later fails every call site until it is handled. Its
/// arguments are named (CMTK0012), since by position they would follow the declaration order, and
/// it refuses a null delegate before picking one. The type is a sealed class, so members are
/// singletons and equality is reference equality.
/// </para>
/// <para>
/// The type must be a sealed partial class (CMTK1013, CMTK1014) with at least one member (CMTK1015),
/// only private constructors (CMTK1016), no other public static member of its own type (CMTK1017),
/// and not a record (CMTK1020), since every one of those would make an instance the generators cannot
/// see as a member. Each member is initialized with <c>new</c> where it is declared, and with nothing
/// else (CMTK1019), and never written again (CMTK0013): a member initialized or assigned with another
/// member would give that instance two names, or answer to another's. A
/// member takes no name the generators use, nor another's <c>Match</c> parameter (CMTK1021). A
/// <c>ToString()</c> written by hand is kept, as is an overload of a generated method with another
/// signature; any other member the generators introduce, written by hand, is CMTK1011.
/// </para>
/// <para>
/// An instance the set's own code creates beside its members has no name: <see cref="Name"/>, the
/// generated <c>ToString()</c>, <c>Match</c>, JSON and the EF Core column throw for it rather than carry
/// a null.
/// A JSON <c>null</c> reads as <see langword="null"/>, as for any class; the serializer's
/// <c>RespectNullableAnnotations</c> refuses it for a property that is not nullable.
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
    /// <exception cref="InvalidOperationException">The instance is not one of the set's members.</exception>
    string Name { get; }

    /// <summary>The set's name and the member's, <c>OrderStatus.Open</c>, so two sets that share a member name stay apart.</summary>
    string QualifiedName { get; }

    /// <summary>Every member, in declaration order.</summary>
    static abstract IReadOnlyList<TSelf> All { get; }

    /// <summary>The member of that name, if any. Names compare ordinally.</summary>
    /// <param name="name">The name.</param>
    /// <param name="member">The member, or null.</param>
    /// <returns>Whether a member has that name.</returns>
    static abstract bool TryParse([NotNullWhen(true)] string? name, [MaybeNullWhen(false)] out TSelf member);
}
