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
| `[Concrete]` | a class, record or struct that can be instantiated | an abstract or static type, and an interface. No constructor is demanded, unlike `new()` |

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

## Forwarding

A type parameter given as the argument must carry the attribute itself, or the requirement would end
at the first forwarding parameter:

```csharp
public abstract class Leaky<TEvent> : Aggregate<TEvent, AccountId>;               // CMTK0010: TEvent does not carry [Abstract]

public abstract class Sound<[Abstract] TEvent> : Aggregate<TEvent, AccountId>;    // the requirement follows the chain

public sealed class Sealed<T> : Aggregate<AccountEvent, T> where T : struct;      // struct proves [Sealed], and [Concrete]
```

A method that implements an interface's method, or overrides a base class's, carries the constraint
of the method it implements whether or not it repeats the attribute, so a call through the class is
held to what the interface declared. Repeat the attribute anyway, so the declaration says so.

`struct` proves `[Sealed]` and `[Concrete]`, and `new()` proves `[Concrete]`, so a parameter under
those C# constraints needs no attribute. The open generic inside its own body and an unbound
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
| has a primary constructor | at the type | none: every other constructor would chain to the primary one, with arguments only its author knows |
| is abstract, or an interface | no: only concrete descendants are checked | |

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
