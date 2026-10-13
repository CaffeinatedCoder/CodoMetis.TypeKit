namespace CodoMetis.TypeKit;

/// <summary>
/// Requires the type argument for this type parameter to be an abstract class or record. C# has no
/// <c>where T : abstract</c>; the CodoMetis.TypeKit analyzer reports a closed use that breaks it
/// (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[Abstract]</c>
/// itself, so the requirement follows the generic chain instead of ending at the first forwarding
/// parameter. An interface is not abstract in this sense. Reflection (<c>MakeGenericType</c>) is
/// out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class AbstractAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be sealed: a sealed class or record, a
/// struct or an enum. C# has no <c>where T : sealed</c>; the CodoMetis.TypeKit analyzer reports a
/// closed use that breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[Sealed]</c> itself or is
/// constrained to <c>struct</c>. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class SealedAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be a record, class or struct. C# has no
/// <c>where T : record</c>; the CodoMetis.TypeKit analyzer reports a closed use that breaks it
/// (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[Record]</c> itself.
/// Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class RecordAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be concrete: a class, record or struct that
/// is not abstract, not static and not an interface. <c>new()</c> says that too, and demands a public
/// parameterless constructor with it; this does not. The CodoMetis.TypeKit analyzer reports a closed
/// use that breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[Concrete]</c> itself or
/// is constrained to <c>struct</c> or <c>new()</c>. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class ConcreteAttribute : Attribute;
