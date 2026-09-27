# CodoMetis.TypeKit

`Option<T>` and `Result<T, TError>` that cannot be misused, and the contracts for strongly-typed
value objects. This is the base package: it brings no code generator and no Metalama, only the
types, and the analyzers that keep them honest.

```bash
dotnet add package CodoMetis.TypeKit
```

| You want | Reference |
|---|---|
| `Option` and `Result` only | `CodoMetis.TypeKit` |
| Value objects generated from a one-line declaration | `CodoMetis.TypeKit.Generators` as well |
| Value objects as EF Core columns, with `.Value` in queries | `CodoMetis.TypeKit.EntityFrameworkCore` in the data project |
| Value objects in an OpenAPI document | `CodoMetis.TypeKit.AspNetCore` in the host |

## Option&lt;T&gt;

A value that may be absent. There is no `.Value`: the content is reached where absence is handled.

```csharp
using CodoMetis.TypeKit;

Option<Customer> customer = repository.FindByEmail(email);   // Some(...) or None

string greeting = customer.Match(
    c  => $"Welcome back, {c.Name}",
    () => "Welcome");

if (customer.TryGetValue(out var found))
    found.RecordVisit();

Option<string> town = customer
    .Bind(c => c.Address)          // Option<Address>, itself optional
    .Map(a => a.Town)
    .Filter(t => t.Length > 0);

var name = from c in customer where c.IsActive select c.Name;   // query syntax works too
```

Create one with `Option.Some(value)` or `Option.None<T>()`, or lift a nullable with `ToOption()`.
`Some(null)` throws, so an option that reports a value always has one. A `default(Option<T>)` is
`None`. `Coalesce(fallback)`, `OrDefault()` and `OrNull()` unwrap with a fallback. `ToString()` never
prints the content, so an option is safe to log; the debugger shows it.

## Result&lt;T, TError&gt; and Result&lt;TError&gt;

The outcome of an operation: a value or an error of your own type, or for a command that produces
no value, a success or an error. There is no `.Value` and no `.Error`.

```csharp
public enum OrderFault { Empty, CustomerUnknown }

public Result<Order, OrderFault> Place(CustomerId customer, IReadOnlyList<Line> lines)
{
    if (lines.Count == 0) return Result.Error(OrderFault.Empty);
    if (!customers.Exists(customer)) return Result.Error(OrderFault.CustomerUnknown);

    return new Order(customer, lines);   // a bare value is a success
}

public Result<OrderFault> Cancel(OrderId id) =>
    orders.Remove(id) ? Result.Ok() : OrderFault.CustomerUnknown;
```

A bare value converts to a success, and an error goes through `Result.Error(...)`. A bare error
converting as well would be ambiguous in the worst way: in a `Result<long, int>`, `return 5;` would
pick the error, `int` being the closer match. `Result<TError>` has no value to confuse it with, so
there a bare error converts too.

```csharp
var placed = service.Place(customer, lines);

IResult response = placed.Match(
    order => Results.Created($"/orders/{order.Id}", order),
    fault => Results.BadRequest(fault.ToString()));

if (placed.TryGetValue(out var order, out var fault)) { /* order is non-null here */ }

if (service.Cancel(id)) { /* the bool conversion is true for a success */ }

Result<Invoice, OrderFault> invoiced = placed.Bind(order => billing.Invoice(order));
Option<Order> maybe = placed.ToOption();
```

`Map`, `Bind`, `Tap` and `TapAsync` run only on success and carry an error through unchanged. A
`default(Result<…>)`, which an array slot or an unassigned field can still produce, is
`ResultState.Uninitialized`, and every member that would pick a branch throws
`InvalidOperationException` on it rather than inventing a `default(TError)`. `ToString()` never
prints the value or the error.

## Value objects

A value object wraps exactly one value and is equal to another when the wrapped values are equal.
You declare it in one line; [CodoMetis.TypeKit.Generators](https://www.nuget.org/packages/CodoMetis.TypeKit.Generators)
generates the rest at compile time.

```csharp
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

public readonly partial record struct OrderId : IValue<Guid>;

public enum CodeFault { Blank, TooShort, NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);

        var trimmed = value.Trim();
        if (trimmed.Length < 3) return Result.Error(CodeFault.TooShort);
        if (trimmed != trimmed.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);

        return new ProductCode(trimmed);   // the private constructor is generated
    }
}
```

The contracts in this package say what every value object has:

| Contract | Meaning |
|---|---|
| `IValue<T>` | Wraps any `T`. Gets `From(value)`. |
| `IValidatedValue<TSelf, T, TFault>` | You write `Create`, which returns a `Result`. Gets `TryFrom(value)` returning an `Option`, and `FromKnownGood(value)`, which throws and names the caller's expression, never the value. Every generated way in, JSON, parsing and the type converter, applies `Create`. |
| `IValueObject<TSelf, T>` | What every generated value object implements: `Value`, equality. Run-time code recognises a value object by this interface, never by name. |
| `IValueWrapper<TSelf, T>` | `From`, on plain value objects only. |
| `IValueObjectMaterializer<TSelf, T>` | Rebuilds an instance **without validation**, for values the application wrote itself, such as a database column. Implemented explicitly, so it is not on the public surface. |

Which factory to call: `Create` when the caller has to say what to fix, `TryFrom` when "is it
valid" is the whole question, `FromKnownGood` for a literal in source or a value the caller has just
produced, `From` when there are no rules. `OrderId.New()` creates an identifier from a version 7 `Guid`;
it is an extension in the `CodoMetis.TypeKit` namespace, so the calling file imports that.

**Stored JSON.** The generated JSON converter applies `Create`, which is right for input and wrong
for documents the application stored before a rule existed. Register `StoredJsonConverterFactory`
on that store's options, and nowhere near input:

```csharp
var storeOptions = new JsonSerializerOptions { Converters = { new StoredJsonConverterFactory() } };
```

**Your own no-default structs.** `[RequireCustomInitialization("Use Money.Of(...)")]` on a struct
makes the analyzer refuse `default` and `new()` of it, as it does for `Option`, `Result` and every
value object.

## The analyzers

They arrive with this package, so whoever can see the interfaces gets the guard:

- **CMTK0001**: `default(OrderId)`, `new OrderId()`, `new()` and `default` of a value object, an
  `Option`, a `Result` or a `[RequireCustomInitialization]` struct. Such an instance passed no
  factory.
- **CMTK0002**: a type implements `IValue<T>` or `IValidatedValue<,,>` but the project does not
  reference `CodoMetis.TypeKit.Generators`, so nothing would be generated for it.

The ids are stable across releases. See
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers) for the
rule table.

## Where things are

`Option`, `Result` and `IValueObject` live in `CodoMetis.TypeKit`; the value-object contracts in
`CodoMetis.TypeKit.ValueObjects`; the attribute in `CodoMetis.TypeKit.Attributes`. Targets .NET 10.
Source and issues: [github.com/CaffeinatedCoder/CodoMetis.TypeKit](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit).
