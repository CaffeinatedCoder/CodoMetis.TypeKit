using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Analyzers.NotGenerated;

// This project references the generators, so the declaration compiles into a value object here.

// sample: CodoMetis.TypeKit.Analyzers/cmtk0002
public readonly partial record struct OrderId : IValue<Guid>;   // CMTK0002 without CodoMetis.TypeKit.Generators
// end sample
