# Type constraints

C# has `where T : class`, `struct`, `new()` and a base type, but no `where T : abstract`, `sealed`
or `record`, and no way to demand a parameterless constructor of a given accessibility. The base
package has attributes that say what C# cannot, and the analyzers report the type that breaks them,
at build time, in whichever project declares it. CMTK0010 and CMTK0011 are both errors; the
[Analyzers README](../src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0010) is their reference. The
examples import `CodoMetis.TypeKit`.

## Constraints on a type parameter

| Attribute | Accepts | Refuses |
|---|---|---|
| `[Abstract]` | an abstract class or record | a concrete type, and an interface |
| `[Sealed]` | a sealed class or record, a struct, an enum | a type that can be derived from |
| `[Record]` | a record class or record struct | a class or a struct |
| `[Concrete]` | a class, record or struct that can be instantiated, an enum, a delegate | an abstract or static type, an interface, and the abstract bases `Enum`, `Delegate` and `ValueType`. No constructor is demanded, unlike `new()` |
| `[Distinct]`, on two or more parameters | different types for them, related or not | one type twice, nullable annotations aside |
| `[Unrelated]`, on two or more parameters | types with no implicit identity, reference or boxing conversion between them | a base and a derived type, an interface and an implementer, `object` and anything, a struct and `ValueType`; it implies `[Distinct]` |

An aggregate whose events are a closed family, and whose identifier is a value object:

```csharp
public abstract record AccountEvent;

public sealed record Deposited(decimal Amount) : AccountEvent;

public readonly partial record struct AccountId : IValue<Guid>;

public abstract class Aggregate<[Abstract] TEvent, [Sealed] TId>
{
    public List<TEvent> Pending { get; } = [];
}

public sealed class Account : Aggregate<AccountEvent, AccountId>;   // fine: an abstract record, a struct

public sealed class Ledger : Aggregate<Deposited, AccountId>;       // CMTK0010: Deposited is not abstract
```

The rule reports at every closed use of the generic: a base type as above, an implemented interface,
a field, a property, a parameter, a local, a `typeof`, a call, and a method group converted to a
delegate, whether its type arguments are written or inferred. A written argument is reported where it
is written; an inferred one at the call, or at the method group, whose arguments come from the delegate.

```csharp
public static class Snapshots
{
    public static string Describe<[Record] TState>(TState state) => state!.ToString()!;   // a record prints its members
}

public sealed class Balance { public decimal Amount { get; init; } }

public sealed record Statement(decimal Amount);
```

```csharp
string shown = Snapshots.Describe(new Statement(10m));   // "Statement { Amount = 10 }"
string hidden = Snapshots.Describe(new Balance());       // CMTK0010: Balance is not a record, inferred at the call
```

## Two parameters apart

The first four constrain one parameter. `[Distinct]` and `[Unrelated]` constrain a pair, so they go on
both parameters, and a breach is reported once, at the later argument. A converter from a type to itself
is a mistake, and a choice between two types routes a value by its static type, so a derived type beside
its base takes the base's side whenever the value is held as the base:

```csharp
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
```

Where one parameter of a pair carries `[Unrelated]` and the other `[Distinct]`, the stricter applies;
a parameter carrying neither is in no pair, and one parameter alone with the attribute constrains
nothing. `Fault<T1, T2>` and `Fault<T1, T2, T3>` declare theirs `[Distinct]`, since with one type twice
every conversion and `Of` would be ambiguous.

## Forwarding

A type parameter given as the argument must carry the attribute itself, or be constrained as the
table below says, or the requirement would end at the first forwarding parameter:

```csharp
public abstract class Leaky<TEvent> : Aggregate<TEvent, AccountId>;               // CMTK0010: TEvent does not carry [Abstract]

public abstract class Sound<[Abstract] TEvent> : Aggregate<TEvent, AccountId>;    // the requirement follows the chain

public sealed class Sealed<T> : Aggregate<AccountEvent, T> where T : struct;      // struct proves [Sealed], and [Concrete]
```

A method that implements an interface's method, or overrides a base class's, carries the constraint
of the method it implements whether or not it repeats the attribute, so a call through the class is
held to what the interface declared. Repeat the attribute anyway, so the declaration says so.

A parameter whose C# constraints already leave it no argument that breaks the attribute needs no
attribute:

| Attribute | Proved by |
|---|---|
| `[Abstract]` | nothing but `[Abstract]`: no C# constraint excludes a concrete type |
| `[Sealed]` | `struct` or `unmanaged`, or a bound `where T : U` with `U` proving `[Sealed]`, since a sealed type has no other subtype |
| `[Record]` | a bound that is a record, `where T : AccountEvent`, or a `U` proving `[Record]`, since only records derive from records |
| `[Concrete]` | `new()`, or anything that proves `[Sealed]`, `[Sealed]` itself included, since a sealed type is never abstract |
| `[Distinct]`, for a pair | both parameters carrying `[Distinct]` or `[Unrelated]` in one generic, whose closed uses are checked in turn; `struct` or `unmanaged` against `class` or a class bound; two class bounds neither deriving from the other; against a type, C# constraints the type breaks |
| `[Unrelated]`, for a pair | both carrying `[Unrelated]` in one generic; `struct` or `unmanaged` against a class bound, or a class, other than `object`, `ValueType` and `Enum`; two class bounds neither deriving from the other. `class` alone proves nothing: `object` satisfies it and is related to everything |

The open generic inside its own body and an unbound
`typeof(Aggregate<,>)` are never reported. Reflection, `MakeGenericType`, is out of reach, and the
attributes carry no run-time check: the guarantee is the build.

## A parameterless constructor

A type the host creates by reflection, an endpoint, a module, an entity configuration, a job, needs
a parameterless constructor of a given accessibility, and C# checks that only through `new()`, which
demands a public one and only where a type parameter says so. `[RequireParameterlessConstructor]` on
the interface or base class says it once; CMTK0011 appears on the concrete type that lacks the
constructor, directly or through any chain of base classes and interfaces, and the code fix writes
it.

```csharp
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
```

| The type | Reported | The fix |
|---|---|---|
| declares no constructor, and `Public` is required | no: the implicit constructor is public | |
| declares no constructor, and another accessibility is required | at the type | adds `internal Importer() { }` |
| declares constructors, none parameterless | at the type | adds one |
| has a parameterless constructor of the wrong accessibility | at the constructor | changes it |
| has a primary constructor, in any of its parts | at the type | none: every other constructor would chain to the primary one, with arguments only its author knows |
| is abstract, or an interface | no: only concrete descendants are checked | |
| is a struct or a record struct | no: its parameterless constructor is public and can be nothing else (CS8958), so it satisfies `Public` as it is and no other accessibility can be asked of it | |

A record's copy constructor does not count. The attribute is read from metadata, so the marked
interface can live in another package, and a host's contract reaches every implementation in every
project. It is a check and not an aspect that adds the constructor, since an aspect runs only in a
project that compiles with Metalama, and the error has to appear in one that does not.

## Severity

Both rules are errors, since the code is wrong whatever surrounds it: a type argument that breaks
what its parameter declares is what the host's reflection-driven construction then fails on, at run
time, where the build could have said so. Both arrived in 2.0.0, a major version, which is the
only kind that adds a rule at Error. The ids are public contract; the severity follows
`.editorconfig` as every rule's does (`dotnet_diagnostic.CMTK0010.severity = warning`).
