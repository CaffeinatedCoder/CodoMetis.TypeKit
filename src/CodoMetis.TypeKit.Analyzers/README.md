# CodoMetis.TypeKit.Analyzers

The Roslyn analyzers for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit).
You do not reference this package yourself: `CodoMetis.TypeKit` depends on it, so every project
that can see `Option`, `Result` or the value-object contracts gets the rules, directly or through
another project. Reference it directly only to take a newer analyzer than the base package you are on.

The characteristic failure these rules prevent is code that compiles and does less than it
appears to: an instance that passed no factory, or a value object that was never generated.

## Rules

| Id | Severity | Reports |
|---|---|---|
| CMTK0001 | Error | `default`, `default(T)`, `new T()`, `new()` and `new T { }` of a value object, an `Option`, a `Result`, or a struct marked `[RequireCustomInitialization]`. Also through a type parameter whose constraints make it one of those. A default that is only compared (`id == default`, `id.Equals(default)`, `ThrowIfEqual(id, default)`, `Assert.NotEqual(default, id)`) is a guard and is not reported, nor is one assigned to an `out` parameter of a Try method that returns `bool`. |
| CMTK0002 | Error | A type implements `IValue<T>`, `IValidatedValue<,,>` or `IClosedSet<TSelf>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so nothing is generated for it: no field, no `Value`, no factory, or for a closed set no name and no `Match`. |
| CMTK0003 | Error | A `Result` or `Option` that a call returns, or a collection or a tuple of them that the statement made, dropped. The drops: a statement, awaited or not; a conversion of its `Task` to a plain `Task`, a method group's included; `Wait`; a loop that never reads it; a branch of a conditional, a `switch` expression or a `??` that picked the call. `_` says the drop is meant. |
| CMTK0004 | Error | `Materialize`, which rebuilds a value object without its rules, called or referenced anywhere but the EF Core satellite. |
| CMTK0005 | Warning | An array or span of a value object, `Option`, `Result` or `[RequireCustomInitialization]` struct created with a length, so every slot starts as `default`. A constant length of zero is not reported. |
| CMTK0006 | Warning | A field or auto-property of such a type in a class that no initializer, `required` or constructor sets, or a `required` one that a `[SetsRequiredMembers]` constructor does not set. |
| CMTK0007 | Info | `FromKnownGood` given a value that comes straight from a parameter. |
| CMTK0008 | Warning | The wrapped values of two different value objects compared, `order.CustomerId.Value == product.Id.Value`, or the two value objects themselves through `Equals(object)`. |
| CMTK0009 | Warning | A call that returns the `default` of such a type when it finds nothing: `FirstOrDefault()`, `GetValueOrDefault()`, `Option<T>.OrDefault()` and their kind. |
| CMTK0010 | Error | A type argument that breaks a constraint its parameter declares by attribute, `[Abstract]`, `[Sealed]`, `[Record]` or `[Concrete]`, at any closed use: a base type, a member, a parameter, a local, a `typeof`, an attribute, a call or a method group, written or inferred, and a member of a generic extension block, whose arguments the receiver gives. A type parameter forwarded without the attribute is a breach too, and a method implementing an interface's method, or overriding one, carries the constraint of what it implements, a static one and one a base class declares included. |
| CMTK0011 | Error | A concrete type that derives from or implements a type marked `[RequireParameterlessConstructor]`, directly or through another, without a parameterless constructor of the accessibility it asks for. The code fix writes or changes the constructor. |
| CMTK0012 | Error | A closed set's `Match` given an argument by position, which binds to whichever member is declared at that place, so reordering the members swaps the branches. The code fix names every argument. |

The ids are public contract and never change meaning. The generators' own build errors,
CMTK1000 to CMTK1022, come from `CodoMetis.TypeKit.Generators` and are listed in its README.

Every rule also runs in `.razor` and `.cshtml` files (see [Generated code](#generated-code-and-razor)).

## CMTK0001

A `default` value object wraps `default(T)` and passed no rule. A `default` result is neither a
success nor an error, and every branching member throws on it.

```csharp
OrderId id = default;                       // CMTK0001: create it with OrderId.From
var code = new ProductCode();               // CMTK0001: create it with Create, TryFrom or FromKnownGood
Result<Order, OrderFault> pending = new();  // CMTK0001: use Result.Success(value) or Result.Error(error)

T Empty<T>() where T : struct, IValue<Guid> => default;   // CMTK0001, through the constraint
```

```csharp
OrderId id = OrderId.New();
var code = ProductCode.FromKnownGood("ABC");
Result<Order, OrderFault> pending = Result.Error(OrderFault.Empty);
```

Code inside the type itself is exempt, since its factories have to construct it. A `Nullable<T>`
of a value object is a null, not an instance, and is not reported. What the rule cannot see, such
as an array created with a length or an unassigned field of a class, is why an uninitialized
`Result` throws instead of reporting an error it never had.

A default that is only compared is a guard against exactly those, or an assertion that one is not
there, and is not reported:

```csharp
if (id == default) throw new ArgumentException("An order id is required.", nameof(id));
if (option != default) { /* … */ }
if (id.Equals(default(OrderId)) || EqualityComparer<OrderId>.Default.Equals(id, default)) { /* … */ }
ArgumentOutOfRangeException.ThrowIfEqual(id, default);
Assert.NotEqual(default, id);
id.ShouldNotBe(default);
```

That is an operand of `==` or `!=`, or an argument of a call whose name says it compares: one of its
words is `Equal`, `Equals` or `Compare`, or its last word is `Be`. That takes in `Equals`,
`ReferenceEquals`, `ThrowIfEqual`, `ThrowIfNotEqual`, `CompareTo`, a comparer's `Compare`, and the
assertions `Equal` and `NotEqual` (xUnit), `AreEqual` and `AreNotEqual` (MSTest), `EqualTo` (NUnit),
and `ShouldBe` and `ShouldNotBe` (Shouldly). The `default` has to be the argument itself. Anything
else still reports: `Assert.Contains(default, ids)`, `WithEquality(default)`, `Assert.Equal(Wrap(default), id)`,
and the second `default` of `id == default ? default : id`.

A `default` assigned to an `out` parameter of a method that returns `bool` is the Try pattern, as
`int.TryParse` writes it, and is not reported either: the caller reads the parameter only when the
method returned `true`.

```csharp
public bool TryFind(string key, out OrderId id)
{
    if (_ids.TryGetValue(key, out var found)) { id = found; return true; }

    id = default;
    return false;
}
```

The rule does not follow which value the method returns. It leaves alone `default` and `default(T)`
assigned whole, or through a conditional or a switch arm (`id = found ? OrderId.From(g) : default;`),
to an `out` parameter of a method, local function or lambda that returns `bool`. An `out` parameter
of a method that returns anything else, a `ref` parameter, `new()`, and a default kept in a local or
a field first still report.

To mark your own struct, whose `default` is not a valid instance:

```csharp
[RequireCustomInitialization("Use Money.Of(amount, currency)")]
public readonly record struct Money { /* … */ }
```

The message you give is the one the rule reports, as `Option` and `Result` name their own factories.

A struct made of value objects, such as `readonly record struct OrderLine(OrderId Order, Quantity Quantity)`,
is not one itself, and its `default` holds default value objects that no rule sees: the rules do not
look inside another struct. Mark it with `[RequireCustomInitialization]`, and CMTK0001, CMTK0005,
CMTK0006 and CMTK0009 then treat it as they treat a value object.

## CMTK0002

The generators apply through a transitive project fabric rather than an attribute on the interfaces,
which keeps the base package free of Metalama. The price is that a project that sees the interfaces
but not the generators compiles a value object with nothing in it, silently. This rule is that error.
A closed set fails to compile there anyway, on the interface members nobody implemented; the rule
says why.

```csharp
public readonly partial record struct OrderId : IValue<Guid>;   // CMTK0002 without CodoMetis.TypeKit.Generators
```

Reference `CodoMetis.TypeKit.Generators` in the project that declares value objects. Projects that
merely reference that one inherit it.

## CMTK0003

A dropped `Result` is an error nobody sees, and a dropped `Option` an absence nobody handles.

```csharp
Email.Create(input);                         // CMTK0003: validated, and the verdict forgotten
await orders.CancelAsync(id);                // CMTK0003: it may have failed
_ = cache.Remove(key);                       // an explicit discard is silent
```

A `Task<Result<…>>` converts implicitly to `Task`, which drops the result as silently, and is
reported too:

```csharp
Task Cancel(Guid id) => orders.CancelAsync(id);           // CMTK0003: the Result is gone
Func<Task> cancel = () => orders.CancelAsync(id);         // CMTK0003
async Task CancelQuietly(Guid id) => _ = await orders.CancelAsync(id);   // an explicit discard is silent
```

The conversion of a call's task is what is reported, in a `return`, an expression body, a lambda or
an argument (`Task.WhenAny(orders.CancelAsync(id), timeout)`), and so is a method group converted to a
delegate that returns `Task`, which drops the result on every call:

```csharp
Func<Guid, Task> cancel = orders.CancelAsync;             // CMTK0003
bus.Subscribe<Guid>(orders.CancelAsync);                  // CMTK0003: the parameter is a Func<T, Task>
```

A task you still hold is not dropped, awaited as it is or through `ConfigureAwait` or `WaitAsync`,
which hand on the task they are called on; through them, a call's task is still that call's
(`await orders.CancelAsync(id).ConfigureAwait(false);` is reported, naming `CancelAsync`). An
explicit cast, to `Task` or to the delegate type, says the drop is intended. A `ValueTask<T>` has no conversion to `ValueTask`.

A collection of results is dropped as silently as one result:

```csharp
await Task.WhenAll(ids.Select(orders.CancelAsync));                 // CMTK0003: a Result[] nobody reads
await Task.WhenAll(orders.CancelAsync(first), orders.CancelAsync(second));   // CMTK0003
ids.Select(orders.Cancel).ToList();                                 // CMTK0003: a list of them, forgotten

var cancelFirst = orders.CancelAsync(first);
var cancelSecond = orders.CancelAsync(second);
await Task.WhenAll(cancelFirst, cancelSecond);                      // silent: both tasks still hold their results
```

A collection is an array, or a type that is or implements `ICollection<T>` or `IReadOnlyCollection<T>`
(`List<T>`, `ImmutableArray<T>`, `IReadOnlyList<T>`), of results, of tasks of them, or of key-value
pairs whose value is one (`Dictionary<Guid, Result<…>>`, `ids.ToDictionary(id => id, orders.Cancel)`);
a tuple that holds one, `(Result<…>, int)`, is held to the same rule. It is reported
when the statement made the results in it: a call in the statement returns a result or a task of one,
a method group of such a method is passed on, or the call that returns the collection was given no
result and made them itself, as `orders.CancelAll(ids);` does. A call given results that something
still holds only passes them on and is silent: `Task.WhenAll` over tasks in variables, fields or
parameters, or `pending.ToList()`. A lazy `IEnumerable<T>`, such as a `Select` nobody enumerates, has
made nothing yet; CA1806 reports a LINQ result that is dropped. A lazy sequence of results that a call
returns without being given any, `orders.CancelEach(ids)`, makes them as it is enumerated, so
`orders.CancelEach(ids).ToList();` is reported, and so is `await orders.CancelEachAsync(ids).ToListAsync();`.

Waiting for a task drops its result as surely, and so does a loop that never reads what it walks:

```csharp
orders.CancelAsync(id).Wait();                                   // CMTK0003: blocks until the Result exists, then forgets it
Task.WaitAll(ids.Select(orders.CancelAsync));                    // CMTK0003: the tasks become plain ones
await foreach (var outcome in orders.CancelEachAsync(ids)) { }   // CMTK0003: every Result, read by nobody
await foreach (var _ in orders.CancelEachAsync(ids)) { }         // a variable named _ says the drop is meant
```

`Wait` is reported on a task the expression made, whether the statement drops the `bool` a timeout
overload returns or an `if` reads it. `Task.WaitAll` and `Task.WaitAny` take plain tasks, so tasks of
results the statement made are converted and their results dropped, as one task's conversion is; an
explicit cast to the plain tasks' type says that drop is intended. A
`foreach` or `await foreach` is reported when its header made the results, by the rule above, and
its body never reads the iteration variable. Tasks and collections that something still holds keep
their results and are silent.

Out of reach: a lazy sequence of results drained by a call that reads none of them,
`orders.CancelEach(ids).Count();`. The call is given the sequence, and nothing in its signature tells
a count from a read; enumerate it with a loop that reads each result, or discard the count with `_ =`.

`Tap`, `TapAsync`, `TapError` and `TapErrorAsync` return their receiver unchanged, and so do the
`Task` continuations `TapAsync` and `TapErrorAsync`, so `result.Tap(log);` and `await pending.TapAsync(log);`
on a variable, parameter or field are silent. `Find(id).Tap(log);` drops the result and is reported.

## CMTK0004

`IValueObjectMaterializer<,>.Materialize` and `ValueObjectConverter<,>.Materialize` rebuild a value
object without `Create`, for rows the application stored itself. Anywhere else they let input past
the rules. The EF Core satellite's own call is compiled into the satellite, and the compiled model EF
generates in your project is generated code, which the rule does not report. A test that means it
suppresses the rule where it calls it.

## CMTK0005

```csharp
var ids = new OrderId[count];                // CMTK0005: count default instances
OrderId[] kept = [.. rows.Select(r => OrderId.From(r.Id))];   // built from values
```

`stackalloc`, `GC.AllocateUninitializedArray`, `GC.AllocateArray` and `Array.Resize` are reported
too; `Array.Resize` adds default slots whenever the new size is larger, which the rule cannot tell.
A constant length or size of zero creates no slot, as `new OrderId[0]` does not, and is not reported.
A warning, since filling such an array in a loop straight after is correct and indistinguishable.

## CMTK0006

The CS8618 that nullable analysis does not give structs:

```csharp
public sealed class Order
{
    public OrderId Id { get; set; }            // CMTK0006: new Order { } and JSON without "id" leave it default
    public required CustomerId Customer { get; set; }   // required: silent
}
```

A parameterless constructor that is not public is exempt, since EF Core and the serializers
materialize through it and then set the properties. `required` works with EF Core's compiled model
and precompiled queries, and makes System.Text.Json refuse a document without the property. A
Blazor component's `[Parameter]` of a value-object type is reported too, and `required` works there.

The message names `required` only where it compiles: not on a get-only property or a `readonly`
field (CS9034), nor on a member, or a setter, less visible than the class (CS9032). There it says to
initialize the member or assign it in every constructor.

A `required` member is set by every object initializer, except behind a constructor marked
`[SetsRequiredMembers]`, which promises to set it instead:

```csharp
public sealed class Order
{
    [SetsRequiredMembers] public Order() { }   // CMTK0006 on Id: the constructor promises, and does not
    public required OrderId Id { get; init; }
}
```

## CMTK0007

`FromKnownGood` throws on a value that breaks the rules: right for a constant, wrong for input, where a
request that fails validation becomes an exception. The rule reports a value that comes straight from
a parameter of the enclosing method or lambda (`input`, `request.Email`, `args[0]`), through `!` and
parentheses, passed by position or by name, and called as `Email.FromKnownGood(…)` or, under
`using static`, as `FromKnownGood(…)`. A value the code produced itself is not reported, nor is a
parameter copied into a local first. Info, shown as a suggestion in the IDE, since a test theory's
parameters are reported too.

## CMTK0008

`OrderId == CustomerId` does not compile, which is what the types are for. Unwrapping both sides
compiles, and brings back the bug they prevent:

```csharp
orders.Where(o => o.CustomerId.Value == product.Id.Value);  // CMTK0008: a customer id against a product id
orders.Where(o => o.CustomerId == customer.Id);             // what was meant
```

`==`, `!=`, the ordering operators, `a.Value.Equals(b.Value)` and `CompareTo` are reported, a
comparison argument after the value included (`a.Value.Equals(b.Value, StringComparison.Ordinal)`),
and so are the forms that take both values: `string.Equals`, `string.Compare`, `string.CompareOrdinal`,
`object.Equals` and `EqualityComparer<T>.Default.Equals`. Analyzers such as Meziantou's MA0006
rewrite `==` into those. The value is read through `.Value`, `?.Value`, `GetValue()` or `ValueOrNull()`.

A join compares its two keys. `on o.Id equals c.Id`, an order id against a customer id, does not
compile, and unwrapped it does:

```csharp
var wrong = from o in orders join c in customers on o.Id.Value equals c.Id.Value select o;  // CMTK0008: an order id joined to a customer id
var meant = from o in orders join c in customers on o.CustomerId equals c.Id select o;      // what was meant
orders.Join(customers, o => o.Id.Value, c => c.Id.Value, (o, c) => o);                     // CMTK0008
```

Query syntax is reported, with or without `into`, and so are `Join`, `GroupJoin`, `LeftJoin` and
`RightJoin` of `Enumerable`, `Queryable` and `AsyncEnumerable` whose two key selectors each return
such a value. A composite key, `new { … }` or a tuple, is not looked into.

Two value objects of different types compared without unwrapping compile through `Equals(object)`
and are never equal, the same bug:

```csharp
if (order.Id.Equals(customerId)) { /* never */ }            // CMTK0008
if (Equals(order.Id, customerId)) { /* never */ }           // CMTK0008
```

Two of one type are not reported, nor a value object or its value against a raw value, nor an
explicit cast, which says the conversion is deliberate.

## CMTK0009

CMTK0001 sees `default` written out. These calls write it for you when they find nothing, and hand
back an instance no factory made:

```csharp
var code = ProductCode.TryFrom(input).OrDefault();   // CMTK0009: a ProductCode that never passed Create
var first = ids.FirstOrDefault();                    // CMTK0009: an OrderId nobody made, for an empty list
```

| Call | Instead |
|---|---|
| `Enumerable.FirstOrDefault`, `LastOrDefault`, and `ImmutableArray`'s own | `FirstOrNone`, `LastOrNone`, which return an `Option`, or the overload that takes a default value |
| `Enumerable.SingleOrDefault`, and `ImmutableArray`'s own | `SingleOrNone`, which returns an `Option` and still throws for more than one, or the overload that takes a default value |
| `Enumerable.DefaultIfEmpty`, and `ImmutableArray`'s own | the overload that takes a default value |
| `Enumerable.ElementAtOrDefault`, and `ImmutableArray`'s own | `ElementAtOrNone(index)` |
| `Queryable.FirstOrDefault`, `SingleOrDefault`, `ElementAtOrDefault` | `FirstOrNone`, `SingleOrNone`, `ElementAtOrNone`, which ask the query for one element (two for `SingleOrNone`), or the nullable projection below |
| `Queryable.LastOrDefault`, `DefaultIfEmpty` | select a nullable first, `Select(x => (OrderId?)x).LastOrDefault()`, which a query provider translates |
| EF Core's `FirstOrDefaultAsync`, `SingleOrDefaultAsync` | `FirstOrNoneAsync`, `SingleOrNoneAsync` of `CodoMetis.TypeKit.EntityFrameworkCore`, named where the project references it, or the nullable projection |
| EF Core's `LastOrDefaultAsync`, `ElementAtOrDefaultAsync` | the same nullable projection, `Select(x => (OrderId?)x).LastOrDefaultAsync()`; EF Core has no overload that takes a default value |
| `FirstOrDefaultAsync`, `LastOrDefaultAsync`, `SingleOrDefaultAsync`, `DefaultIfEmpty` on an `IAsyncEnumerable<T>` | the overload that takes a default value |
| `ElementAtOrDefaultAsync` on an `IAsyncEnumerable<T>` | the nullable projection |
| `Find`, `FindLast` of a `List<T>`, an array (`Array.Find`) or an `ImmutableList<T>` | `FirstOrNone(predicate)`, `LastOrNone(predicate)` |
| `Nullable<T>.GetValueOrDefault()` | `GetValueOrDefault(fallback)`, or check `HasValue` |
| `dictionary.GetValueOrDefault(key)`, an immutable dictionary's included | `GetValueOrNone(key)`, which returns an `Option`, or pass a default value |
| `Activator.CreateInstance<T>()`, `Activator.CreateInstance(typeof(T))`, `RuntimeHelpers.GetUninitializedObject(typeof(T))` | one of the type's factories |
| `Option<T>.OrDefault()` | `Or(fallback)` or `Match`, which say what `None` becomes |

The element type is judged as CMTK0001 judges a `default`: a value object, `Option`, `Result`,
`[RequireCustomInitialization]` struct, or a type parameter constrained to be one. For the
asynchronous forms, it is the type the task returns. An overload given a default value, and an
element type whose default is null or an ordinary value, are not reported. EF Core's forms are
recognised in any project that references EF Core. A warning, since code that checks the sequence is not empty first is correct and the rule cannot tell.

## CMTK0010

C# has `where T : class`, `struct`, `new()` and a base type, but no `where T : abstract`, `sealed`
or `record`. A generic whose argument must be an abstract record, the command family of an
aggregate, gets no help from the compiler, and the closed type that breaks it compiles. Four
attributes on a type parameter say what C# cannot, and this rule reports the use that breaks it,
at the argument where one is written, and where the arguments are inferred at the call, or at the
method group that takes them from the delegate it converts to (`Action<PlaceOrder> run = Runner.Run;`):

```csharp
public abstract class Decider<[Abstract] TCommand, [Record] TEvent> { }

public sealed record PlaceOrder : Command;

public sealed class OrderDecider : Decider<PlaceOrder, OrderPlaced> { }   // CMTK0010: PlaceOrder is not abstract

public abstract class Leaky<TCommand> : Decider<TCommand, OrderPlaced> { }           // CMTK0010: TCommand does not carry [Abstract]
public abstract class Sound<[Abstract] TCommand> : Decider<TCommand, OrderPlaced> { }
```

| Attribute | Accepts | A forwarding type parameter satisfies it when |
|---|---|---|
| `[Abstract]` | an abstract class or record; not an interface | it carries `[Abstract]` |
| `[Sealed]` | a sealed class or record, a struct, an enum | it carries `[Sealed]` or is constrained to `struct` |
| `[Record]` | a record class or record struct | it carries `[Record]` |
| `[Concrete]` | a class, record or struct that is not abstract, static or an interface; no constructor is demanded, unlike `new()` | it carries `[Concrete]` or is constrained to `struct` or `new()` |

A type parameter given as the argument must carry the attribute itself, or the requirement would end
at the first forwarding parameter and `Sound<PlaceOrder>` would never be reported. A generic
attribute, `[Handles<PlaceOrder>]`, is a use like any other. So is a member of a C# 14 extension
block with a constrained type parameter, `extension<[Concrete] T>(T value)`: its argument comes from
the receiver, so `handler.Go()`, `handler.Count`, `Handler.Make()`, a method group and an operator are
reported at the use, and where the arguments are written, the block's come first
(`handler.Take<Handler, PlaceOrder>(order)`). A method that implements an interface's method carries
its constraint, a static method implementing a `static abstract` one included, and so does a base
class's method that implements the interface for a derived class, when it is reached through the
derived class. The open generic
inside its own body, an unbound `typeof(Decider<,>)`, a `nameof(Decider<PlaceOrder, OrderPlaced>)`,
which uses no argument, and a type argument that does not exist, which is the compiler's error, are
never reported. Reflection,
`MakeGenericType`, is out of reach, and the attributes carry no run-time check.

## CMTK0011

A type the host creates by reflection, an endpoint, a module, an entity configuration, needs a
parameterless constructor, and C# demands one only through `new()`, which demands a public one and
only where a type parameter says so. `[RequireParameterlessConstructor]` on the interface or base
class says it once, and the error appears on the type that lacks the constructor, in whichever
project declares it, where the host would otherwise have failed at run time.

```csharp
[RequireParameterlessConstructor]
public interface IEndpoint { void Map(IEndpointRouteBuilder app); }

[RequireParameterlessConstructor(ConstructorAccessibility.Internal)]
public abstract class Module { }

public sealed class OrderEndpoint(ILogger log) : IEndpoint   // CMTK0011: a primary constructor, so no parameterless one
{
    public void Map(IEndpointRouteBuilder app) { }
}

public sealed class Identity : Module;                       // CMTK0011: the implicit constructor is public, Module requires internal
```

A class that declares no constructor has a public parameterless one and satisfies `Public` as it
is. A wrong accessibility is reported at the constructor, and the fix changes it there; a missing
one is reported at the type, and the fix adds it. A record or class with a primary constructor gets
no fix: every other constructor of such a type chains to the primary one, with arguments only its
author knows. The requirement reaches a type through any chain of base classes and interfaces, and
the attribute is read from metadata, so the marked type can live in another package.

## CMTK0012

A closed set's `Match` takes one delegate per member, in the order the members are declared, so
every parameter has the same type. An argument by position binds to whichever member is declared
at that place: reorder two members, and every such call swaps their branches and compiles as
before. A named argument follows its member, and a member renamed or removed fails the call.

```csharp
var label = status.Match(() => "Open", () => "On its way", () => "Cancelled");   // CMTK0012: by position
var named = status.Match(open: () => "Open", shipped: () => "On its way", cancelled: () => "Cancelled");
```

The code fix names each argument after the parameter it binds to now, so the call keeps its
meaning. A parameter is the member's name with its first letter lower-cased, and a keyword is
escaped: a member `New` is `@new:`. An overload the set declares itself, such as
`Match(Func<OrderStatus, string>)`, is its own and is not reported.

## Configuration

Severity follows the usual `.editorconfig` mechanism, for instance
`dotnet_diagnostic.CMTK0001.severity = warning`. No `.editorconfig` section reaches the C# generated
for `.razor` and `.cshtml` files, not `[*.cs]`, not `[*.razor]`, not even `[*]`. To set a severity
there too, put the line in a `.globalconfig` file, which applies to every file of the project.

The errors are the rules where the code is wrong whatever surrounds it: a `default` written out
(CMTK0001), a value object nothing generates (CMTK0002), validation bypassed (CMTK0004), a type
argument that breaks what its parameter declares (CMTK0010), a constructor a host will look for
and not find (CMTK0011), and branches bound to members by their order (CMTK0012). So is a result
dropped without saying so (CMTK0003): a drop that nothing depends on is correct, and `_` says it in
the code, where every reader sees it. A warning is what gets ignored, so these fail the build. The
others are warnings, or for CMTK0007 Info, because correct code can look the same and has nothing
but a suppression to say so: an array filled in a loop straight after it is created (CMTK0005), a
member set in a second step, such as a component's `OnInitialized` (CMTK0006), a sequence known not
to be empty (CMTK0009), or a comparison of two identities that really are shared (CMTK0008).

Within a major version, an Error never reports more than it did: a new Error rule, or an Error that
reports more, waits for the next major version. A Warning or an Info may learn more forms in a minor
version, such as another method that returns a default instance for CMTK0009. In a build that
treats warnings as errors, such an update can report code that built before, as a newer compiler's
warnings can.

## Generated code and Razor

The C# that the Razor compiler generates for a `.razor` or `.cshtml` file is generated code, which
analyzers skip by default. The rules analyse it, and report what maps back to the markup through
its `#line` directives: `@code { OrderId _id = default; }` is CMTK0001 at that line of the component.
Everything else generated stays unreported, as before: EF Core's compiled model (which calls
`Materialize`), source-generated JSON and regex code, and code under `#line hidden`.

Metalama compiles every project that reaches `CodoMetis.TypeKit.Generators`, and there source
generators, the Razor compiler among them, run after its transformation. An ordinary analyzer sees
only the source, without the component's C#. This package therefore asks Metalama to run the rules
on the transformed code, where it is, through a `MetalamaTransformedCodeAnalyzer` item that arrives
with the package. Nothing needs configuring.

## In the project that declares the value objects

The build runs the rules on the code as Metalama transformed it, where the members the generators
add exist, so every form below is reported in the declaring project too. An IDE analyses as you
type, without weaving. Where the members a value object of the same project gets (`From`, `New`,
`TryFrom`, `FromKnownGood`, `Revalidate`, `Value`) do not exist there, a call to one does not bind.
The rules recognise the forms below by the generated member's name on a value object's type, which
does bind, so they report there too:

- CMTK0001: every form, including a `default` beside an unbound call in a conditional, a switch arm
  or a collection element (`cond ? OrderId.From(g) : default`), whose type is taken from the whole
  expression. A local declared with `var` from a generated member (`var id = OrderId.New(); id = default;`)
  has no type there, and is not seen.
- CMTK0003: `X.TryFrom(…)` and `x.Revalidate()` dropped as a statement, or as the whole body of a
  method, local function, accessor or lambda that returns nothing; a hand-written `X.Create(…)` in
  every form. A call chained onto an unbound one, such as `X.TryFrom(s).Tap(log)` or
  `X.Create(s).Map(c => c.Value)`, does not bind either and is not seen.
- CMTK0006: a constructor that chains with `this(OrderId.New())` is taken to chain to the one
  constructor that takes that many arguments; with none or several, it counts as assigning everything.
- CMTK0007: `X.FromKnownGood(…)`, and `FromKnownGood(…)` under a `using static` of its file, in every
  argument form.
- CMTK0008: `.Value` and its companions read from a receiver whose type is declared: a parameter,
  field, property or typed local. A `var` local initialized from a generated factory, or the factory
  call itself (`OrderId.From(g).Value`), has no type there, and is not seen.
- CMTK0009: `X.TryFrom(…).OrDefault()`.

In every other project the generated members bind like any other, and every form is seen.

The analyzer resolves the types it looks for by symbol, so a type of your own named `IValue<T>` in
another namespace is not mistaken for ours, and a value object that implements a marker through a
derived interface is still seen.
