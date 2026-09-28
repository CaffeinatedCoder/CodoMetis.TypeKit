using System.Diagnostics.CodeAnalysis;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Analyzers.SetsRequired;

public readonly partial record struct OrderId : IValue<Guid>;

#pragma warning disable CMTK0006 // what the sample shows the rule reporting
// sample: CodoMetis.TypeKit.Analyzers/cmtk0006-sets-required
public sealed class Order
{
    [SetsRequiredMembers] public Order() { }   // CMTK0006 on Id: the constructor promises, and does not
    public required OrderId Id { get; init; }
}
// end sample
#pragma warning restore CMTK0006
