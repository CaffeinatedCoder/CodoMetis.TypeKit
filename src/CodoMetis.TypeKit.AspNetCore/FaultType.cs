using CodoMetis.TypeKit.ClosedSets;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// What the satellite knows of a fault type, an enum or a closed set: its members, and each member's
/// name, qualified name, kind and description. The mapping, the startup check, the document and the
/// response read a fault type through this alone, so an enum and a closed set take the same path.
/// </summary>
/// <remarks>
/// Made once per type, by the registration overload whose constraint says which it is, so nothing
/// reflects: an enum's members come from <see cref="Enum.GetValues{TEnum}"/>, a set's from its
/// <c>All</c>. A member's kind and description are read through <see cref="IFault"/>, which a fault
/// set's members implement and an enum's never do.
/// </remarks>
internal sealed class FaultType<TFault> where TFault : notnull
{
    private readonly Func<TFault, string> _name;
    private readonly Func<TFault, string> _qualifiedName;

    internal FaultType(string typeName, IReadOnlyList<TFault> members, Func<TFault, string> name, Func<TFault, string> qualifiedName)
    {
        TypeName = typeName;
        Members = members;
        _name = name;
        _qualifiedName = qualifiedName;
    }

    /// <summary>The fault type's name, as every message names it.</summary>
    public string TypeName { get; }

    /// <summary>Every member, in declaration order.</summary>
    public IReadOnlyList<TFault> Members { get; }

    public string NameOf(TFault fault) => _name(fault);

    public string QualifiedNameOf(TFault fault) => _qualifiedName(fault);

    /// <summary>The member's kind, where it is a fault set's member; an enum's has none.</summary>
    public static FaultKind? KindOf(TFault fault) => fault is IFault member ? member.Kind : null;

    /// <summary>The member's description, where it is a fault set's member that declares one.</summary>
    public static string? DescriptionOf(TFault fault) => fault is IFault member ? member.Description : null;

    /// <summary>The member of that name, compared ordinally.</summary>
    public bool TryFind(string name, out TFault fault)
    {
        foreach (var member in Members)
        {
            if (string.Equals(NameOf(member), name, StringComparison.Ordinal))
            {
                fault = member;
                return true;
            }
        }

        fault = default!;
        return false;
    }
}

/// <summary>Makes the <see cref="FaultType{TFault}"/> of an enum or a closed set, each under the constraint that says which.</summary>
internal static class FaultTypes
{
    public static FaultType<TEnum> Enum<TEnum>() where TEnum : struct, Enum =>
        new(typeof(TEnum).Name, System.Enum.GetValues<TEnum>(), static fault => fault.ToString(), static fault => $"{typeof(TEnum).Name}.{fault}");

    public static FaultType<TSet> Set<TSet>() where TSet : class, IClosedSet<TSet> =>
        new(typeof(TSet).Name, TSet.All, static fault => fault.Name, static fault => fault.QualifiedName);
}
