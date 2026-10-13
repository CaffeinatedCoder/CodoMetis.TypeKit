namespace CodoMetis.TypeKit;

/// <summary>
/// Requires every concrete type that derives from or implements the marked type to have a
/// parameterless constructor of the given accessibility, which the CodoMetis.TypeKit analyzer
/// checks (CMTK0011) and its code fix writes.
/// </summary>
/// <remarks>
/// <para>
/// For a type the host creates by reflection: an endpoint, a module, an entity configuration, a
/// serializer's target. C# can demand a constructor only through <c>new()</c>, which demands a
/// public one and only where a type parameter says so; this attribute states it on the interface or
/// base class, and the error appears on the type that lacks it, in whichever project declares it.
/// </para>
/// <para>
/// A class that declares no constructor has a public parameterless one and satisfies
/// <see cref="ConstructorAccessibility.Public"/> as it is. A record or class with a primary
/// constructor has none, and its fix is written by hand, since every other constructor of such a
/// type chains to the primary one.
/// </para>
/// </remarks>
/// <param name="accessibility">The accessibility the constructor must have.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class RequireParameterlessConstructorAttribute(ConstructorAccessibility accessibility = ConstructorAccessibility.Public) : Attribute
{
    /// <summary>The accessibility the constructor must have.</summary>
    public ConstructorAccessibility Accessibility { get; } = accessibility;
}
