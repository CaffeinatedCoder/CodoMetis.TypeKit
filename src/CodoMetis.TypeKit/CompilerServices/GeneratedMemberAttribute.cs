using System.ComponentModel;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// Put by CodoMetis.TypeKit.Generators on every member it introduces into a value object, a closed set
/// or a value object's companion class: the contract the member implements, and which of its members.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Contract"/> is the interface the member implements (<c>IParsable&lt;OrderId&gt;</c> for the
/// generated <c>Parse(string, IFormatProvider?)</c>), or, for a member no interface declares, the marker
/// that promises it (<c>IValidatedValue&lt;Sku, string, SkuFault&gt;</c> for <c>TryFrom</c>).
/// <see cref="Member"/> is that member's name in the contract, or the generated member's own where the
/// contract declares none.
/// </para>
/// <para>
/// Code that has a member in hand reads it to tell a generated member from one written by hand, without
/// a list of names, as <see cref="GeneratedValueObjectAttribute"/> tells a value object from any other
/// type. Not <c>[GeneratedCode]</c>: Roslyn analyzers skip code marked with it by default, which would
/// silence the CMTK rules and the application's own analyzers on these members.
/// </para>
/// <para>
/// Nothing to write by hand: a hand-written one on a value object's or a closed set's member is CMTK1011,
/// as for every attribute the generators put there.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedMemberAttribute : Attribute
{
    /// <summary>Marks a generated member as implementing <paramref name="member"/> of <paramref name="contract"/>.</summary>
    /// <param name="contract">The interface the member implements, or the marker that promises it.</param>
    /// <param name="member">The name of the contract's member, or the generated member's own.</param>
    public GeneratedMemberAttribute(Type contract, string member)
    {
        Contract = contract;
        Member   = member;
    }

    /// <summary>The interface the member implements, or the marker that promises it.</summary>
    public Type Contract { get; }

    /// <summary>The name of the contract's member the generated member implements, or its own where the contract declares none.</summary>
    public string Member { get; }
}
