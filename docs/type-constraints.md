# Type constraints

Tell a generic which types it accepts, beyond what a `where` clause can say. You put an attribute
such as `[Abstract]` or `[Sealed]` on the type parameter, and the build fails wherever someone passes
a type that doesn't fit.

C# has `where T : class`, `struct`, `new()` and a base type. It has no `where T : abstract`,
`sealed`, `record` or `interface`. It can't ask for a marker interface with any type argument, keep
two parameters apart, or demand a parameterless constructor of a given accessibility. The base
package has attributes for all of these. The analyzers report the type that breaks one at build
time, in whichever project declares it.

Both rules, CMTK0010 for type arguments and CMTK0011 for constructors, are errors. The
[Analyzers README](../src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0010) is their reference. The
examples import `CodoMetis.TypeKit`.

## Constrain a type parameter

Here is an aggregate whose events are a closed family, and whose identifier is a value object:

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

`Account` builds, because `AccountEvent` is an abstract record and `AccountId` is a struct. `Ledger`
fails the build (CMTK0010), because `Deposited` isn't abstract.

Each attribute accepts some kinds of type and rejects the rest:

| Attribute | Accepts | Rejects |
|---|---|---|
| `[Abstract]` | an abstract class or record | a concrete type, and an interface |
| `[Sealed]` | a sealed class or record, a struct, an enum | a type that can be derived from |
| `[Record]` | a record class or record struct | a class or a struct |
| `[Concrete]` | a class, record or struct that can be instantiated, an enum, a delegate | an abstract or static type, an interface, and the abstract bases `Enum`, `Delegate` and `ValueType`. Unlike `new()`, it demands no constructor |
| `[Interface]` | an interface | a class, even one that implements the interface |
| `[Unsealed]` | a class or record class that can be derived from, abstract included, and `dynamic`, which is `object` | a sealed or static class, an interface, a struct |
| `[ValueObject]` | a struct or class that implements `IValue<T>` for any `T`, which no `where` can say | any other type, a closed set included, and an interface, even one that derives from `IValue<T>` |
| `[ClosedSet]` | a type that implements `IClosedSet<TSelf>`, a fault set included | any other type, a value object or an enum included |
| `[FlagsEnum]` | an enum marked `[Flags]` | a plain enum, which `where T : Enum` admits |
| `[ReadOnlyStruct]` | a `readonly struct` or `readonly record struct`, the primitives included | a mutable struct, which `where T : struct` admits; an enum; a class |
| `[Distinct]`, on two or more parameters | different types for them, related or not | one type twice, nullable annotations aside |
| `[Unrelated]`, on two or more parameters | types with no implicit identity, reference or boxing conversion between them | a base and a derived type, an interface and an implementer, `object` and anything, a struct and `ValueType`. It implies `[Distinct]` |

The rule checks every closed use of the generic:

- **Declarations:** a base type, as above, an implemented interface, a field, a property, a
  parameter and a local.
- **Expressions:** a `typeof`, a call, and a method group converted to a delegate.
- **Methods the compiler calls for you:** a `foreach`'s `GetEnumerator`, an `await`'s `GetAwaiter`, a
  `Deconstruct` (in `var (a, b) = x`, a `foreach`, or a pattern like `x is (1, 2)`), and a collection
  expression's `Add` or builder method. Here `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` aren't
  checked until 3.0.0; every other attribute is.

It checks them whether you write the type arguments or the compiler infers them. A written argument
is reported where you wrote it. An inferred one is reported at the call, or at the method group,
whose arguments come from the delegate, or where the compiler calls the method for you: the
`foreach`'s collection, the `await`, the deconstruction, the pattern or the collection expression:

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

## The kind of a type argument

Six of the attributes ask for a kind of type that C# has no constraint for:

- **`[Interface]` and `[Unsealed]`** are what a proxy needs: a contract to implement, or a class to
  derive from. Without them, a wrong type argument fails at run time.
- **`[ValueObject]` and `[ClosedSet]`** accept their marker interface with any type argument. C# has
  no `where T : IValue<?>`, so a generic over every value object has had no constraint at all.
- **`[FlagsEnum]` and `[ReadOnlyStruct]`** narrow what `where T : Enum` and `where T : struct` admit.

```csharp
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
```

The closed-set example is in the
[Analyzers README](../src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0010).

## Two parameters apart

The attributes above each constrain one parameter. `[Distinct]` and `[Unrelated]` constrain a pair,
so you put them on both parameters. A breach is reported once, at the later argument.

- **`[Distinct]`** keeps one type out of both places. A converter from a type to itself is a mistake.
- **`[Unrelated]`** also keeps a type and its base apart. A choice between two types routes a value
  by its static type. So a derived type beside its base takes the base's side whenever the value is
  held as the base.

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

**Good to know**

- **A mixed pair gets the stricter rule.** Where one parameter carries `[Unrelated]` and the other
  `[Distinct]`, `[Unrelated]` applies.
- **Only marked parameters form a pair.** A parameter that carries neither attribute is in no pair.
  One parameter alone with the attribute constrains nothing.
- **`Fault<T1, T2>` and `Fault<T1, T2, T3>` declare theirs `[Distinct]`.** With one type twice, every
  conversion and `Of` would be ambiguous.

## Pass a constrained parameter on

When you pass a type parameter of your own as the argument, it must carry the attribute too, or have
C# constraints that prove it (see the table below). Otherwise the requirement would end at the first
parameter that forwards it:

```csharp
public abstract class Leaky<TEvent> : Aggregate<TEvent, AccountId>;               // CMTK0010: TEvent does not carry [Abstract]

public abstract class Sound<[Abstract] TEvent> : Aggregate<TEvent, AccountId>;    // the requirement follows the chain

public sealed class Sealed<T> : Aggregate<AccountEvent, T> where T : struct;      // struct proves [Sealed], and [Concrete]
```

A method that implements an interface's method, or overrides a base class's, carries the constraint
of the method it implements. That holds whether or not it repeats the attribute, so a call through
the class is held to what the interface declared. Repeat the attribute anyway, so the declaration
says so.

### When C# constraints are enough

A parameter needs no attribute when its C# constraints already rule out every argument that would
break it:

| Attribute | Proved by |
|---|---|
| `[Abstract]` | nothing but `[Abstract]`: no C# constraint excludes a concrete type |
| `[Sealed]` | `struct` or `unmanaged`, `[ReadOnlyStruct]` or `[FlagsEnum]`, or a bound `where T : U` with `U` proving `[Sealed]`, since a sealed type has no other subtype |
| `[Record]` | a bound that is a record, `where T : AccountEvent`, or a `U` proving `[Record]`, since only records derive from records |
| `[Concrete]` | `new()`, or anything that proves `[Sealed]`, `[Sealed]` itself included, since a sealed type is never abstract |
| `[ValueObject]`, `[ClosedSet]` | a bound that is or implements the marker interface, `where T : struct, IValue<Guid>`, or a `U` proving it. For `[ValueObject]`, the bound counts only beside `struct`, `unmanaged`, `new()` or a class bound, since an interface meets `IValue<Guid>` and `class` alike |
| `[Interface]`, `[Unsealed]`, `[FlagsEnum]`, `[ReadOnlyStruct]` | nothing but the attribute: an interface bound admits its implementers, `class` a sealed class, `Enum` a plain enum, `struct` a mutable one |

A pair of parameters is proved apart by `[Distinct]` when:

- **Both parameters carry `[Distinct]` or `[Unrelated]`** in one generic, whose closed uses are
  checked in turn.
- **One is `struct` or `unmanaged` and the other `class`**, or a class bound other than `Enum`, which
  every enum meets.
- **Both have class bounds**, and neither derives from the other.
- **Against a type,** the type breaks the parameter's C# constraints.

Two kinds of bound need care:

- **A bound built from a type parameter** is met by what the parameter is at each use.
  `where T : IEquatable<T>` admits `int`. Two classes built from one, `Node<TA>`, are compared by
  their definitions.
- **A type built from a parameter**, `List<T>` or `T[]`, is held as the parameter is. It is proved
  apart by another definition, `List<T>` against `HashSet<int>`. Or it is proved apart by an argument
  that no use can make the other's, `List<T>` against `List<int>` where `T : class`.

A pair is proved apart by `[Unrelated]` when:

- **Both parameters carry `[Unrelated]`** in one generic. So a pair of one `[Unrelated]` and one
  `[Distinct]`, held to `[Unrelated]` at its closed uses, forwards only `[Distinct]`.
- **One is `struct` or `unmanaged` and the other a class bound, or a class,** other than `object`,
  `ValueType` and `Enum`.
- **Both have class bounds**, and neither derives from the other. `Node<TA>` and `Node<TB>` are one
  chain.
- **For types built from a parameter:** a struct against a class that no value type boxes to, two
  structs that are never one, or two classes of different chains. Two arrays, two delegates or an
  interface never prove it, since variance or an implementer relates them.

`class` alone proves nothing for `[Unrelated]`: `object` satisfies it, and `object` is related to
everything.

**Good to know**

- **The open generic is never reported.** Inside its own body, and as an unbound
  `typeof(Aggregate<,>)`, it is left alone.
- **The guarantee is the build.** Reflection, `MakeGenericType`, is out of reach, and the attributes
  carry no run-time check.

## Require a parameterless constructor

Some types are created by a host through reflection: an endpoint, a module, an entity
configuration, a job. Such a type needs a parameterless constructor of a given accessibility. C#
checks that only through `new()`, which demands a public one, and only where a type parameter asks
for it.

Put `[RequireParameterlessConstructor]` on the interface or base class once. A concrete type that
lacks the constructor, directly or through any chain of base classes and interfaces, then fails the
build (CMTK0011). The code fix writes the constructor for you.

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

**Good to know**

- **A record's copy constructor doesn't count.**
- **The marked interface can live in another package.** The attribute is read from metadata, so a
  host's rule reaches every implementation in every project.
- **It's a check, not a generator.** An aspect that adds the constructor would run only in a project
  that compiles with Metalama, and the error has to appear in one that doesn't.

## Why both rules are errors

The code is wrong whatever surrounds it. A type argument that breaks what its parameter declares is
the type the host's reflection-based construction then fails on, at run time, where the build could
have told you.

Both rules arrived in 2.0.0, a major version. Only a major version adds a rule at Error. The ids are
public contract and never change. The severity follows `.editorconfig`, as every rule's does
(`dotnet_diagnostic.CMTK0010.severity = warning`).
