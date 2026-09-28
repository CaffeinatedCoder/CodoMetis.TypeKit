using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Fabrics;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// Applies the value-object aspects in every project that references this assembly, directly or
/// through another project or package.
/// </summary>
/// <remarks>
/// <para>
/// A fabric rather than <c>[Inheritable]</c> on the marker interfaces: an attribute there would make
/// CodoMetis.TypeKit depend on Metalama. The price is that a project which sees the interfaces but
/// not this assembly generates nothing, which the analyzer reports as CMTK0002.
/// </para>
/// <para>
/// Every concrete class or struct that implements a marker, directly or through another interface,
/// gets ValueObjectAspect, which validates the declaration and adds the others. Selecting
/// only direct implementations would skip <c>record struct CustomerId : IIdentifier</c> with
/// <c>IIdentifier : IValue&lt;Guid&gt;</c> silently, because the analyzer sees this assembly and stays quiet.
/// </para>
/// <para>
/// The aspect is handed what owns its companion class's name, and whether what it wraps is a value
/// object (<see cref="ValueObjectTypes.WrappedValueObjectRefusal"/>). Both read types other than its
/// target, so they are computed here, where nothing has been introduced yet, rather than in an
/// aspect. The factory below still runs in parallel on one code model, so they find a type by
/// enumerating a collection, never by <c>OfName</c>, whose index is not safe for parallel callers
/// (<see cref="CompanionClass"/>).
/// </para>
/// </remarks>
internal sealed class ValueObjectFabric : TransitiveProjectFabric
{
    public override void AmendProject(IProjectAmender amender) =>
        amender.SelectTypes(includeNestedTypes: true)
               .Where(ValueObjectTypes.IsValueObject)
               .AddAspect(type => new ValueObjectAspect(
                              CompanionClass.NameOwner(type),
                              ValueObjectTypes.WrappedValueObjectRefusal(type)));
}
