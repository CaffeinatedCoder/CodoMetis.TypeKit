namespace CodoMetis.TypeKit;

/// <summary>
/// Requires the type argument for this type parameter to be an abstract class or record. C# has no
/// <c>where T : abstract</c>; the CodoMetis.TypeKit analyzer reports a closed use that breaks it
/// (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[Abstract]</c>
/// itself, so the requirement follows the generic chain instead of ending at the first forwarding
/// parameter; no C# constraint excludes a concrete type. An interface is not abstract in this sense.
/// Reflection (<c>MakeGenericType</c>) is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class AbstractAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be sealed: a sealed class or record, a
/// struct, an enum, a delegate or an array. C# has no <c>where T : sealed</c>; the CodoMetis.TypeKit
/// analyzer reports a closed use that breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[Sealed]</c> itself, is
/// constrained to <c>struct</c> or <c>unmanaged</c>, or is bound (<c>where T : U</c>) to a type
/// parameter that satisfies it, since a sealed type has no other subtype. Reflection is out of the
/// analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class SealedAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be a record, class or struct. C# has no
/// <c>where T : record</c>; the CodoMetis.TypeKit analyzer reports a closed use that breaks it
/// (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[Record]</c> itself, or is
/// bound to a record (<c>where T : AccountEvent</c>) or to a type parameter that satisfies it, since
/// only records derive from records. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class RecordAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be concrete: a class, record or struct that
/// is not abstract, not static and not an interface, an enum, a delegate or an array. <c>new()</c>
/// says that too, and demands a public parameterless constructor with it; this does not. The
/// CodoMetis.TypeKit analyzer reports a closed use that breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[Concrete]</c> itself, is
/// constrained to <c>new()</c>, or satisfies <see cref="SealedAttribute"/>, since a sealed type is
/// never abstract. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class ConcreteAttribute : Attribute;
