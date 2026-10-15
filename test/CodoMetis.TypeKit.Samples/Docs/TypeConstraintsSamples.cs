using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Docs.TypeConstraints;

#pragma warning disable CMTK0010 // what the page shows the rule reporting
// sample: type-constraints/aggregate
public abstract record AccountEvent;

public sealed record Deposited(decimal Amount) : AccountEvent;

public readonly partial record struct AccountId : IValue<Guid>;

public abstract class Aggregate<[Abstract] TEvent, [Sealed] TId>
{
    public List<TEvent> Pending { get; } = [];
}

public sealed class Account : Aggregate<AccountEvent, AccountId>;   // fine: an abstract record, a struct

public sealed class Ledger : Aggregate<Deposited, AccountId>;       // CMTK0010: Deposited is not abstract
// end sample

// sample: type-constraints/inferred
public static class Snapshots
{
    public static string Describe<[Record] TState>(TState state) => state!.ToString()!;   // a record prints its members
}

public sealed class Balance { public decimal Amount { get; init; } }

public sealed record Statement(decimal Amount);
// end sample

// sample: type-constraints/forwarding
public abstract class Leaky<TEvent> : Aggregate<TEvent, AccountId>;               // CMTK0010: TEvent does not carry [Abstract]

public abstract class Sound<[Abstract] TEvent> : Aggregate<TEvent, AccountId>;    // the requirement follows the chain

public sealed class Sealed<T> : Aggregate<AccountEvent, T> where T : struct;      // struct proves [Sealed], and [Concrete]
// end sample

// sample: type-constraints/kinds
public sealed class Proxy<[Interface] TContract>;                     // what DispatchProxy.Create, a mock or a REST client implements
public sealed class Tracked<[Unsealed] TEntity>;                      // what a lazy-loading proxy derives from
public sealed class Index<[ValueObject] TKey, TValue>;                // a key that is a value object, whatever it wraps
public sealed class Rights<[FlagsEnum] TFlags>;                       // values that combine
public sealed class Cell<[ReadOnlyStruct] TValue>;                    // read through `in` without a defensive copy

public interface IAccounts;
public sealed class Accounts : IAccounts;
public struct Position { public int Line; }

public static class Kinds
{
    public static Proxy<IAccounts> Contract = new();
    public static Proxy<Accounts> Implementer = new();                // CMTK0010: Accounts is not an interface
    public static Tracked<Accounts> Sealed = new();                   // CMTK0010: Accounts is not a class that can be derived from
    public static Index<AccountId, string> Keyed = new();             // AccountId : IValue<Guid>, from above
    public static Index<Guid, string> Bare = new();                   // CMTK0010: Guid is not a value object (IValue<T>)
    public static Rights<FileAccess> Combined = new();                // FileAccess is marked [Flags]
    public static Rights<DayOfWeek> Plain = new();                    // CMTK0010: DayOfWeek is not an enum marked [Flags]
    public static Cell<DateOnly> Pinned = new();
    public static Cell<Position> Mutable = new();                     // CMTK0010: Position is not a readonly struct
}
// end sample

// sample: type-constraints/pairs
public class Converter<[Distinct] TFrom, [Distinct] TTo>;                  // converting a type to itself is a mistake

public sealed class OneOf<[Unrelated] TFirst, [Unrelated] TSecond>          // one implicit conversion per side
{
    public static implicit operator OneOf<TFirst, TSecond>(TFirst value) => new();
    public static implicit operator OneOf<TFirst, TSecond>(TSecond value) => new();
}

public abstract record Shape;
public sealed record Circle(double Radius) : Shape;

public sealed class Round : Converter<Shape, Circle>;                 // fine: different types, related or not
public sealed class Identity : Converter<Circle, Circle>;             // CMTK0010: Circle is the same type as the argument for TFrom

public static class Picks
{
    public static OneOf<Shape, Circle> Narrowed = new Circle(1);      // CMTK0010: Circle derives from Shape; a Circle held as a Shape takes the first side
    public static OneOf<Shape, string> Fine = "none";
}
// end sample
#pragma warning restore CMTK0010

#pragma warning disable CMTK0011 // what the page shows the rule reporting
#pragma warning disable CS9113 // the constructor's parameter is the point, and unused
// sample: type-constraints/constructor
[RequireParameterlessConstructor]
public interface IJob
{
    Task RunAsync(CancellationToken cancellationToken);
}

[RequireParameterlessConstructor(ConstructorAccessibility.Internal)]
public abstract class Plugin;

public sealed class Nightly : IJob                         // fine: no constructor declared, so the implicit one is public
{
    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Report : IJob                          // CMTK0011: no parameterless constructor; the fix adds one
{
    public Report(TimeProvider clock) { }

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Importer : Plugin;                     // CMTK0011: the implicit constructor is public, Plugin requires internal
// end sample
#pragma warning restore CS9113
#pragma warning restore CMTK0011

public static class Usage
{
    public static (string Shown, string Hidden) Inferred()
    {
#pragma warning disable CMTK0010 // what the page shows the rule reporting
        // sample: type-constraints/inferred-use
        string shown = Snapshots.Describe(new Statement(10m));   // "Statement { Amount = 10 }"
        string hidden = Snapshots.Describe(new Balance());       // CMTK0010: Balance is not a record, inferred at the call
        // end sample
#pragma warning restore CMTK0010

        return (shown, hidden);
    }
}

public sealed class TypeConstraintsTests
{
    private const string Readme = "docs/type-constraints.md";

    [Fact]
    public void A_record_prints_as_the_comment_says()
    {
        var (shown, hidden) = Usage.Inferred();

        shown.ShouldBeShownIn(Readme, text => $"\"{text}\"", "Snapshots.Describe(new Statement(10m))");
        hidden.ShouldBe("ReadmeSamples.Docs.TypeConstraints.Balance", "a class prints its name, which is what the rule guards against");
    }

    /// <summary>The attributes carry no run-time check: the types the page marks as reported still exist and run.</summary>
    [Fact]
    public void The_guarantee_is_the_build()
    {
        new Ledger().Pending.ShouldBeEmpty();
        new Account().Pending.Add(new Deposited(1m));
        typeof(Aggregate<,>).GetGenericArguments()[0].GetCustomAttributes(typeof(AbstractAttribute), false).ShouldNotBeEmpty();
        new Identity().ShouldNotBeNull();
        Kinds.Mutable.ShouldNotBeNull();
        typeof(FileAccess).IsDefined(typeof(FlagsAttribute), false).ShouldBeTrue("what the page says of FileAccess");
        Picks.Narrowed.ShouldNotBeNull();
        typeof(Fault<,>).GetGenericArguments().ShouldAllBe(parameter => parameter.GetCustomAttributes(typeof(DistinctAttribute), false).Length == 1);
        typeof(Fault<,,>).GetGenericArguments().ShouldAllBe(parameter => parameter.GetCustomAttributes(typeof(DistinctAttribute), false).Length == 1);
        typeof(Report).GetConstructor(Type.EmptyTypes).ShouldBeNull("what CMTK0011 reports");
        typeof(Nightly).GetConstructor(Type.EmptyTypes).ShouldNotBeNull();
    }
}
