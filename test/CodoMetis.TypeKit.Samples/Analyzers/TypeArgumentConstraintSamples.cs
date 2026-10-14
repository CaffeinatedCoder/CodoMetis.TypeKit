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

// sample: CodoMetis.TypeKit.Analyzers/cmtk0010-pairs
public sealed class Mapping<[Distinct] TFrom, [Distinct] TTo> { }

public sealed class Either<[Unrelated] TLeft, [Unrelated] TRight>   // an implicit conversion from each side
{
    public static implicit operator Either<TLeft, TRight>(TLeft left) => new();
    public static implicit operator Either<TLeft, TRight>(TRight right) => new();
}

public class Pet { }
public sealed class Dog : Pet { }

public static class Pairs
{
    public static Mapping<Pet, Dog> Related = new();   // fine: different types, related or not
    public static Mapping<Dog, Dog> Same = new();      // CMTK0010: Dog is the same type as the argument for TFrom
    public static Either<Pet, Dog> Routed = new();     // CMTK0010: Dog derives from Pet; a Dog held as a Pet would take the left side
    public static Either<Pet, int> Fine = new();
}
// end sample
#pragma warning restore CMTK0010
