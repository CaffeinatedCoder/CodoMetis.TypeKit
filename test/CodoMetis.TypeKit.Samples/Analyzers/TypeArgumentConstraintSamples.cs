using CodoMetis.TypeKit;

namespace ReadmeSamples.Analyzers.TypeArgumentConstraints;

public abstract record Command;

public sealed record OrderPlaced;

#pragma warning disable CMTK0010 // what the sample shows the rule reporting
// sample: CodoMetis.TypeKit.Analyzers/cmtk0010
public abstract class Decider<[Abstract] TCommand, [Record] TEvent> { }

public sealed record PlaceOrder : Command;

public sealed class OrderDecider : Decider<PlaceOrder, OrderPlaced> { }   // CMTK0010: PlaceOrder is not abstract

public abstract class Leaky<TCommand> : Decider<TCommand, OrderPlaced> { }           // CMTK0010: TCommand does not carry [Abstract]
public abstract class Sound<[Abstract] TCommand> : Decider<TCommand, OrderPlaced> { }
// end sample
#pragma warning restore CMTK0010
