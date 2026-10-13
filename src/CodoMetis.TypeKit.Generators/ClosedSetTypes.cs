using CodoMetis.TypeKit.ClosedSets;
using Metalama.Framework.Aspects;
using Metalama.Framework.Code;

namespace CodoMetis.TypeKit.Generators;

/// <summary>What the fabric and the aspect know about closed sets: a class implementing <c>IClosedSet&lt;TSelf&gt;</c>, directly or through another interface.</summary>
[CompileTime]
internal static class ClosedSetTypes
{
    public static bool IsClosedSet(INamedType type) =>
        type.TypeKind == TypeKind.Class && Marker(type) is not null;

    /// <summary>The <c>IClosedSet&lt;TSelf&gt;</c> the type implements, or null.</summary>
    public static INamedType? Marker(INamedType type) =>
        type.AllImplementedInterfaces.FirstOrDefault(i => i.IsGeneric && i.Definition.Equals(TypeFactory.GetNamedType(typeof(IClosedSet<>))));

    /// <summary>The members: every public static readonly field of the set's own type, in declaration order.</summary>
    public static IReadOnlyList<IField> Members(INamedType type) =>
        [.. type.Fields.Where(f => f.IsStatic && f.Accessibility == Accessibility.Public && f.Type.Equals(type) && f.Writeability == Writeability.ConstructorOnly)];
}
