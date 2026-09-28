using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Analyzers.Unassigned;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct CustomerId : IValue<Guid>;

#pragma warning disable CMTK0006 // what the sample shows the rule reporting
// sample: CodoMetis.TypeKit.Analyzers/cmtk0006
public sealed class Order
{
    public OrderId Id { get; set; }            // CMTK0006: new Order { } and JSON without "id" leave it default
    public required CustomerId Customer { get; set; }   // required: silent
}
// end sample
#pragma warning restore CMTK0006
