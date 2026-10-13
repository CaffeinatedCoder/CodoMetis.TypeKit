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
the end of a sequence, with or without a predicate. On a query, `FirstOrNone` and `FirstOrError` ask
the provider for one element and let it run the predicate (a `WHERE` and a `LIMIT` in SQL), and `LastOrNone` is a
compile error, since a query has no last element until it is ordered. `SingleOrNone()` answers the
only element, `None` for none, and throws for more than one, as `SingleOrDefault` does, since many
is a defect and not absence; `ElementAtOrNone(index)` answers the element at an index, `^1` included.
`Choose(selector)` keeps the values a selector answers `Some` for, in order and without the `None`s, a
filter and a map in one, and `Choose()` the values a sequence of options holds.
An optional input that is validated when present is `Traverse`: `dto.Phone.ToOption().Traverse(Phone.Create)`
is a `Result<Option<Phone>, PhoneFault>`, and `Transpose()` turns an `Option<Result<…>>` inside out,
and a `Result<Option<…>>` back.
`Option.None()` takes its type from where it goes (`return Option.None();`, a conditional beside
`Some`); where nothing supplies one, as with `var`, write `Option.None<T>()`.
`Some(null)` throws, so an option that reports a value always has one. A `default(Option<T>)` is
`None`. `Or(fallback)` and `OrDefault()` unwrap with a fallback, `OrNull()` unwraps into a nullable,
`int?` for an `Option<int>` and `string?` for an `Option<string>`, and `ToResult(error)` turns
absence into an error. `ToString()` never
prints the content, so an option is safe to log; the debugger shows it.

A lookup that is still running is a `Task<Option<T>>`, and continues as a pending result does:

```csharp
Result<Customer, SignInFault> active = await repository.FindByEmailAsync(email)   // Task<Option<Customer>>
    .FilterAsync(c => c.IsActive)
    .TapAsync(c => audit.SeenAsync(c))                                             // waited for
    .ToResultAsync(SignInFault.Unknown);                                           // into a Result pipeline
```

`MapAsync`, `BindAsync`, `FilterAsync`, `TapAsync` and `ToResultAsync` continue a pending option, and
an option has `MapAsync`, `BindAsync` and `TapAsync` over callbacks that return a `Task`. Each checks
its delegates, and `ToResultAsync` its error, before the option is known.

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
Result<OrderFault> settled = service.Cancel(id)                               // a second chance:
    .Recover(fault => fault == OrderFault.Closed ? Result.Success() : fault);  // closed needs no cancelling
Option<Order> maybe = placed.ToOption();
```

Only `Result<TError>` converts to `bool`. A valued result would say whether the operation succeeded
where a reader expects its value: `if (await users.IsEmailTakenAsync(email))` over a
`Result<bool, DbFault>` would take the branch for a success holding `false`. Match a valued result,
or compare its `State`.

`Map`, `Bind`, `Tap` and `TapAsync` run only on success and carry an error through unchanged;
`MapError`, `TapError` and `TapErrorAsync` run only on an error, and `Recover` hands an error the result
its selector returns for it, a success or the error kept. `Ensure(predicate, error)` turns a success whose
value breaks a rule into that error. A lambda that returns a bare value on one branch and
`Result.Error(...)` on the other needs its type argument, `placed.Bind<Invoice>(o => o.IsPaid ?
invoices.Of(o) : Result.Error(OrderFault.Unpaid))`, because C# infers a lambda's return type from its
body alone; `placed.Ensure(o => o.IsPaid, OrderFault.Unpaid).Map(invoices.Of)` says the same without
one. A `default(Result<…>)`, which an array slot or an unassigned field can still produce, is
`ResultState.Uninitialized`, and every member that would pick a branch throws
`InvalidOperationException` on it rather than inventing a `default(TError)`. `ToString()` never
prints the value or the error.

**Pipelines.** Asynchronous steps chain without an `await` each: `Map`, `Bind`, `MapError`, `Recover`,
`Tap`, `TapError` and `Ensure` also continue a `Task<Result<…>>` (`MapAsync`, `BindAsync`, …), so the chain
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
sequence order, and `Traverse` calls nothing after it. None of them collects errors. `TraverseAsync`
awaits one operation at a time, in order, and starts none after an error, or once the cancellation
token it is given is cancelled; operations that may all run at once are `Task.WhenAll` and `Sequence()`.

The asynchronous steps take callbacks that return a `Task`, and wait for them:
`.TapErrorAsync(fault => audit.RecordAsync(fault))` has recorded the fault when the chain's `await`
returns, and what it throws reaches that `await`. A callback that returns a `ValueTask` needs
`.AsTask()`, or an `async` lambda: `TapAsync` and `TapErrorAsync` would bind it to their synchronous
overload and not wait for it, and `MapAsync` would hold the `ValueTask` as the value.

**Exceptions as outcomes.** A call that throws has no way into a `Result` except a `try`/`catch`
written by hand. `Result.Catch` runs the call and answers its value, or the exception as the error.

```csharp
Result<int, Exception> parsed = Result.Catch(() => int.Parse("42"));                                   // Success: 42
Result<int, Exception> refused = Result.Catch(() => int.Parse("x"));                                   // Error: FormatException
Result<int, FormatException> narrowed = Result.Catch<int, FormatException>(() => int.Parse("x"));     // only that type; any other propagates
Result<Exception> copied = Result.Catch(() => File.Copy(source, target));                             // a command: a success, or the exception
Result<byte[], Exception> fetched = await Result.CatchAsync(() => client.GetByteArrayAsync(url));      // waits for the task; its exception is the error
```

`Catch<T, TException>` catches that type only and lets every other exception propagate, so the error
type says what was expected to fail; both type arguments are spelled, since C# infers all or none,
and `Catch<FormatException>(() => { … })` with one is the command form, for a call without a
value: given one that returns a value, which it would discard, it is a compile error naming the
valued form. `CatchAsync` awaits the task, so an exception thrown after its first `await` is the error
too. An `OperationCanceledException` passes through all of them: a cancellation is not an outcome,
and caught it would read as a failure that `Recover` then runs on. A timeout is the exception: one
over a `TimeoutException` is how `HttpClient` reports its `Timeout` elapsing, which is the call
failing, so it is the error, and a narrowed `Catch` names `TaskCanceledException` or
`OperationCanceledException` to catch it alone. A delegate that returns a `Task`, an `async` lambda
among them, given to `Catch` or its command form is a compile error naming `CatchAsync`, since the
task's exception would be thrown after `Catch` had returned. A `ValueTask`, or a task spelled as the
type argument (`Catch<Task<int>, …>`), is refused when called, before the delegate runs; `CatchAsync`
takes a `ValueTask` through `.AsTask()` or an `async` lambda, as above. A delegate that returns
null throws, as `Result.Success(null)` does. `Map`, `Bind` and the rest are unchanged: a selector
that throws propagates. An exception is for the code that called, not for a client: `ToHttpResult()`
does not accept a `Result<T, Exception>`, and `MapError` turns the exception into a fault that it does.

**A fault with a sentence.** A fault is one of a fixed set, an enum's member or a closed set's, so
what varies per occurrence travels beside it: `Fault<TFault>` holds the fault, its `Reason`, and an
optional `Detail`. `Fault.Of(OrderFault.NotAmendable, "The order shipped on 3 May.")` infers the type,
and a bare fault converts to one without a detail. CodoMetis.TypeKit.AspNetCore answers a
`Result<T, Fault<TFault>>` as it answers the fault, with the detail as the problem's `detail`. A
closed set whose members are faults declares `IFaultSet<TSelf>`, a `FaultKind` and an optional
description per member, from which the host derives each status.

## Not wire types

The one wire type here is `Patch<T>`, a merge patch's field (below).

Neither `Option` nor `Result` serializes, nor does `Fault<TFault>`, which is matched to a response as
the result holding it is. System.Text.Json refuses them, in both directions, with a
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

## Written text

Every application that stores what a person typed trims it and refuses a blank. Few refuse a
zero-width space, a Hangul filler or NUL, which `IsNullOrWhiteSpace` lets through and `Trim`
leaves in place, and fewer compose it, so a name written with `ü` and the same name written with
`u` and a combining diaeresis are two rows of a unique index that render alike. Two extensions on
`string` do all of it and answer with a `Result`:

```csharp
var name    = "  Jean\u00A0Paul ".AsText();        // Success: Jean Paul, trimmed, the no-break space folded to an ordinary one
var subject = "subject\u200Bid".AsIdentifier();   // Error: Hidden, a zero-width space
var missing = ((string?)null).AsText();            // Error: Blank
```

Which one to call is decided by who chose the value. `AsText()` is for text a person wrote: it
trims, folds every space separator to an ordinary space, composes to NFC and refuses any character
that renders as nothing, keeping the two zero-width joiners because they spell Persian, Arabic and
Indic scripts and hold an emoji sequence together. `AsIdentifier()` is for a value another system
issued, a subject claim, a session id, a key: it trims and alters nothing else, since an issued
value is compared against what its issuer sends next time, and it refuses the joiners too. A
variation selector is kept after the character it varies and refused anywhere else, and a value of
joiners, selectors and combining marks alone renders no character of its own and is refused by
both. Both
scan by `Rune`, so a character above the BMP is seen for what it is, and an unpaired surrogate is
`Hidden` rather than an exception.

`TextFault` has two members because the caller decides what a blank means: a required field makes
`Blank` an error, an optional one makes it absence. `Hidden` is a refusal in both. A null is
`Blank`, since the extension is the door a bound request property goes through and binding
enforces no non-nullability.

A value object over either guard is one line, and named for the field rather than for the guard,
since `Description` says more than `Text` and two fields of one type can be assigned to each other:

```csharp
public readonly partial record struct Description : IValidatedValue<Description, string, TextFault>
{
    public static Result<Description, TextFault> Create(string value) =>
        value.AsText().Map(text => new Description(text));
}
```

Not refused on purpose: unassigned code points, since the runtime's Unicode tables lag the
standard and a name spelled with a newly assigned character would be refused until the SDK caught
up; and confusable letters across scripts, which is a comparison question and not a rejection.

## Merge patches

A PATCH body in JSON Merge Patch (RFC 7396) says three things about a field: nothing, so it stays;
`null`, so it is cleared; or a value. System.Text.Json cannot tell the first two apart, since it
calls no converter for a property it does not find, so every PATCH endpoint rebuilt the distinction
by hand. `Patch<T>` is that field, and the one wire type here, beside `Option`, which refuses JSON:
an option says whether a value exists, a patch whether a request mentioned it.

```csharp
public sealed record OrderPatch(Patch<string> Name, Patch<string?> Nickname, Patch<int> Quantity);
```

```csharp
var patch = JsonSerializer.Deserialize<OrderPatch>("""{"nickname":null,"quantity":2}""", JsonSerializerOptions.Web)!;
var name = patch.Name;           // Absent, the field was not mentioned
var nickname = patch.Nickname;   // Null, the field was null
var quantity = patch.Quantity;   // Value: 2
```

`default(Patch<T>)` is `Absent`, the one `default` of this package that means something, and the
analyzers leave it alone. Applying one needs no reflection:

```csharp
patch.Name.IfValue(name => order.Name = name);                 // a clear cannot reach a required field
patch.Nickname.IfPresent(nickname => order.Nickname = nickname); // null clears an optional one
order.Quantity = patch.Quantity.Match(
    quantity => quantity,
    () => throw new InvalidOperationException("Quantity cannot be cleared."),
    () => order.Quantity);
```

Reading never refuses a null: the Null state is part of the wire format whatever `T` is, and whether
a clear is accepted is the application's rule, said in `Match`. So a `Patch<T?>` over a struct is
never needed, and a value object inside a patch keeps its own rule: `Patch<Email>` given a value
`Create` refuses is refused as anywhere else, the input not quoted. Where the refusal should be the
endpoint's own fault, the request carries a `Patch<string>` and `Traverse` validates it:
`request.Email.Traverse(Email.Create)` is a `Result<Patch<Email>, EmailFault>`, a clear and an
absence passed through, and `Map` changes a value the same way. Writing an absent patch is
refused, since a converter cannot leave a property out; a property that is written takes
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`, and an absent patch is left out.

Under a source-generated `JsonSerializerContext`, the context never sees `T`, since the patch's
converter hides it: a primitive, a string, a `Guid`, a date or a `Uri` is read with the serializer's
built-in converter and a value object with its own, and any other type, a class or an enum, is
listed in the context, or the serializer refuses with its own message naming it. The OpenAPI document
describes a patch property as `T`'s schema, not required, admitting null
([CodoMetis.TypeKit.AspNetCore](https://www.nuget.org/packages/CodoMetis.TypeKit.AspNetCore)). This is
not ASP.NET Core's JSON Patch (RFC 6902, a list of operations).

## The analyzers

They arrive with this package, so whoever can see the interfaces gets the guard:

- **CMTK0001**: `default(OrderId)`, `new OrderId()`, `new()` and `default` of a value object, an
  `Option`, a `Result` or a `[RequireCustomInitialization]` struct. Such an instance passed no
  factory.
- **CMTK0002**: a type implements `IValue<T>`, `IValidatedValue<,,>` or `IClosedSet<TSelf>` but the
  project does not reference `CodoMetis.TypeKit.Generators`, so nothing would be generated for it.
- **CMTK0003**: a `Result` or `Option` that a call returns, dropped, waited for and forgotten, or
  walked by a loop that never reads it. `_ = …` says it is meant.
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
- **CMTK0010**: a type argument that breaks what its parameter declares with `[Abstract]`, `[Sealed]`,
  `[Record]` or `[Concrete]`, the constraints C# has no `where` for, at any closed use. A method
  implementing an interface's method, or overriding one, carries the constraint of what it implements.
- **CMTK0011**: a type under an interface or base class marked `[RequireParameterlessConstructor]`
  without the parameterless constructor it asks for, which the host would look for by reflection.
  The code fix writes it.
- **CMTK0012**: a closed set's `Match` given an argument by position, where reordering two members
  would swap their branches and compile. The code fix names every argument.

The rules also run in Razor components (`.razor`, `.cshtml`).

The ids are stable across releases. See
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers) for the
rule table.

## Native AOT

This package works in a trimmed or Native AOT application, and the packages are built with the
trim and AOT analyzers on. The one exception is `FirstOrNone`, `FirstOrError`, `SingleOrNone` and
`ElementAtOrNone` on an EF Core query, which compose the query where EF cannot precompile it and throw
there; write EF's operators at the call site and take the option of the list. With source-generated JSON, which Native AOT requires, list the value
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
`Fault<TFault>`, a marker such as `Option.None()`, or a nullable of any of them. The same link ends
the refusal of writing an absent `Patch<T>`, which no document can carry: mark the property
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`.

**Why:** without it a `Some` is written as `{}` and read back as `None`, and nothing is raised; see
[Not wire types](#not-wire-types) and the [design](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/design/plan.md#9-option-and-result).

**Instead:** say absent with `T?` in the shape, and convert at the boundary: `dto.Nickname.ToOption()`
on the way in, `nickname.OrNull()` on the way out. `Match` a result to a response. A source-generated
context refuses an `Option<T>?` property earlier, with the serializer's
``The converter '' is not compatible with the type 'CodoMetis.TypeKit.Option`1[…]'``; declare `T?`
there too.

### A null in an Option or a Result

**Thrown:** `ArgumentNullException` from `Option.Some(null)`, `Result.Success(null)` or
`Result.Error(null)`, from a `Map` or `MapError` whose selector returns null, from `FirstOrNone()`
over a null element, or for a null error given to `ToResult`, `Ensure` or `FirstOrError`. A
`Patch<T>.Map` whose selector returns null throws the same: a clear is the Null state, never a value.
So does `CatchAsync` for a delegate that returns no task: a null cannot be awaited, and it is not an
exception the call threw.

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
[design](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/design/plan.md#8-decisions),
decision 27. A JSON error still carries its path.

**Instead:** `TryParse` for text that may not parse. Text that is yours to log, log before parsing.

### A null given to a value object

**Thrown:** `ArgumentNullException: ProductName wraps no null.` from `From`, or `JsonException:
ProductName cannot be read from a JSON null.` from JSON.

**Why:** a value object is a value; its `Value` promises never to be null, as an `Option` never holds
one. A string-backed value object would otherwise wrap the null it was given.

**Instead:** where the value can be absent, declare the property or parameter as `ProductName?`.
JSON then reads a `null` as `null`, and the value object is never asked to hold it.

### A closed set has no member of that name

`OrderStatus.Parse("nope")` and a JSON `"nope"` for an `OrderStatus` property throw: a closed set's
members are its `public static readonly` fields, and a name that is none of them, compared
ordinally, is refused. The message names the set and never the input. `TryParse` answers `false`
instead, and `All` lists the names there are.

### An instance that is not a member of its set

**Thrown:** `InvalidOperationException: OrderStatus: this instance is not a member of the set, so it
has no name.` from `Name`, `QualifiedName`, the generated `ToString()`, `Match`, the JSON converter
and the EF Core column.

**Why:** a closed set's constructors are private, but the set's own code can still call one outside
a member's declaration, in a static method or a private field. That instance has no name, and
written as JSON or stored in a column it would come back as nothing. A member's initializer or
constructor, or any other static field initializer of the set, that reads a member's name sees the
same thing, since the names are given after every static field is initialized; there the exception
surfaces as a `TypeInitializationException`, and the set cannot be used for the rest of the process.

**Instead:** return one of the members, `OrderStatus.Open`, wherever the set's code produces an
instance, keep data a member needs in its own constructor's arguments, and read a name in a property
or method rather than a static field initializer.

### A task given to Catch

**Thrown:** `ArgumentException: Catch was given a delegate whose value is a task…` from `Catch` when
its value's type is a `Task` or a `ValueTask`, spelled (`Catch<Task<int>, HttpRequestException>`) or
inferred from a delegate that returns a `ValueTask`; and from `CatchAsync` when the task's value is
another task. Nothing has run: the delegate is refused before it is called.

**Why:** the task's exception is thrown after `Catch` has returned, so the answer would be a success
whatever the call does. A delegate that returns a `Task` is refused at compile time; a `ValueTask`
converts to no task type an overload could name without making every `async` lambda ambiguous, and a
spelled type argument chooses its overload itself.

**Instead:** `CatchAsync`, which awaits the task: `await Result.CatchAsync(() => stream.ReadAsync(buffer).AsTask())`,
or an `async` lambda. In `CatchAsync`, await the inner task in the delegate.

### Materialize and CMTK0004

Not a run-time exception but a build error: `Materialize` rebuilds a value object without `Create`,
for a value the application wrote itself, and anywhere but the EF Core satellite it is
[CMTK0004](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0004).
Use `Create` or `FromKnownGood` instead, and `StoredJsonConverterFactory` for JSON the application
stored.

## Where things are

`Option`, `Result`, `OrderId.New()`, `AsText()`, `AsIdentifier()`, `Patch<T>`, `[RequireCustomInitialization]`, `[RequireParameterlessConstructor]` and the type-parameter attributes `[Abstract]`, `[Sealed]`, `[Record]` and `[Concrete]` live in `CodoMetis.TypeKit`;
every value-object contract in `CodoMetis.TypeKit.ValueObjects`, and `IClosedSet<TSelf>` in `CodoMetis.TypeKit.ClosedSets`
(generated by [CodoMetis.TypeKit.Generators](https://www.nuget.org/packages/CodoMetis.TypeKit.Generators), which has the family's README section). What only the generated code and the
satellites call is in `CodoMetis.TypeKit.CompilerServices`, hidden from IntelliSense. Targets .NET 10.
Source and issues: [github.com/CaffeinatedCoder/CodoMetis.TypeKit](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit).
