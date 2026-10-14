using CodoMetis.TypeKit;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Analyzers.RequiredConstructor;

#pragma warning disable CMTK0011 // what the sample shows the rule reporting
#pragma warning disable CS9113 // the primary constructor's parameter is the point, and unused
// sample: CodoMetis.TypeKit.Analyzers/cmtk0011
[RequireParameterlessConstructor]
public interface IEndpoint { void Map(IEndpointRouteBuilder app); }

[RequireParameterlessConstructor(ConstructorAccessibility.Internal)]
public abstract class Module { }

public sealed class OrderEndpoint(ILogger log) : IEndpoint   // CMTK0011: a primary constructor, so no parameterless one
{
    public void Map(IEndpointRouteBuilder app) { }
}

public sealed class Identity : Module;                       // CMTK0011: the implicit constructor is public, Module requires internal
// end sample
#pragma warning restore CS9113
#pragma warning restore CMTK0011
