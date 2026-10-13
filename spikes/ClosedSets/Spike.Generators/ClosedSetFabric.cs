using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Fabrics;

namespace Spike.Generators;

internal sealed class ClosedSetFabric : TransitiveProjectFabric
{
    public override void AmendProject(IProjectAmender amender) =>
        amender.SelectTypes(includeNestedTypes: true)
               .Where(type => type.TypeKind == TypeKind.Class
                           && type.AllImplementedInterfaces.Any(i => i.IsGeneric && i.Definition.Equals(TypeFactory.GetType(typeof(IClosedSet<>)))))
               .AddAspect(_ => new ClosedSetAspect());
}
