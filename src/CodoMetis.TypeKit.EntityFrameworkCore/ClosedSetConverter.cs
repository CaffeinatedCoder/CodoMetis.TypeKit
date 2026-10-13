using System.ComponentModel;
using System.Linq.Expressions;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Stores a closed set's member as its name, and reads the member itself back with the set's own
/// <c>TryParse</c>.
/// </summary>
/// <remarks>
/// <c>UseTypeKit()</c> applies it to every closed-set property by itself, so it only needs to be
/// named to configure a property explicitly. A name the set no longer has is an
/// <see cref="InvalidOperationException"/>, never a null.
/// </remarks>
/// <typeparam name="TSet">The closed set.</typeparam>
public sealed class ClosedSetConverter<TSet> : ValueConverter<TSet, string>
    where TSet : class, IClosedSet<TSet>
{
    // Both directions call a public static method of this class. EF's compiled model
    // (dotnet ef dbcontext optimize, which Native AOT requires) writes these expressions out as C# in
    // the application's own assembly, where an internal helper does not compile (CS0122, measured with
    // EF Core 10.0.12); and an expression tree can call neither the static abstract TryParse nor
    // anything with an out variable.
    internal static readonly Expression<Func<TSet, string>> ToProvider   = member => ProviderValue(member);
    internal static readonly Expression<Func<string, TSet>> FromProvider = name => MemberNamed(name);

    /// <summary>Creates the converter.</summary>
    public ClosedSetConverter()
        : this(null)
    {
    }

    /// <summary>Creates the converter with hints for the text column.</summary>
    /// <param name="mappingHints">Size or Unicode hints for the column.</param>
    public ClosedSetConverter(ConverterMappingHints? mappingHints)
        : base(ToProvider, FromProvider, mappingHints)
    {
    }

    /// <summary>The value to store: the member's name.</summary>
    /// <remarks>Public for the code EF's compiled model generates; nothing else needs to call it.</remarks>
    /// <param name="member">The member.</param>
    /// <returns>Its name.</returns>
    /// <exception cref="InvalidOperationException">The instance is not one of the set's members.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string ProviderValue(TSet member) => member.Name;

    /// <summary>The member of a stored name.</summary>
    /// <remarks>Public for the code EF's compiled model generates; nothing else needs to call it.</remarks>
    /// <param name="name">A name the application stored.</param>
    /// <returns>The member itself.</returns>
    /// <exception cref="InvalidOperationException">The set has no member of that name.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TSet MemberNamed(string name) =>
        TSet.TryParse(name, out var member)
            ? member
            : throw new InvalidOperationException($"{typeof(TSet).Name} has no member of that name: the column holds a name the set no longer has.");
}
