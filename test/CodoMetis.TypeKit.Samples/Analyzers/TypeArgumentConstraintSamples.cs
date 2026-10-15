using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;

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

// sample: CodoMetis.TypeKit.Analyzers/cmtk0010-kinds
public sealed class Proxy<[Interface] TContract> { }                 // DispatchProxy.Create<TContract, TProxy> throws for a class
public sealed class LazyLoaded<[Unsealed] TEntity> { }               // a proxy derives from the entity
public sealed class Lookup<[ValueObject] TKey, TValue> { }           // a key that is a value object of any wrapped type
public sealed class Legend<[ClosedSet] TMember> { }                  // a closed set, whatever its members carry
public sealed class Permissions<[FlagsEnum] TRights> { }             // values that combine
public sealed class Slot<[ReadOnlyStruct] TValue> { }                // read through `in` without a defensive copy

public interface IOrders { }
public sealed class Orders : IOrders { }
public struct Cursor { public int Position; }
public readonly partial record struct AccountNumber : IValue<string>;
public sealed partial class Tier : IClosedSet<Tier> { public static readonly Tier Basic = new(); }

public static class Kinds
{
    public static Proxy<IOrders> Contract = new();
    public static Proxy<Orders> Implementer = new();                 // CMTK0010: Orders is not an interface
    public static LazyLoaded<Orders> Sealed = new();                 // CMTK0010: Orders is not a class that can be derived from
    public static Lookup<AccountNumber, string> Keyed = new();
    public static Lookup<Guid, string> Bare = new();                 // CMTK0010: Guid is not a value object (IValue<T>)
    public static Legend<Tier> Tiers = new();
    public static Legend<DayOfWeek> Days = new();                    // CMTK0010: DayOfWeek is not a closed set (IClosedSet<TSelf>)
    public static Permissions<FileAccess> Combined = new();          // FileAccess is marked [Flags]
    public static Permissions<DayOfWeek> Plain = new();              // CMTK0010: DayOfWeek is not an enum marked [Flags]
    public static Slot<DateOnly> Pinned = new();
    public static Slot<Cursor> Mutable = new();                      // CMTK0010: Cursor is not a readonly struct
}
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
