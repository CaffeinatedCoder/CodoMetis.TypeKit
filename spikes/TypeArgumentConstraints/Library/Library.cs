using CodoMetis.TypeKit;

namespace Library;

public abstract record Command;
public abstract record Event;
public sealed record PlaceOrder : Command;
public sealed record OrderPlaced : Event;
public abstract class Handler { }
public interface IHandler { }
public sealed class OrderHandler : Handler { }
public struct Point { }

/// <summary>The library author's generic: the command and event families must be abstract records.</summary>
public abstract class Decider<[Abstract, Record] TCommand, [Abstract, Record] TEvent>
{
    public Decider<TCommand, TEvent>? Next;                 // claim 10: the open generic in its own body, silent
    public static Type Open => typeof(Decider<,>);         // claim 10: unbound, silent
}

public sealed class Box<[Sealed] T> { }

public sealed class Factory<[Concrete] T> { }

public static class Runner
{
    public static void Run<[Abstract] T>(T command) { }
    public static Box<T> Boxed<T>() where T : struct => new();     // claim 5: struct proves [Sealed]
}
