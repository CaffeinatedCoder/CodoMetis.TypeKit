using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Fabrics;
using Spike.Abstractions;

namespace Spike.ValueObjects;

/// <summary>
/// Replaces [Inheritable] on the marker interfaces: every project that references this assembly
/// gets the aspect on each type that directly implements IValue&lt;&gt; or IValidatedValue&lt;,,&gt;.
/// </summary>
internal sealed class ValueObjectFabric : TransitiveProjectFabric
{
    public override void AmendProject(IProjectAmender amender)
    {
        var valueObjects = amender.SelectTypes()
                                  .Where(t => t.TypeKind is TypeKind.Struct or TypeKind.Class
                                           && !t.IsAbstract
                                           && ValueObjectTypes.GetKind(t) != null);

        valueObjects.AddAspect(t => new ValueObjectAspect(ValueObjectTypes.GetKind(t)!.Value));
        valueObjects.AddAspect<ExtraAspect1>();
        valueObjects.AddAspect<ExtraAspect2>();
        valueObjects.AddAspect<ExtraAspect3>();
        valueObjects.AddAspect<ExtraAspect4>();
    }
}
