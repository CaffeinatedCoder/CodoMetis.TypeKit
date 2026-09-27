using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// The <c>{TSelf}Extensions</c> class beside a value object: its name, its accessibility, and what
/// may already own that name.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NameOwner"/> reads the namespace's types, and it runs in the fabric, when the aspects
/// are selected and nothing has been introduced yet. Run inside the companion aspect, it read the
/// namespace while sibling instances were introducing their companion classes into it, since the
/// instances of one aspect layer run in parallel on one code model, and one build in nine then
/// missed a declared <c>TakenNameExtensions</c> and introduced a second one (CS0260 instead of
/// CMTK1007). With Metalama's concurrent build off it never missed (spikes/ConcurrentNamespaceTypes).
/// </para>
/// </remarks>
[CompileTime]
internal static class CompanionClass
{
    /// <summary><c>OrderIdExtensions</c> for <c>OrderId</c>, and for <c>Order.Id</c>.</summary>
    public static string Name(INamedType type)
    {
        var name = type.Name;

        for (var container = type.DeclaringType; container is not null; container = container.DeclaringType)
            name = container.Name + name;

        return name + "Extensions";
    }

    /// <summary>
    /// Public when the value object and every type it is nested in are public, internal when any of
    /// them is internal, and <see langword="null"/> when one is private or protected: the class sits
    /// at namespace level, so it can neither be more visible than the value object (CS0051) nor refer
    /// to one nested as private or protected (CS0122).
    /// </summary>
    public static Accessibility? NamespaceLevelAccessibility(INamedType type)
    {
        var result = Accessibility.Public;

        for (INamedType? current = type; current is not null; current = current.DeclaringType)
        {
            switch (current.Accessibility)
            {
                case Accessibility.Public:
                    break;
                case Accessibility.Internal:
                    result = Accessibility.Internal;
                    break;
                default:
                    return null;
            }
        }

        return result;
    }

    /// <summary>
    /// What already has the companion class's name in the value object's namespace: a declared type,
    /// or another value object whose own companion would get the same name. Both value objects of a
    /// clash get the answer, so both report it. Called from the fabric only, before any introduction.
    /// </summary>
    public static string? NameOwner(INamedType valueObject)
    {
        var className = Name(valueObject);
        var @namespace = valueObject.ContainingNamespace;

        if (@namespace.Types.OfName(className).FirstOrDefault() is { } declared)
            return $"the type '{declared.ToDisplayString()}'";

        var rival = AllTypes(@namespace.Types)
            .FirstOrDefault(type => !type.Equals(valueObject)
                                 && ValueObjectTypes.IsValueObject(type)
                                 && NamespaceLevelAccessibility(type) is not null
                                 && Name(type) == className);

        return rival is null ? null : $"the extension class of '{rival.ToDisplayString()}'";
    }

    private static IEnumerable<INamedType> AllTypes(IEnumerable<INamedType> types) =>
        types.SelectMany(type => AllTypes(type.Types).Prepend(type));
}
