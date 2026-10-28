# CodoMetis.TypeKit

The base package of TypeKit. It gives you `Option<T>` and `Result<T, TError>` that you can't misuse,
and the interfaces that value objects and closed sets (smart enums) are generated from. It brings no
code generator and no Metalama, only the types and the analyzers that check them.

What it does for you:

- **A value or nothing, a value or an error.** `Option` and `Result` have no `.Value`, so you handle
  absence and failure where you use the content.
- **One model for validation and outcomes.** A value object's `Create` returns a `Result`. Its error
  (TypeKit calls it a fault) is the error the rest of your application handles, the EF Core and
  ASP.NET Core packages included.
- **Merge patches.** `Patch<T>` tells "absent", "null" and "a value" apart in a PATCH body.
- **Text guards.** `AsText()` and `AsIdentifier()` clean and check text a person wrote, or a value
  another system issued.
- **Mistakes caught at build time.** The analyzers arrive with this package. They flag a `default`
  value, an ignored result and skipped validation.

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

A value that may be absent. It has no `.Value`, so you get the content out where you handle absence.

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

### Create one

- **`Option.Some(value)` and `Option.None()`** create one. `ToOption()` lifts a nullable.
- **`Option.None()` takes its type from where it goes**, such as `return Option.None();` or a
  conditional beside `Some`. Where nothing supplies a type, as with `var`, write `Option.None<T>()`.
- **`Some(null)` throws**, so an option that reports a value always has one. A `default(Option<T>)`
  is `None`.

### Find one in a collection

- **`GetValueOrNone(key)`** looks a key up in a dictionary.
- **`FirstOrNone()` and `LastOrNone()`** take the first or last element of a sequence, with or
  without a predicate.
- **`SingleOrNone()`** returns the only element, or `None` for none. It throws for more than one, as
  `SingleOrDefault` does, because many is a defect and not absence.
- **`ElementAtOrNone(index)`** returns the element at an index. On a sequence, `^1` works too.
- **`Choose(selector)`** keeps the values a selector returns `Some` for, in order and without the
  `None`s. It filters and maps in one step, over elements that may be null
  (`nicknames.Choose(n => n.ToOption())`). `Choose()` keeps the values a sequence of options holds.

On a database query, `FirstOrNone`, `FirstOrError`, `SingleOrNone` and `ElementAtOrNone` ask the
provider for only the element they need, and let it run the predicate (a `WHERE` and a `LIMIT` in
SQL). `LastOrNone`, `LastOrError` and an index from the end don't compile there, because a query has
no last element until it is ordered.

Write the predicate as a lambda at the call, which makes it an expression. A `Func<T, bool>` variable
or a method group is no expression. It binds to the sequence's overload, which reads every row and
runs the condition in memory. Hold an `Expression<Func<T, bool>>` instead.

### Get the value out

- **`Or(fallback)`** unwraps with a fallback.
- **`OrDefault()`** unwraps with `default`.
- **`OrNull()`** unwraps into a nullable: `int?` for an `Option<int>`, `string?` for an
  `Option<string>`.
- **`ToResult(error)`** turns absence into an error.
- **`Traverse`** validates an optional input when it is present.
  `dto.Phone.ToOption().Traverse(Phone.Create)` is a `Result<Option<Phone>, PhoneFault>`.
  `TraverseAsync` does the same with a check you await, such as a lookup.
- **`Transpose()`** turns an `Option<Result<...>>` inside out, and a `Result<Option<...>>` back.

`ToString()` never prints the content, so an option is safe to log. The debugger shows it.

### Continue a pending lookup

A lookup that is still running is a `Task<Option<T>>`. You continue it the way you continue a pending
result:

```csharp
Result<Customer, SignInFault> active = await repository.FindByEmailAsync(email)   // Task<Option<Customer>>
    .FilterAsync(c => c.IsActive)
    .TapAsync(c => audit.SeenAsync(c))                                             // waited for
    .ToResultAsync(SignInFault.Unknown);                                           // into a Result pipeline
```

`MapAsync`, `BindAsync`, `FilterAsync`, `TapAsync` and `ToResultAsync` continue a pending option. An
option also has `MapAsync`, `BindAsync` and `TapAsync` over callbacks that return a `Task`.

**Good to know**

- **Arguments are checked first.** Each one checks its delegates, and `ToResultAsync` its error,
  before the option is known.
- **A callback runs where the task it waits for completed.** When that task was still pending, that
  isn't the caller's synchronization context. A pending result's callbacks behave the same way
  (below).

## Result&lt;T, TError&gt; and Result&lt;TError&gt;

The outcome of an operation. `Result<T, TError>` holds a value or an error of your own type.
`Result<TError>` is for a command that produces no value, and holds a success or an error. Neither
has a `.Value` or an `.Error`.

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

Return a bare value for a success, and `Result.Error(...)` for an error. If a bare error converted as
well, it would be ambiguous in the worst way: in a `Result<long, int>`, `return 5;` would pick the
error, because `int` is the closer match. `Result<TError>` has no value to confuse it with, so there
a bare error converts too.

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

Only `Result<TError>` converts to `bool`. On a result with a value, a `bool` would say whether the
operation succeeded where a reader expects the value. `if (await users.IsEmailTakenAsync(email))` over
a `Result<bool, DbFault>` would take the branch for a success holding `false`. Match a valued result,
or compare its `State`.

- **`Map`, `Bind`, `Tap` and `TapAsync`** run only on success, and carry an error through unchanged.
- **`MapError`, `TapError` and `TapErrorAsync`** run only on an error.
- **`Recover`** hands an error to its selector and returns what the selector returns for it: a
  success, or the error kept.
- **`Ensure(predicate, error)`** turns a success whose value breaks a rule into that error.

A lambda that returns a bare value on one branch and `Result.Error(...)` on the other needs its type
argument: `placed.Bind<Invoice>(o => o.IsPaid ? invoices.Of(o) : Result.Error(OrderFault.Unpaid))`.
C# infers a lambda's return type from its body alone.
`placed.Ensure(o => o.IsPaid, OrderFault.Unpaid).Map(invoices.Of)` says the same without one.

**Good to know**

- **A `default` result throws.** An array slot or an unassigned field can still produce a
  `default(Result<...>)`. Its `State` is `ResultState.Uninitialized`, and every member that would pick
  a branch throws `InvalidOperationException` on it, rather than invent a `default(TError)`.
- **`ToString()` never prints the value or the error.**

### Chain steps

You chain asynchronous steps without an `await` on each. `Map`, `Bind`, `MapError`, `Recover`, `Tap`,
`TapError` and `Ensure` also continue a `Task<Result<...>>` (`MapAsync`, `BindAsync`, ...). You await
the chain once, at its end, and `Match` follows that `await`.

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
sequence order. `Traverse` calls nothing after it. None of them collects errors.

`TraverseAsync` awaits one operation at a time, in order. It starts none after an error, or once the
cancellation token you give it is cancelled. For operations that may all run at once, use
`Task.WhenAll` and `Sequence()`.

The asynchronous steps take callbacks that return a `Task`, and wait for them.
`.TapErrorAsync(fault => audit.RecordAsync(fault))` has recorded the fault when the chain's `await`
returns, and what it throws comes out of that `await`.

**Good to know**

- **A `ValueTask` callback needs `.AsTask()`**, or an `async` lambda. Without it, `TapAsync` and
  `TapErrorAsync` would bind it to their synchronous overload and not wait for it, and `MapAsync`
  would hold the `ValueTask` as the value.
- **Nothing resumes on your synchronization context.** So the package's awaits never deadlock code
  that blocks on a chain.
- **A callback runs where the task it waits for completed.** When that task had already completed,
  it runs at once, on the caller's context. When it was still pending, it runs without that context,
  usually on a thread-pool thread. In a UI or a Blazor component, await the task and continue with
  the synchronous combinators
  ([where a callback runs](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/result.md#where-a-callback-runs)).

### An exception as an outcome

Without help, the only way from a call that throws into a `Result` is a `try`/`catch` you write by
hand. `Result.Catch` runs the call and returns its value, or the exception as the error.

```csharp
Result<int, Exception> parsed = Result.Catch(() => int.Parse("42"));                                   // Success: 42
Result<int, Exception> refused = Result.Catch(() => int.Parse("x"));                                   // Error: FormatException
Result<int, FormatException> narrowed = Result.Catch<int, FormatException>(() => int.Parse("x"));     // only that type; any other propagates
Result<Exception> copied = Result.Catch(() => File.Copy(source, target));                             // a command: a success, or the exception
Result<byte[], Exception> fetched = await Result.CatchAsync(() => client.GetByteArrayAsync(url));      // waits for the task; its exception is the error
```

- **`Catch<T, TException>` catches that type only.** Every other exception propagates, so the error
  type says what was expected to fail. Spell both type arguments, since C# infers all or none.
- **`Catch<FormatException>(() => { ... })`, with one type argument, is the command form**, for a
  call without a value. Given a call that returns a value, which it would discard, it doesn't compile,
  and the error names the valued form.
- **`CatchAsync` awaits the task**, so an exception thrown after its first `await` is the error too.
  The error is what `await` throws. For a task that faulted with several exceptions, as
  `Task.WhenAll`'s does, that is the first of them, while the task's `Exception` holds them all. Hold
  the task where every failure matters.
- **A cancellation passes through.** An `OperationCanceledException` passes through all of them,
  because a cancellation is no outcome, and `Recover` would run on it as on a failure. So does an
  `AggregateException` that holds only cancellations, as `Wait()` and `.Result` throw for a cancelled
  task. One that holds anything beside them is the error, as it was thrown.
- **A timeout is the error.** `HttpClient` reports its `Timeout` elapsing as an
  `OperationCanceledException` over a `TimeoutException`. That is the call failing, so it is the
  error, wrapped or not, `.Result` on such a call included. To catch the timeout alone, name
  `TaskCanceledException` or `OperationCanceledException` in a narrowed `Catch`.
- **A task goes to `CatchAsync`.** A delegate that returns a `Task`, an `async` lambda among them,
  given to `Catch` or its command form doesn't compile, and the error names `CatchAsync`. The task's
  exception would be thrown after `Catch` had returned.
- **`CatchAsync` takes a `ValueTask` too**, as in `Result.CatchAsync(() => stream.ReadAsync(buffer))`.
  An `async` lambda still goes to the `Task` form.
- **A `ValueTask` given to `Catch`, or a task spelled as the type argument** (`Catch<Task<int>, ...>`),
  is rejected when called, before the delegate runs. Use `CatchAsync`.
- **A delegate that returns null throws**, as `Result.Success(null)` does.
- **The other members don't catch.** `Map`, `Bind` and the rest are unchanged: a selector that throws
  propagates.
- **An exception is for the code that called, not for a client.** `ToHttpResult()` doesn't accept a
  `Result<T, Exception>`. Use `MapError` to turn the exception into a fault that it does accept.

### A fault with a sentence

A fault is one of a fixed set, an enum's member or a closed set's. What varies per occurrence travels
beside it, in a `Fault<TFault>`: the fault as its `Reason`, and an optional `Detail`.

- **`Fault.Of(OrderFault.NotAmendable, "The order shipped on 3 May.")`** infers the type.
  `Fault.Of(OrderFault.NotFound)` makes one without a detail. A blank detail throws
  `ArgumentException`. Leave the detail out instead, and it is `null`.
- **A bare fault doesn't convert to one**, so comparing the two doesn't compile. Write
  `fault.Reason == OrderFault.NotFound`.
- **`WithRule` names the rule that failed**, where the reason covers several rules. The rule is an
  enum's or a closed set's member, kept as a qualified key:
  `Fault.Of(AccountFault.EmailTaken).WithRule(EmailRule.Reserved)` has the `Rule`
  `EmailRule.Reserved`.
- **CodoMetis.TypeKit.AspNetCore** turns a `Result<T, Fault<TFault>>` into a response as it does the
  fault, with the detail as the problem's `detail` and the rule as its `rule`.
- **A closed set whose members are faults** declares `IFaultSet<TSelf>`, and a `FaultKind` and an
  optional description per member. The host derives each status from them.
- **Faults of several types.** Where shared code decides some rejections for every boundary,
  `Fault<OrderFault, EditFault>` holds a fault of either type, and `Fault<T1, T2, T3>` one of any of
  three. A fault of each type converts, and `Match` takes a delegate per type.
  `Fault<OrderFault, EditFault>.Of(reason)` names the type where a fault becomes a result. C# needs
  that, because it is a second conversion.

## JSON converters that say what they write

Two converters write a decimal in one spelling per value, its smallest scale:
`DecimalNumberJsonConverter` as a number, and `DecimalStringJsonConverter` as a string. They do so as
a value and as a dictionary key, and read either form. A value object over a decimal is written by
whichever one the options hold.

A converter that implements `IDescribedJsonConverter` says what it writes. `JsonSchemaDescriptions`
in the options describes a converter someone else wrote. With these, two tools see what is really
written: System.Text.Json's schema exporter, through `DescribedJsonSchema.ExporterOptions()`, and the
OpenAPI document, through CodoMetis.TypeKit.AspNetCore's `AddTypeKit()`. The
[JSON](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/docs/json.md#decimals-in-one-spelling)
page shows both.

## Not wire types

`Option` and `Result` never go over the wire. The one wire type in this package is `Patch<T>`, a
merge patch's field (below).

System.Text.Json rejects an `Option`, a `Result`, a `Fault<TFault>` and a fault of several types, in
both directions. A fault is matched to a response, as the result holding it is. The
`NotSupportedException` names the type and the alternative:

```csharp
JsonSerializer.Serialize(new OrderDto(Option.Some(customer)));
// NotSupportedException: Option<Customer> is not a wire type. A serialized shape says absent with a
// nullable (Customer?), and ToOption() and OrNull() convert at the boundary. ...
```

Without this, the serializer would see no public members. It would write `{}` for a `Some` and read
it back as `None`, with nothing raised and the missing data as the only evidence. A `Result` would
write its `State` and read back uninitialized.

So a request, a response, a stored document or an event payload says absent with `T?`, and the
option lives between them: `dto.Nickname.ToOption()` on the way in, `nickname.OrNull()` on the way
out. You match a result to a response or a document. It has no wire shape of its own.

The markers `Option.None()`, `Result.Success(...)` and `Result.Error(...)` are rejected too, so an
endpoint that returns one fails on its first call rather than answering `{}`. So is a nullable of any
of them, an `Option<T>?` in a PATCH-style shape, whether it holds a value or not.

They aren't columns either. EF Core can't map an `Option` or a `Result` property, and says so when it
builds the model. An entity says absent with `T?` too.

**Good to know**

- **Your own converter wins.** A converter registered on the `JsonSerializerOptions` takes precedence
  over the rejection. To put `Option<T>` on the wire, write one and register it there, on those
  options only.
- **A missing property reaches no converter.** The serializer leaves it `default`: a `None`, or an
  uninitialized result. Where absence must be an error too, mark the property `required`, or set
  `RespectRequiredConstructorParameters` on the options.
- **A source-generated context can't describe an `Option<T>?` or `Result<...>?` property.** It fails
  when it builds that shape's contract, before any document is read or written, with the serializer's
  own `InvalidOperationException`:
  ``The converter '' is not compatible with the type 'CodoMetis.TypeKit.Option`1[System.String]'.``
  The rejection can't be TypeKit's own there. That contract takes a converter typed for
  `Option<string>` alone, which only `MakeGenericType` could build for every `T`. Declare the
  property `T?`.

## Value objects

A value object wraps exactly one value, and equals another when the values inside are equal. You
declare it in one line, and
[CodoMetis.TypeKit.Generators](https://www.nuget.org/packages/CodoMetis.TypeKit.Generators)
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

This package holds the interfaces every value object is built on:

| Interface | What it means |
|---|---|
| `IValue<T>` | Wraps any `T`. Gets `From(value)`. |
| `IValidatedValue<TSelf, T, TFault>` | You write `Create`, which returns a `Result`. Gets `TryFrom(value)`, which returns an `Option`, and `FromKnownGood(value)`, which throws and names the caller's expression, never the value. Every generated way to create one runs `Create`: JSON, parsing and the type converter. |
| `IValueObject<TSelf, T>` | What every generated value object implements: `Value`, equality. Run-time code recognises a value object by the attribute the generators add beside it, whose type arguments are constrained to this interface, never by name. |
| `IPlainValueObject<TSelf, T>` | A value object with no rules: `From` accepts any `T`. Plain value objects only. |
| `IValueObjectMaterializer<TSelf, T>` | Rebuilds an instance **without validation**, for values your application wrote itself, such as a database column. Implemented explicitly, so it isn't on the public surface. |

Pick the factory by what you need:

- **`Create`** when the caller has to learn what to fix.
- **`TryFrom`** when "is it valid?" is the whole question.
- **`FromKnownGood`** for a literal in your code, or a value your code has just produced.
- **`From`** when there are no rules.

`OrderId.New()` creates an identifier from a version 7 `Guid`. It is an extension in the
`CodoMetis.TypeKit` namespace, so import that in the file that calls it.

A validated value object has no `From`, so nothing that looks harmless throws on input. The one
factory that throws is `FromKnownGood`. Its name says the caller vouches for the value, and its
exception names the expression at the call site, never the value.

The fault `Create` returns is what every rejection reports. `FromKnownGood`'s exception carries it,
and the generated JSON converter and parsing name it in their `JsonException` and `FormatException`.
Wherever a rejection happens, it names the type and the rule and never the input, which can be a
secret. Text the underlying type can't read at all is reported as that, without quoting it. The
fault's own text is yours, so keep the input out of it.

### Generic code over value objects

The interfaces have static abstract members, so a method constrained on them works for every value
object. The generated types also implement `IEqualityOperators`, `IComparisonOperators` and
`IMinMaxValue` where the underlying type allows, so they satisfy generic-math constraints too.
`OrderId.New()` is such a method: one extension for every plain (unvalidated) value object over a
`Guid`, not a member generated per type.

```csharp
static Option<TSelf> Read<TSelf, TFault>(string field)
    where TSelf : IValidatedValue<TSelf, string, TFault>
    where TFault : notnull =>
    TSelf.Create(field).ToOption();

static TId NewId<TId>() where TId : IValueObject<TId, Guid>, IPlainValueObject<TId, Guid> =>
    TId.From(Guid.CreateVersion7());
```

### Stored JSON

The generated JSON converter runs `Create`. That is right for input, and wrong for documents your
application stored before a rule existed. Register `StoredJsonConverterFactory` on that store's
options, and nowhere near input:

```csharp
var storeOptions = new JsonSerializerOptions { Converters = { new StoredJsonConverterFactory() } };
```

### Your own no-default structs

`[RequireCustomInitialization("Use Money.Of(...)")]` on a struct makes the analyzer reject `default`
and `new()` of it, as it does for `Option`, `Result` and every value object.

## Written text

Most applications trim what a person typed and reject a blank. Few reject a zero-width space, a
Hangul filler or NUL, which `IsNullOrWhiteSpace` lets through and `Trim` leaves in place. Fewer
compose the text. So a name written with the single character `U+00FC` (u with diaeresis) and the
same name written with `u` and a combining diaeresis become two rows of a unique index that look
alike. Two extensions on `string` handle all of it and return a `Result`:

```csharp
var name    = "  Jean\u00A0Paul ".AsText();        // Success: Jean Paul, trimmed, the no-break space folded to an ordinary one
var subject = "subject\u200Bid".AsIdentifier();   // Error: Hidden, a zero-width space
var missing = ((string?)null).AsText();            // Error: Blank
```

Who chose the value decides which one you call:

- **`AsText()`** is for one line of text a person wrote, such as a name or a title. It trims, folds
  every space separator to an ordinary space, and composes to NFC (not NFKC, so a ligature or a
  fullwidth letter stays as written). It rejects a character that renders as nothing or breaks the
  line. It keeps the two zero-width joiners, because they spell Persian, Arabic and Indic scripts and
  hold an emoji sequence together.
- **`AsIdentifier()`** is for a value another system issued, such as a subject claim, a session id or
  a key. It trims and changes nothing else, since an issued value is compared against what its issuer
  sends next time. It rejects the joiners too.

Both reject these as `Hidden`, once the surrounding whitespace is trimmed:

- **Control characters:** NUL, a tab, a line feed, a carriage return, and the rest of the C0 and C1
  ranges. A multi-line field, such as a comment, goes through neither guard.
- **Format characters:** the soft hyphen, the zero-width space, the word joiner, the byte-order mark,
  the bidirectional marks, embeddings, overrides and isolates (so an Arabic name ending in a
  right-to-left mark is rejected), the invisible operators and the tag characters. Not the Arabic
  number sign, the end of ayah and the other prepended concatenation marks, which are drawn, even
  with nothing after them.
- **Private-use characters**, and the line and paragraph separators.
- **Broken encodings:** an unpaired surrogate, and `U+FFFE`, a byte-order mark read in the wrong byte
  order. `string.Normalize` throws on these, and the guards reject them instead.
- **Characters that render nothing**, whatever their category: the combining grapheme joiner, the
  Hangul fillers, the Braille pattern blank, the Khmer inherent vowels and the Khitan filler.
- **A misplaced variation selector**, anywhere but after the character it varies (an emoji's
  presentation, an ideograph's variant).
- **More than 30 combining marks in a row.** No script stacks that many, and composing a run
  reorders it in time that grows with the square of its length.
- **A value of joiners, variation selectors and combining marks alone**, which renders no character
  of its own.

Both scan by `Rune`, so a character above the BMP is seen for what it is.

`AsText()` needs the runtime's globalization data to compose. In globalization-invariant mode it
throws ([why](#astext-in-globalization-invariant-mode)), so written text has no guard there.
`AsIdentifier()` composes nothing and works there, for the values it is for.

`TextFault` has two members, because you decide what a blank means. A required field makes `Blank`
an error. An optional one makes it absence: `AsOptionalText()` and `AsOptionalIdentifier()` return a
`Result<Option<string>, TextFault>`. `Hidden` is a rejection in both. A null is `Blank`, since the
extension is the door a bound request property goes through, and binding enforces no
non-nullability.

A value object over either guard is one line. Name it for the field rather than for the guard:
`Description` says more than `Text`, and two fields of one type can be assigned to each other.

```csharp
public readonly partial record struct Description : IValidatedValue<Description, string, TextFault>
{
    public static Result<Description, TextFault> Create(string value) =>
        value.AsText().Map(text => new Description(text));
}
```

**Good to know**

- **Unassigned code points are accepted, on purpose.** The runtime's Unicode tables lag the
  standard, so a name spelled with a newly assigned character would be rejected until the SDK
  caught up.
- **Confusable letters across scripts are accepted, on purpose.** That is a comparison question, not
  a rejection.

## Merge patches

A PATCH body in JSON Merge Patch (RFC 7396) says one of three things about a field:

- **nothing**, so it stays;
- **`null`**, so it is cleared;
- **a value**.

System.Text.Json can't tell the first two apart, because it calls no converter for a property it
doesn't find. So every PATCH endpoint rebuilt the difference by hand. `Patch<T>` is that field, and
the one wire type in this package, since `Option` rejects JSON. An option says whether a value
exists, a patch whether a request mentioned it.

```csharp
public sealed record OrderPatch(Patch<string> Name, Patch<string> Nickname, Patch<int> Quantity);
```

```csharp
var patch = JsonSerializer.Deserialize<OrderPatch>("""{"nickname":null,"quantity":2}""", JsonSerializerOptions.Web)!;
var name = patch.Name;           // Absent, the field was not mentioned
var nickname = patch.Nickname;   // Null, the field was null
var quantity = patch.Quantity;   // Value: 2
```

`default(Patch<T>)` is `Absent`. It is the one `default` in this package that means something, and
the analyzers leave it alone. Applying a patch needs no reflection:

```csharp
patch.Name.Tap(name => order.Name = name);                       // a clear cannot reach a required field
patch.Nickname.IfPresent(nickname => order.Nickname = nickname); // null clears an optional one
order.Quantity = patch.Quantity.Match(
    quantity => quantity,
    () => throw new InvalidOperationException("Quantity cannot be cleared."),
    () => order.Quantity);
```

Reading never rejects a null. The Null state is part of the wire format whatever `T` is, and whether
a clear is accepted is your application's rule, which you state in `Match`.

So you never need a `Patch<T?>`. `T` is the value's type and `notnull`, as an option's is.
`Patch<string>` is a field that can be cleared. `Patch<string?>` is a warning (CS8714) at its
declaration, rather than a nullable handed to every `Match`, `Map` and `Traverse` though a value is
never null.

A value object inside a patch keeps its own rule. `Patch<Email>` given a value that `Create` rejects
is rejected as anywhere else, without quoting the input. Where the rejection should be the
endpoint's own fault, the request carries a `Patch<string>`, and `Traverse` validates it:
`request.Email.Traverse(Email.Create)` is a `Result<Patch<Email>, EmailFault>`, with a clear and an
absence passed through. `TraverseAsync` does the same with a check you await, and `Map` changes a
value the same way.

**Good to know**

- **An absent patch can't be written**, since a converter can't leave a property out. Give a property
  that is written `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`, and an absent
  patch is left out.
- **A source-generated context never sees `T`**, since the patch's converter hides it. A primitive, a
  string, a `Guid`, a date or a `Uri` is read with the serializer's built-in converter, and a value
  object with its own. List any other type, a class or an enum, in the context, or the serializer
  rejects it with its own message naming it.
- **The OpenAPI document** describes a patch property as `T`'s schema, not required, admitting null
  ([CodoMetis.TypeKit.AspNetCore](https://www.nuget.org/packages/CodoMetis.TypeKit.AspNetCore)).
- **This isn't ASP.NET Core's JSON Patch** (RFC 6902, a list of operations).

## The analyzers

They arrive with this package, so every project that can see the interfaces gets the checks:

- **CMTK0001**: `default(OrderId)`, `new OrderId()`, `new()` and `default` of a value object, an
  `Option`, a `Result` or a `[RequireCustomInitialization]` struct. Such an instance passed no
  factory.
- **CMTK0002**: a type implements `IValue<T>`, `IValidatedValue<,,>` or `IClosedSet<TSelf>`, but the
  project doesn't reference `CodoMetis.TypeKit.Generators`, so nothing would be generated for it.
- **CMTK0003**: a `Result` or `Option` that a call returns, dropped, waited for and forgotten, or
  walked by a loop that never reads it. `_ = ...` says you mean it.
- **CMTK0004**: `Materialize`, which rebuilds a value object without its rules, called anywhere but
  the EF Core package.
- **CMTK0005** (warning): `new OrderId[n]` and the like, which fill every slot with a default
  instance.
- **CMTK0006** (warning): a property or field of such a type that nothing sets. `required` fixes it.
- **CMTK0007** (Info, a suggestion in the IDE): `FromKnownGood` given a parameter, which is input as
  far as the code can tell.
- **CMTK0008** (warning): `order.CustomerId.Value == product.Id.Value`, the values inside two
  different value objects compared, which the types exist to prevent.
- **CMTK0009** (warning): a call that hands out a `default` instance when it finds nothing,
  `ids.FirstOrDefault()` or `ProductCode.TryFrom(s).OrDefault()`. `FirstOrNone()`, `GetValueOrNone()`
  and `Or(fallback)` say what happens then.
- **CMTK0010**: a type argument that breaks what its parameter declares with `[Abstract]`, `[Sealed]`,
  `[Record]`, `[Concrete]`, `[Interface]`, `[Unsealed]`, `[ValueObject]`, `[ClosedSet]`, `[FlagsEnum]`
  or `[ReadOnlyStruct]`, the constraints C# has no `where` for, at any closed use. Also a pair of
  arguments for two parameters marked `[Distinct]` or `[Unrelated]` that are one type, or related. A
  method that implements an interface's method, or overrides one, carries the constraint of what it
  implements.
- **CMTK0011**: a type under an interface or base class marked `[RequireParameterlessConstructor]`
  without the parameterless constructor it asks for, which the host would look for by reflection.
  The code fix writes it.
- **CMTK0012**: a closed set's `Match` given an argument by position, where reordering two members
  would swap their branches and still compile. The code fix names every argument.
- **CMTK0013**: a closed set's member written outside its declaration, in the static constructor or
  another field's initializer, so it holds an instance named and listed as another member.
- **CMTK0014** (warning): a `FaultKind` or a `ConstructorAccessibility` that names no member, such as
  a cast or `default`.

The rules also run in Razor components (`.razor`, `.cshtml`).

The ids stay the same across releases. The rule table is in
[CodoMetis.TypeKit.Analyzers](https://www.nuget.org/packages/CodoMetis.TypeKit.Analyzers).

## Native AOT

This package works in a trimmed or Native AOT application. The packages are built with the trim and
AOT analyzers on.

- **One exception: the query helpers on EF Core.** `FirstOrNone`, `FirstOrError`, `SingleOrNone` and
  `ElementAtOrNone` on an EF Core query compose the query where EF can't precompile it, and throw
  there. Write EF's operators at the call site and take the first element of the list.
- **List value objects in your JSON context.** Native AOT requires source-generated JSON. List the
  value objects (or the types that hold them) in your `JsonSerializerContext`, not the types inside
  them. The generated converters build the underlying type's contract themselves, from a converter on
  your options, the type's own `[JsonConverter]`, or the serializer's built-in one. Only a value
  object over a type of your own that has no converter needs that type in the context, and the
  serializer's error names it.
- **List the enum of an enum-backed value object too**, if it should be written by name. A context's
  `UseStringEnumConverter` doesn't reach an enum the context doesn't list.
- **`Option` and `Result` reject JSON** there exactly as on the JIT, and `StoredJsonConverterFactory`
  reads without the rules.

## Why does this throw?

Some calls throw on purpose, where carrying on would hand back a plausible wrong answer. Each of
these exceptions ends its message with a link to its entry here: what it rejects, why, and what to
call instead.

### Option or Result in JSON

**Thrown:** `NotSupportedException: Option<Customer> is not a wire type...`, or
`Result<Order, OrderFault> is an outcome, not a wire type...`. System.Text.Json throws it when it
reads or writes an `Option`, a `Result`, a `Fault<TFault>` or a `Fault<T1, T2>`, a marker such as
`Option.None()`, or a nullable of any of them. Writing an absent `Patch<T>`, which no document can
carry, fails with the same link. Mark that property
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]`.

**Why:** without it, a `Some` is written as `{}` and read back as `None`, and nothing is raised. See
[Not wire types](#not-wire-types) and the [design](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/design/option-result.md).

**Instead:** say absent with `T?` in the shape, and convert at the boundary: `dto.Nickname.ToOption()`
on the way in, `nickname.OrNull()` on the way out. `Match` a result to a response. A source-generated
context rejects an `Option<T>?` property earlier, with the serializer's
``The converter '' is not compatible with the type 'CodoMetis.TypeKit.Option`1[...]'``. Declare `T?`
there too.

### A null in an Option or a Result

**Thrown:** `ArgumentNullException`, from:

- `Option.Some(null)`, `Result.Success(null)` or `Result.Error(null)`;
- a `Map` or `MapError` whose selector returns null, and a `Patch<T>.Map` whose selector returns
  null, since a clear is the Null state, never a value;
- `FirstOrNone()` over a null element;
- a null error given to `ToResult`, `Ensure` or `FirstOrError`;
- `CatchAsync` given a delegate that returns no task. A null can't be awaited, and it isn't an
  exception the call threw. Every other asynchronous combinator, `MapAsync`, `BindAsync`, `TapAsync`,
  `TapErrorAsync`, `RecoverAsync` and `TraverseAsync`, rejects such a delegate the same way.

**Why:** an option that reports a value always has one, and a result's value and error are never
null. `notnull` isn't checked at run time, so the factories check it.

**Instead:** `value.ToOption()` turns a null into `None`. A selector that may return null becomes
`Bind(c => c.Nickname.ToOption())`.

### An uninitialized Result

**Thrown:** `InvalidOperationException`, as one of:

- `This Result was never initialized...` from `Match`, `Map`, `TryGetValue` and every other member
  that picks a branch;
- `This Fault<OrderFault, ConcurrencyFault> holds no fault...` from `Match` on a fault of several
  types;
- `This Fault<OrderFault> holds no fault...` where a `default` fault of a closed set converts to one.

**Why:** a `default` result is neither a success nor an error, and taking the error branch would
invent a `default(TError)`. A `default` fault of several types holds a fault of none of them. Both come
from an array slot, a field nothing set, or a property missing from a JSON document.

**Instead:** create results through `Result.Success`/`Result.Error` or a conversion, and faults
through `Fault.Of`. CMTK0005 and CMTK0006 point at the arrays and fields, and `required` closes a
property. `State` tells an uninitialized result apart without throwing.

### A value object refused a value

**Thrown:** `JsonException: ProductCode refused the JSON value (TooShort).`, or
`FormatException: ProductCode refused the input (TooShort).` from `Parse` and the type converter.

**Why:** every generated way to create a value object runs `Create`, so input the rules reject never
becomes a value object. The message names the rule and never the input, which can be a secret. See
[Value objects](#value-objects).

**Instead:** where the caller should learn which rule failed, call `ProductCode.Create(text)` and
match the fault. Use `TryFrom` or `TryParse` where valid or not is the whole question. ASP.NET Core
answers the `JsonException` of a request body with 400.

### FromKnownGood refused a value

**Thrown:** `InvalidOperationException: ProductCode refused request.Code, which the call site
declared known-good (TooShort).`

**Why:** `FromKnownGood` is for a literal or a value your code has just produced, so a rejection
there is a bug at that call site. The message names the call site's expression and never the value.

**Instead:** send input through `Create` or `TryFrom`. CMTK0007 points at a `FromKnownGood` given a
parameter.

### A value object could not read the input

**Thrown:** `FormatException: Quantity could not read the input as Int32.` from `Parse` and the type
converter, or `JsonException: Quantity could not read the JSON value as Int32.` from JSON.

**Why:** the underlying type's own message quotes the input ("The input string '...' was not in a
correct format."). So TypeKit replaces it, and doesn't keep it as the inner exception either. See the
[design](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/design/adr/README.md),
decision 27.

A JSON error still carries its path. For a value object read as a dictionary key, the path ends in
the key itself (`$.items.SE`), and MVC uses the path as the error's name in its 400. Keep secrets out
of a dictionary's keys.

**Instead:** use `TryParse` for text that may not parse. If the text is yours to log, log it before
parsing.

### A null given to a value object

**Thrown:** `ArgumentNullException: ProductName wraps no null.` from `From`, or
`JsonException: ProductName cannot be read from a JSON null.` from JSON.

**Why:** a value object is a value. Its `Value` promises never to be null, as an `Option` never holds
one. A string-backed value object would otherwise wrap the null it was given.

**Instead:** where the value can be absent, declare the property or parameter as `ProductName?`.
JSON then reads a `null` as `null`, and the value object is never asked to hold it.

### A closed set has no member of that name

**Thrown:** `FormatException: OrderStatus has no member of that name.` from `Parse` and the type
converter. JSON throws a `JsonException` with the same message, also for a token that isn't a string.

**Why:** a closed set's members are its `public static readonly` fields. A name that matches none of
them, compared ordinally, is rejected. The message names the set and never the input.

**Instead:** `TryParse` returns `false`, `Parse(name, ignoreCase: true)` accepts any casing, and
`All` lists the names there are.

### An instance that is not a member of its set

**Thrown:** `InvalidOperationException: OrderStatus: this instance is not a member of the set, so it
has no name.` from `Name`, `QualifiedName`, the generated `ToString()`, `Match`, the JSON converter
and the EF Core column.

**Why:** a closed set's constructors are private, but the set's own code can still call one outside
a member's declaration, in a static method or a private field. That instance has no name. Written as
JSON or stored in a column, it would come back as nothing.

The same happens when a member's initializer or constructor, or any other static field initializer
of the set, reads a member's name, because the names are given after every static field is
initialized. There the exception surfaces as a `TypeInitializationException`, and the set can't be
used for the rest of the process.

**Instead:** wherever the set's code produces an instance, return one of the members, such as
`OrderStatus.Open`. Keep the data a member needs in its own constructor's arguments. Read a name in a
property or method, not in a static field initializer.

### A closed set read before it is initialized

**Thrown:** `InvalidOperationException: OrderStatus is still being initialized: a static field
initializer of the set read All, or a member by name, before every member existed.` from `All`,
`Parse`, `TryParse` and the JSON converter. It surfaces as a `TypeInitializationException`, and after
it the set can't be used for the rest of the process.

**Why:** a static field initializer of the set runs while its members are still being created. `All`
is filled once every member exists, and a member declared below the reading field is still null. So
`public static readonly IReadOnlyList<OrderStatus> Snapshot = All;` held null, for a list typed as
never null, and `Parse` handed out null for a later member.

**Instead:** read `All` or parse a name in a static property or method, which runs once the set is
initialized: `public static IEnumerable<OrderStatus> Active => All.Where(status => status != Cancelled);`.
In a field initializer, refer to a member by its field, `Open`, not by its name.

### A task given to Catch

**Thrown:** `ArgumentException: Catch was given a delegate whose value is a task...`, from:

- `Catch`, when its value's type is a `Task` or a `ValueTask`, spelled
  (`Catch<Task<int>, HttpRequestException>`) or inferred from a delegate that returns a `ValueTask`;
- `CatchAsync`, when the task's value is another task.

Nothing has run: the delegate is rejected before it is called.

**Why:** the task's exception is thrown after `Catch` has returned, so the result would be a success
whatever the call does. A delegate that returns a `Task` is rejected at compile time. One that
returns a `ValueTask` is rejected when called, since a compile error there would break a build that
works today. And a spelled type argument chooses its overload itself.

**Instead:** use `CatchAsync`, which awaits the task, a `ValueTask` too:
`await Result.CatchAsync(() => stream.ReadAsync(buffer))`. In `CatchAsync`, await the inner task in
the delegate.

### AsText in globalization-invariant mode

**Thrown:** `PlatformNotSupportedException: AsText() composes text to Unicode normalization form C,
and this process cannot...` from `AsText()`, whatever the string, in a process in
globalization-invariant mode. That mode is on with
`<InvariantGlobalization>true</InvariantGlobalization>` in the project, which Native AOT and
container templates often set, or with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` in the environment.

**Why:** in that mode `string.Normalize` returns its input unchanged. `AsText()` would accept a name
written with a combining diaeresis as a success, uncomposed, and one name would be kept in two
spellings that compare unequal. That is the defect the guard exists to prevent, and it would be
silent.

**Instead:** turn the mode off and make ICU available to the process. You need both, since ICU
installed while the mode stays on does nothing:

- **The mode off:** `<InvariantGlobalization>false</InvariantGlobalization>`, and no
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`.
- **ICU installed:** `libicu` on Debian and Ubuntu, `icu-libs` on Alpine.

Written text has no guard in this mode. `AsIdentifier()` is no replacement: it is the guard for a
value another system issued. It rejects the zero-width joiners that Persian, Arabic, Indic scripts
and emoji sequences need, and it folds no space.

### Materialize and CMTK0004

This one isn't a run-time exception but a build error. `Materialize` rebuilds a value object without
`Create`, for a value your application wrote itself. Called anywhere but the EF Core package, it
fails the build
([CMTK0004](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0004)).
Use `Create` or `FromKnownGood` instead, and `StoredJsonConverterFactory` for JSON your application
stored.

## Where things are

- **`CodoMetis.TypeKit`:** `Option`, `Result`, `OrderId.New()`, `AsText()`, `AsIdentifier()`,
  `Patch<T>`, `[RequireCustomInitialization]`, `[RequireParameterlessConstructor]`, and the
  type-parameter attributes `[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]` and their kind.
- **`CodoMetis.TypeKit.ValueObjects`:** every value-object interface.
- **`CodoMetis.TypeKit.ClosedSets`:** `IClosedSet<TSelf>`. Closed sets are generated by
  [CodoMetis.TypeKit.Generators](https://www.nuget.org/packages/CodoMetis.TypeKit.Generators), whose
  README covers them.
- **`CodoMetis.TypeKit.CompilerServices`:** what only the generated code and the other TypeKit
  packages call, hidden from IntelliSense.

Targets .NET 10. Source and issues:
[github.com/CaffeinatedCoder/CodoMetis.TypeKit](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit).
