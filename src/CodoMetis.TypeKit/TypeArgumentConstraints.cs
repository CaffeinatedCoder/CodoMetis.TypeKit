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

/// <summary>
/// Requires the type arguments of every parameter of the same generic that carries this attribute to be
/// different types: <c>Mapping&lt;[Distinct] TFrom, [Distinct] TTo&gt;</c> refuses <c>Mapping&lt;Dog, Dog&gt;</c>
/// and accepts <c>Mapping&lt;Pet, Dog&gt;</c>. C# cannot say it; the CodoMetis.TypeKit analyzer reports a
/// closed use that breaks it (CMTK0010), at the later of the two arguments.
/// </summary>
/// <remarks>
/// Two types are the same when they are one symbol, their nullable annotations aside, and <c>dynamic</c>
/// is <c>object</c>. Two type parameters given as the arguments are told apart when both carry
/// <c>[Distinct]</c> or <see cref="UnrelatedAttribute"/> in the same generic, or when their C# constraints
/// admit no common type: <c>struct</c> against <c>class</c> or a class bound, or two class bounds neither
/// of which derives from the other. A type parameter against a type is told apart when the type breaks the
/// parameter's C# constraints. One parameter alone with the attribute constrains nothing. Reflection is out
/// of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class DistinctAttribute : Attribute;

/// <summary>
/// Requires the type arguments of every parameter of the same generic that carries this attribute to be
/// unrelated types: neither derives from, implements, boxes to or is the other, so
/// <c>Either&lt;[Unrelated] TLeft, [Unrelated] TRight&gt;</c> refuses <c>Either&lt;Pet, Dog&gt;</c>,
/// <c>Either&lt;IAnimal, Dog&gt;</c> and <c>Either&lt;object, Point&gt;</c>, where a value of the derived
/// type held as the base would take the base's side. It implies <see cref="DistinctAttribute"/>. C#
/// cannot say it; the CodoMetis.TypeKit analyzer reports a closed use that breaks it (CMTK0010), at the
/// later of the two arguments.
/// </summary>
/// <remarks>
/// Related means an implicit identity, reference or boxing conversion in either direction; a numeric or a
/// nullable conversion is none, so <c>int</c> beside <c>long</c> or <c>int?</c> is unrelated. Two
/// interfaces are unrelated even though one type may implement both. Two type parameters given as the
/// arguments are told apart when both carry <c>[Unrelated]</c> in the same generic, or when their C#
/// constraints admit no related types: <c>struct</c> against a class bound other than <c>object</c>, or
/// two class bounds neither of which derives from the other. A type parameter against a type is told
/// apart on the same grounds; <c>class</c> alone proves nothing, since <c>object</c> is related to
/// everything. Where a pair carries <c>[Unrelated]</c> on one parameter and <c>[Distinct]</c> on the
/// other, the stricter applies. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UnrelatedAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be an interface: the contract a proxy
/// (<c>DispatchProxy.Create</c>), a mock or a REST client implements, which fail at run time for a class.
/// C# has no <c>where T : interface</c>; the CodoMetis.TypeKit analyzer reports a closed use that breaks
/// it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[Interface]</c> itself:
/// <c>where T : IContract</c> admits every class that implements it. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class InterfaceAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be a class that can be derived from: a class or
/// record class that is neither sealed nor static, abstract included, which a proxy or a lazy-loading
/// entity can subclass. An interface is implemented, not derived from, and is refused; see
/// <see cref="InterfaceAttribute"/>. The CodoMetis.TypeKit analyzer reports a closed use that breaks it
/// (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[Unsealed]</c> itself: no
/// C# constraint excludes a sealed class. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class UnsealedAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be a value object, a struct or class implementing
/// <c>IValue&lt;T&gt;</c> for any <c>T</c>, which no <c>where T : IValue&lt;?&gt;</c> can say. An interface is
/// none, even one deriving from the marker. The CodoMetis.TypeKit analyzer reports a closed use that breaks
/// it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[ValueObject]</c> itself, or is
/// bound to <c>IValue&lt;T&gt;</c>, <c>IValidatedValue&lt;TSelf, T, TFault&gt;</c>, a type implementing
/// one, or a type parameter that satisfies it, beside a constraint no interface meets: <c>struct</c>,
/// <c>unmanaged</c>, <c>new()</c> or a class bound. <c>class</c> admits an interface. Reflection is out of
/// the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class ValueObjectAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be a closed set, a type implementing
/// <c>IClosedSet&lt;TSelf&gt;</c>, a fault set (<c>IFaultSet&lt;TSelf&gt;</c>) included, which no
/// <c>where T : IClosedSet&lt;?&gt;</c> can say. The CodoMetis.TypeKit analyzer reports a closed use that
/// breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it when it carries <c>[ClosedSet]</c> itself, or is
/// bound to <c>IClosedSet&lt;TSelf&gt;</c>, a type implementing it, or a type parameter that satisfies it.
/// Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class ClosedSetAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be an enum marked <c>[Flags]</c>, whose values
/// combine; <c>where T : Enum</c> admits every enum. The CodoMetis.TypeKit analyzer reports a closed use
/// that breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[FlagsEnum]</c> itself: an
/// enum is sealed, so no bound names one. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class FlagsEnumAttribute : Attribute;

/// <summary>
/// Requires the type argument for this type parameter to be a <c>readonly struct</c> or a
/// <c>readonly record struct</c>, which an <c>in</c> parameter or a <c>ref readonly</c> reads without a
/// defensive copy; <c>where T : struct</c> admits a mutable one. An enum is no struct declaration and is
/// refused. The CodoMetis.TypeKit analyzer reports a closed use that breaks it (CMTK0010).
/// </summary>
/// <remarks>
/// A type parameter given as the argument satisfies it only when it carries <c>[ReadOnlyStruct]</c>
/// itself: no C# constraint excludes a mutable struct. Reflection is out of the analyzer's reach.
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class ReadOnlyStructAttribute : Attribute;
