using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

public static class Guard
{
    public static Option<int> Nothing() => default(Option<int>);
    public static Fault<DayOfWeek, DayOfWeek> Twice() => throw new NotSupportedException();
}

public readonly partial record struct Ungenerated : IValue<Guid>;

public sealed class Proxy<[Interface] T> { }
public sealed class Derivable<[Unsealed] T> { }
public sealed class Keyed<[ValueObject] T> { }
public sealed class Named<[ClosedSet] T> { }
public sealed class Flagged<[FlagsEnum] T> { }
public sealed class Pinned<[ReadOnlyStruct] T> { }
public sealed class Apart<[Unrelated] TLeft, [Unrelated] TRight> { }
public struct Cursor { public int Position; }

public static class Kinds
{
    public static Proxy<string> Implementer = new();
    public static Derivable<string> Sealed = new();
    public static Keyed<Guid> Bare = new();
    public static Named<DayOfWeek> Enum = new();
    public static Flagged<DayOfWeek> Plain = new();
    public static Pinned<Cursor> Mutable = new();
    public static Apart<object, Cursor> Boxed = new();
}
