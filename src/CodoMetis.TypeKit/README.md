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

Option<decimal> discount =                                       // a later from sees the earlier ones
    from c in customer
    from rate in discounts.GetValueOrNone(c.Tier)                // TryGetValue as an option
    select rate;
```

Create one with `Option.Some(value)` and `Option.None()`, or lift a nullable with `ToOption()`.
`GetValueOrNone(key)` looks a key up in a dictionary, and `FirstOrNone()` and `LastOrNone()` take
the end of a sequence, with or without a predicate.
`Option.None()` takes its type from where it goes (`return Option.None();`, a conditional beside
`Some`); where nothing supplies one, as with `var`, write `Option.None<T>()`.
`Some(null)` throws, so an option that reports a value always has one. A `default(Option<T>)` is
`None`. `Or(fallback)` and `OrDefault()` unwrap with a fallback, `OrNull()` unwraps into a nullable,
`int?` for an `Option<int>` and `string?` for an `Option<string>`, and `ToResult(error)` turns
absence into an error. `ToString()` never
prints the content, so an option is safe to log; the debugger shows it.

## Result&lt;T, TError&gt; and Result&lt;TError&gt;

The outcome of an operation: a value or an error of your own type, or for a command that produces
no value, a success or an error. There is no `.Value` and no `.Error`.

```csharp
public enum OrderFault { Empty, CustomerUnknown, Unpaid, Closed }

public Result<Order, OrderFault> Place(CustomerId customer, IReadOnlyList<Line> lines)
{
    if (lines.Count == 0) return Result.Error(OrderFault.Empty);
    if (!customers.Exists(customer)) return Result.Error(OrderFault.CustomerUnknown);

    return new Order(customer, lines);   // a bare value is a success
}

public Result<OrderFault> Cancel(OrderId id) =>
    orders.Remove(id) ? Result.Success() : OrderFault.CustomerUnknown;
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

if (service.Cancel(id)) { /* a Result<TError> converts to bool, true for a success */ }

Result<Invoice, OrderFault> invoiced = placed.Bind(order => billing.Invoice(order));
Result<OrderFault> confirmed = placed.Bind(order => mailer.Confirm(order));   // a command after a query
Result<Order, ApiFault> forApi = placed.MapError(ApiFault.From);             // across layers
Option<Order> maybe = placed.ToOption();
```

Only `Result<TError>` converts to `bool`. A valued result would say whether the operation succeeded
where a reader expects its value: `if (await users.IsEmailTakenAsync(email))` over a
`Result<bool, DbFault>` would take the branch for a success holding `false`. Match a valued result,
or compare its `State`.

`Map`, `Bind`, `Tap` and `TapAsync` run only on success and carry an error through unchanged;
`MapError`, `TapError` and `TapErrorAsync` run only on an error, and `Ensure(predicate, error)` turns a success whose
value breaks a rule into that error. A lambda that returns a bare value on one branch and
`Result.Error(...)` on the other needs its type argument, `placed.Bind<Invoice>(o => o.IsPaid ?
invoices.Of(o) : Result.Error(OrderFault.Unpaid))`, because C# infers a lambda's return type from its
body alone; `placed.Ensure(o => o.IsPaid, OrderFault.Unpaid).Map(invoices.Of)` says the same without
one. A `default(Result<…>)`, which an array slot or an unassigned field can still produce, is
`ResultState.Uninitialized`, and every member that would pick a branch throws
`InvalidOperationException` on it rather than inventing a `default(TError)`. `ToString()` never
prints the value or the error.

**Pipelines.** Asynchronous steps chain without an `await` each: `Map`, `Bind`, `MapError`, `Tap`,
`TapError` and `Ensure` also continue a `Task<Result<…>>` (`MapAsync`, `BindAsync`, …), so the chain
is awaited once, at its end, and `Match` follows that `await`.

```csharp
Result<OrderFault> charged = await orders.FindAsync(id)         // Task<Result<Order, OrderFault>>
    .EnsureAsync(order => order.IsOpen, OrderFault.Closed)       // a rule, on a pending result
    .MapAsync(order => order.Total)                              // a synchronous step
    .TapAsync(total => log.Charging(total))
    .BindAsync(total => payments.ChargeAsync(total))             // a command: Result<OrderFault> remains
    .TapErrorAsync(fault => log.NotCharged(id, fault));

Result<Quote, OrderFault> quote =                                // query syntax: each step sees the earlier ones
    from item in catalog.Find(sku)
    from price in pricing.For(item, customer)
    select new Quote(item, price);

Result<Line, OrderFault> line = product.Zip(quantity, (p, q) => new Line(p, q));
Result<IReadOnlyList<Sku>, SkuFault> skus = input.Skus.Traverse(Sku.Create);
Result<IReadOnlyList<Sku>, SkuFault> all = parsed.Sequence();
```

`Zip` (two to six results), `Traverse` and `Sequence` stop at the first error, in argument or
sequence order, and `Traverse` calls nothing after it. None of them collects errors.

The asynchronous steps take callbacks that return a `Task`, and wait for them:
`.TapErrorAsync(fault => audit.RecordAsync(fault))` has recorded the fault when the chain's `await`
returns, and what it throws reaches that `await`. A callback that returns a `ValueTask` needs
`.AsTask()`, or an `async` lambda: `TapAsync` and `TapErrorAsync` would bind it to their synchronous
overload and not wait for it, and `MapAsync` would hold the `ValueTask` as the value.

## Not wire types

Neither `Option` nor `Result` serializes. System.Text.Json refuses both, in both directions, with a
`NotSupportedException` that names the type and the alternative:

```csharp
JsonSerializer.Serialize(new OrderDto(Option.Some(customer)));
// NotSupportedException: Option<Customer> is not a wire type. A serialized shape says absent with a
// nullable (Customer?), and ToOption() and OrNull() convert at the boundary. ...
```

Without the refusal the serializer would see no public members, write `{}` for a `Some` and read it
back as `None`, with nothing raised and the missing data as the only evidence; a `Result` would write
its `State` and read back uninitialized. So a request, a response, a stored document or an event
payload says absent with `T?`, and the option lives between them: `dto.Nickname.ToOption()` on the
way in, `nickname.OrNull()` on the way out. A result is matched to a response or a document; it has
no wire shape of its own. The `Option.None()`, `Result.Success(...)` and `Result.Error(...)` markers refuse too, so an endpoint
that returns one fails on its first call rather than answering `{}`. So does a nullable of any of
them, an `Option<T>?` in a PATCH-style shape, whether it holds a value or not.

Nor are they columns: EF Core cannot map an `Option` or a `Result` property, and says so when it
builds the model. An entity says absent with `T?` too.

Three things to know. A converter registered on the `JsonSerializerOptions` takes precedence over
the refusal, so an application that wants `Option<T>` on the wire writes one and registers it there,
on those options only. A property that is absent from a document reaches no converter at all: the
serializer leaves it `default`, a `None` or an uninitialized result. Where absence must be an error
too, mark the property `required` or set `RespectRequiredConstructorParameters` on the options. And a
source-generated `JsonSerializerContext` cannot describe a shape with an `Option<T>?` or `Result<…>?`
property at all: it fails when it builds that shape's contract, before any document is read or
written, with the serializer's own `InvalidOperationException`:
``The converter '' is not compatible with the type 'CodoMetis.TypeKit.Option`1[System.String]'.``
The refusal cannot be its own there, since that contract takes a converter typed for
`Option<string>` alone, which only `MakeGenericType` could build for every `T`. Declare the
property `T?`.

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
| `IValueObject<TSelf, T>` | What every generated value object implements: `Value`, equality. Run-time code recognises a value object by the attribute the generators add beside it, whose type arguments are constrained to this interface, never by name. |
| `IPlainValueObject<TSelf, T>` | A value object with no rules: `From` accepts any `T`. Plain value objects only. |
| `IValueObjectMaterializer<TSelf, T>` | Rebuilds an instance **without validation**, for values the application wrote itself, such as a database column. Implemented explicitly, so it is not on the public surface. |

Which factory to call: `Create` when the caller has to say what to fix, `TryFrom` when "is it
valid" is the whole question, `FromKnownGood` for a literal in source or a value the caller has just
produced, `From` when there are no rules. `OrderId.New()` creates an identifier from a version 7 `Guid`;
it is an extension in the `CodoMetis.TypeKit` namespace, so the calling file imports that.

A validated value object has no `From`, so nothing that looks harmless throws on input. The one
throwing factory is `FromKnownGood`, whose name says the caller vouches for the value and whose
exception names the call site's expression, never the value. And the fault `Create` returns is what
every refusal reports: `FromKnownGood`'s exception carries it, and the generated JSON converter and
parsing name it in their `JsonException` and `FormatException`. A refusal, wherever it happens,
names the type and the rule and never the input, which can be a secret; text the wrapped type cannot
read at all is reported as that, without quoting it. The fault's own text is yours, so keep the input
out of it.

**Generic code over value objects.** The contracts are static abstract, so a method constrained on
them works for every value object, and the generated types implement `IEqualityOperators`,
`IComparisonOperators` and `IMinMaxValue` where the wrapped type allows, so they satisfy generic-math
constraints too. `OrderId.New()` is such a method: one extension for every plain (unvalidated)
`Guid`-wrapping value object, not a member generated per type.

```csharp
static Option<TSelf> Read<TSelf, TFault>(string field)
    where TSelf : IValidatedValue<TSelf, string, TFault>
    where TFault : notnull =>
    TSelf.Create(field).ToOption();

static TId NewId<TId>() where TId : IValueObject<TId, Guid>, IPlainValueObject<TId, Guid> =>
    TId.From(Guid.CreateVersion7());
```

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
- **CMTK0003** (warning): a `Result` or `Option` that a call returns, dropped. `_ = …` says it is
  meant.
- **CMTK0004**: `Materialize`, which rebuilds a value object without its rules, called anywhere but
  the EF Core satellite.
- **CMTK0005** (warning): `new OrderId[n]` and the like, which fill every slot with a default
  instance.
- **CMTK0006** (warning): a property or field of such a type that nothing sets. `required`
  closes it.
- **CMTK0007** (Info, a suggestion in the IDE): `FromKnownGood` given a parameter, which is input as
  far as the code can tell.
- **CMTK0008** (warning): `order.CustomerId.Value == product.Id.Value`, the wrapped values of two
  different value objects compared, which the types exist to prevent.
- **CMTK0009** (warning): a call that hands out a `default` instance when it finds nothing,
  `ids.FirstOrDefault()` or `ProductCode.TryFrom(s).OrDefault()`. `FirstOrNone()`, `GetValueOrNone()`
  and `Or(fallback)` say what happens then.

The rules also run in Razor components (`.razor`, `.cshtml`).

The ids are stable across releases. See
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers) for the
rule table.

## Native AOT

Everything here works in a trimmed or Native AOT application, and the packages are built with the
trim and AOT analyzers on. With source-generated JSON, which Native AOT requires, list the value
objects (or the types that hold them) in your `JsonSerializerContext`, not what they wrap: the
generated converters build the wrapped type's contract themselves, from a converter on your options,
the type's own `[JsonConverter]`, or the serializer's built-in one. Only a value object wrapping a
type of your own that has no converter needs that type in the context, and the serializer's error
names it. An enum-backed value object is the exception: a context's `UseStringEnumConverter` does not
reach an enum the context does not list, so list the enum too if it should be written by name.
`Option` and `Result` refuse JSON there exactly as on the JIT, and `StoredJsonConverterFactory` reads
without the rules.

## Why does this throw?

Some calls throw on purpose, where carrying on would hand back a plausible wrong answer. Each of
these exceptions ends its message with a link to its entry here: what it refuses, why, and what to
call instead.

### Option or Result in JSON

**Thrown:** `NotSupportedException: Option<Customer> is not a wire type…`, or `Result<Order, OrderFault>
is an outcome, not a wire type…`, when System.Text.Json reads or writes an `Option`, a `Result`, a
marker such as `Option.None()`, or a nullable of any of them.

**Why:** without it a `Some` is written as `{}` and read back as `None`, and nothing is raised; see
[Not wire types](#not-wire-types) and the [design](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/plan.md#9-option-and-result).

**Instead:** say absent with `T?` in the shape, and convert at the boundary: `dto.Nickname.ToOption()`
on the way in, `nickname.OrNull()` on the way out. `Match` a result to a response. A source-generated
context refuses an `Option<T>?` property earlier, with the serializer's
``The converter '' is not compatible with the type 'CodoMetis.TypeKit.Option`1[…]'``; declare `T?`
there too.

### A null in an Option or a Result

**Thrown:** `ArgumentNullException` from `Option.Some(null)`, `Result.Success(null)` or
`Result.Error(null)`, from a `Map` or `MapError` whose selector returns null, from `FirstOrNone()`
over a null element, or for a null error given to `ToResult`, `Ensure` or `FirstOrError`.

**Why:** an option that reports a value always has one, and a result's value and error are never
null. `notnull` is not checked at run time, so the factories check it.

**Instead:** `value.ToOption()` turns a null into `None`; a selector that may return null becomes
`Bind(c => c.Nickname.ToOption())`.

### An uninitialized Result

**Thrown:** `InvalidOperationException: This Result was never initialized…` from `Match`, `Map`,
`TryGetValue` and every other member that picks a branch.

**Why:** a `default` result is neither a success nor an error, and taking the error branch would
invent a `default(TError)`. It comes from an array slot, a field nothing set, or a property missing
from a JSON document.

**Instead:** create results through `Result.Success`/`Result.Error` or a conversion. CMTK0005 and
CMTK0006 point at the arrays and fields, and `required` closes a property; `State` tells an
uninitialized result apart without throwing.

### A value object refused a value

**Thrown:** `JsonException: ProductCode refused the JSON value (TooShort).`, or `FormatException:
ProductCode refused the input (TooShort).` from `Parse` and the type converter.

**Why:** every generated way in applies `Create`, so input the rules refuse never becomes a value
object. The message names the rule and never the input, which can be a secret; see
[Value objects](#value-objects).

**Instead:** where the caller should learn which rule failed, call `ProductCode.Create(text)` and
match the fault; `TryFrom` or `TryParse` where valid or not is the whole question. ASP.NET Core
answers the `JsonException` of a request body with 400.

### FromKnownGood refused a value

**Thrown:** `InvalidOperationException: ProductCode refused request.Code, which the call site
declared known-good (TooShort).`

**Why:** `FromKnownGood` is for a literal or a value the code has just produced, so a refusal there is
a bug at that call site. It names the call site's expression and never the value.

**Instead:** input goes through `Create` or `TryFrom`. CMTK0007 points at a `FromKnownGood` given a
parameter.

### A value object could not read the input

**Thrown:** `FormatException: Quantity could not read the input as Int32.` from `Parse` and the type
converter, or `JsonException: Quantity could not read the JSON value as Int32.` from JSON.

**Why:** the wrapped type's own message quotes the input ("The input string '…' was not in a correct
format."), so it is replaced, and not kept as the inner exception either; see the
[design](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/plan.md#8-decisions),
decision 27. A JSON error still carries its path.

**Instead:** `TryParse` for text that may not parse. Text that is yours to log, log before parsing.

### A null given to a value object

**Thrown:** `ArgumentNullException: ProductName wraps no null.` from `From`, or `JsonException:
ProductName cannot be read from a JSON null.` from JSON.

**Why:** a value object is a value; its `Value` promises never to be null, as an `Option` never holds
one. A string-backed value object would otherwise wrap the null it was given.

**Instead:** where the value can be absent, declare the property or parameter as `ProductName?`.
JSON then reads a `null` as `null`, and the value object is never asked to hold it.

### Materialize and CMTK0004

Not a run-time exception but a build error: `Materialize` rebuilds a value object without `Create`,
for a value the application wrote itself, and anywhere but the EF Core satellite it is
[CMTK0004](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0004).
Use `Create` or `FromKnownGood` instead, and `StoredJsonConverterFactory` for JSON the application
stored.

## Where things are

`Option`, `Result`, `OrderId.New()` and `[RequireCustomInitialization]` live in `CodoMetis.TypeKit`;
every value-object contract in `CodoMetis.TypeKit.ValueObjects`. What only the generated code and the
satellites call is in `CodoMetis.TypeKit.CompilerServices`, hidden from IntelliSense. Targets .NET 10.
Source and issues: [github.com/CaffeinatedCoder/CodoMetis.TypeKit](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit).
