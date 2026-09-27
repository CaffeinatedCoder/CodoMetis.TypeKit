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
/// gets the implementation aspect, which validates the declaration and adds the others. Selecting
/// only direct implementations would skip <c>record struct CustomerId : IIdentifier</c> with
/// <c>IIdentifier : IValue&lt;Guid&gt;</c> silently, because the analyzer sees this assembly and stays quiet.
/// </para>
/// </remarks>
internal sealed class ValueObjectFabric : TransitiveProjectFabric
{
    public override void AmendProject(IProjectAmender amender) =>
        amender.SelectTypes(includeNestedTypes: true)
               .Where(type => type.TypeKind is TypeKind.Struct or TypeKind.Class
                           && !type.IsAbstract
                           && ValueObjectTypes.Markers(type).Count > 0)
               .AddAspect<ValueObjectImplementationAspect>();
}
