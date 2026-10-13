# Result

`Result<T, TError>` is the outcome of an operation: a value or an error of your own type.
`Result<TError>` is the outcome of a command that produces no value: a success or an error. Neither
has a `.Value` or an `.Error`: the outcome is reached through `Match`, `TryGetValue` and the
combinators, so a failure is handled where the value is used. Both live in `CodoMetis.TypeKit`; the
[base README](../src/CodoMetis.TypeKit/README.md#resultt-terror-and-resultterror) is their reference,
and [Option](option.md) is the type for absence without a reason.

The examples import `CodoMetis.TypeKit` and are a checkout: a cart is found, reserved against stock, and charged.

```csharp
public readonly partial record struct CartId : IValue<Guid>;

public readonly partial record struct OrderId : IValue<Guid>;

public enum CheckoutFault { CartUnknown, CartEmpty, OutOfStock, PaymentRefused }

public sealed record Line(string Sku, int Quantity, decimal Price);

public sealed record Cart(CartId Id, IReadOnlyList<Line> Lines);

public sealed record Reservation(OrderId Id, decimal Total);

public sealed record Receipt(OrderId Id, decimal Total);
```

## Two shapes

```csharp
public Result<Reservation, CheckoutFault> Reserve(Cart cart)   // a value, or a fault
{
    if (cart.Lines.Count == 0) return Result.Error(CheckoutFault.CartEmpty);
    if (!InStock(cart)) return Result.Error(CheckoutFault.OutOfStock);

    return new Reservation(OrderId.New(), cart.Lines.Sum(line => line.Quantity * line.Price));   // a bare value is a success
}

public Result<CheckoutFault> Charge(decimal total) =>              // a success or a fault, and no value
    total <= limit ? Result.Success() : CheckoutFault.PaymentRefused;   // a bare error converts here, where nothing can be confused with it
```

A bare value converts to a valued success; an error goes through `Result.Error(...)`, because in a
`Result<long, int>` a bare `5` could be either. `Result<TError>` has no value, so there a bare error
converts, and so does the `Result.Success()` marker. The fault type is yours: an enum for most
operations, a closed set when each fault carries constant data, a `Fault<TFault>` when one occurrence
carries a sentence, a record where a fault carries the rule's data
([Value objects](value-objects.md#faults)). Keep the input out of it: a fault reaches messages and
logs. `ToHttpResult()` takes the first three.

## Reaching the outcome

```csharp
var reserved = checkout.Reserve(cart);

string outcome = reserved.Match(reservation => $"reserved {reservation.Total}", fault => $"refused: {fault}");   // "reserved 30"
ResultState state = reserved.State;                                                                                 // Success

if (reserved.TryGetValue(out var reservation, out _) && checkout.Charge(reservation.Total)) audit.Add("charged");   // a Result<TError> converts to bool; a valued one never does

Option<Reservation> maybe = reserved.ToOption();   // the fault dropped, where only presence matters
```

`Match` takes both branches and returns what they return. `TryGetValue(out value, out error)` gives
the branch and the content in one call: `value` is non-null when it returns true, `error` when it
returns false. Only `Result<TError>` converts to `bool`, so `if (checkout.Charge(total))` reads as it
should. A valued result does not: `if (await users.IsEmailTakenAsync(email))` over a
`Result<bool, DbFault>` would take the branch for a success holding `false`. `State` tells the three states apart without throwing:
`Success`, `Error`, and `Uninitialized` for a `default`, which an array slot or an unassigned field
still produces and on which every branching member throws rather than inventing a `default(TError)`.
CMTK0005 reports an array of results created with a length, CMTK0001 a `default` in source.

## A pipeline

```csharp
Result<Receipt, CheckoutFault> receipt = checkout.FindCart(cartId)              // Result<Cart, CheckoutFault>
    .Ensure(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty)             // a rule on the value: break it, and this is the error
    .Bind(checkout.Reserve)                                                    // a step that can fail itself
    .Map(reservation => new Receipt(reservation.Id, reservation.Total))        // a step that cannot
    .Tap(r => audit.Add($"receipt {r.Total}"))                                 // on a success only
    .TapError(fault => audit.Add($"refused: {fault}"));                        // on an error only

Result<CheckoutFault> charged = receipt.Bind(r => checkout.Charge(r.Total));   // a command after a query

string paid = receipt.Match(r => $"paid {r.Total}", fault => $"refused: {fault}");     // "paid 30"
string ledger = charged.Match(() => "charged", fault => $"not charged: {fault}");      // "charged"
string empty = checkout.FindCart(emptyCartId).Ensure(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty).Bind(checkout.Reserve).Match(r => "reserved", fault => $"refused: {fault}");   // "refused: CartEmpty"
```

`Map`, `Bind`, `Tap` and `Ensure` run on a success and carry an error through unchanged; `MapError`,
`TapError` and `Recover` run on an error and carry a success through. The first error stops the chain,
and every step after it is skipped, which the `empty` line shows: `Reserve` never ran. `Recover` is
the error branch's `Bind`: its selector answers a success or another error, as a second chance:

```csharp
Result<CheckoutFault> settled = charged.Recover(fault => fault == CheckoutFault.PaymentRefused ? checkout.Invoice(cartId) : fault);   // Success
```

A lambda that returns a bare value on one branch and `Result.Error(...)` on the other needs its type
argument, `.Bind<Receipt>(r => r.Total > 0 ? r : Result.Error(CheckoutFault.CartEmpty))`, because C#
infers a lambda's return type from its body alone; `.Ensure(r => r.Total > 0, CheckoutFault.CartEmpty)`
says the same without one.

## Across layers

A fault names what the operation found. What a client is told is the host's decision, and the two
meet in `MapError`:

```csharp
public enum ApiFault { NotFound, Invalid, Unavailable }

static class Faults
{
    public static ApiFault ToApi(CheckoutFault fault) => fault switch
    {
        CheckoutFault.CartUnknown => ApiFault.NotFound,
        CheckoutFault.CartEmpty or CheckoutFault.OutOfStock => ApiFault.Invalid,
        CheckoutFault.PaymentRefused => ApiFault.Unavailable,
        _ => throw new UnreachableException(),
    };
}
```

```csharp
Result<Receipt, ApiFault> forApi = receipt.MapError(Faults.ToApi);   // Success, untouched; an error would be the ApiFault
```

In ASP.NET Core, `ToHttpResult()` turns the result into a response, the fault into problem details
with the status the host maps it to ([ASP.NET Core](aspnet-core.md#a-result-as-a-response)).

## Async chains

The asynchronous members continue a `Task<Result<…>>` without an `await` each, so a chain is awaited
once, at its end:

```csharp
Result<CheckoutFault> chargedAsync = await checkout.FindCartAsync(cartId)        // Task<Result<Cart, CheckoutFault>>
    .EnsureAsync(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty)          // a rule on a pending result
    .BindAsync(cart => checkout.ReserveAsync(cart))                              // an asynchronous step
    .MapAsync(reservation => reservation.Total)                                  // a synchronous step on a pending result
    .BindAsync(total => checkout.ChargeAsync(total))                             // a command: Result<CheckoutFault> remains
    .TapErrorAsync(fault => audit.Add($"not charged: {fault}"));

string asyncLedger = chargedAsync.Match(() => "charged", fault => $"not charged: {fault}");   // "charged"
```

The asynchronous steps take callbacks that return a `Task` and wait for them, so what a callback
throws reaches the chain's `await`. A callback that returns a `ValueTask` needs `.AsTask()` or an
`async` lambda: `TapAsync` and `TapErrorAsync` would otherwise bind to their synchronous overload and
not wait, and `MapAsync` would hold the `ValueTask` as the value.

## Collecting results

```csharp
Result<IReadOnlyList<Line>, CheckoutFault> lines = requested.Traverse(checkout.ToLine);   // every request to a line, or the first fault
Result<IReadOnlyList<Reservation>, CheckoutFault> all = reservations.Sequence();          // a list of results to a result of a list
Result<decimal, CheckoutFault> amount = unitPrice.Zip(quantity, (price, count) => price * count);   // both successes, or the first error
Result<Line, CheckoutFault> bulk = cart.Lines.FirstOrError(line => line.Quantity >= 10, CheckoutFault.OutOfStock);   // the first match, or the error

string summary = requested.Traverse(checkout.ToLine).Match(list => $"{list.Count} lines", fault => $"refused: {fault}");   // "2 lines"
string stopped = refused.Traverse(checkout.ToLine).Match(list => $"{list.Count} lines", fault => $"refused: {fault}");    // "refused: OutOfStock"
```

`Traverse`, `Sequence` and `Zip` (two to six results) stop at the first error, in sequence or
argument order, and `Traverse` calls its selector for nothing after it. None of them collects errors:
a fault type that is a list of your own is how several faults travel together.

An asynchronous operation runs one element at a time with `TraverseAsync`, each awaited before the
next starts, so nothing starts after a fault. With a cancellation token, the token is checked before
each operation and handed to it, and a cancellation propagates rather than becoming an error.
Operations that may all run at once are `Task.WhenAll` over their tasks and `Sequence()` over what
it answers.

```csharp
Result<IReadOnlyList<Reservation>, CheckoutFault> reserved = await carts.TraverseAsync(checkout.ReserveAsync);     // one at a time, in order; nothing starts after a fault
Result<CheckoutFault> charged = await totals.TraverseAsync(checkout.ChargeAsync);                                   // a command per element, likewise
Result<IReadOnlyList<Cart>, CheckoutFault> found = await ids.TraverseAsync(checkout.LoadCartAsync, cancellationToken);   // the token checked before each, and handed to it
```

## An exception as an outcome

A call that throws has no way into a `Result` except a `try`/`catch` written by hand. `Result.Catch`
runs the call and answers its value or the exception as the error:

```csharp
Result<Uri, Exception> url = Result.Catch(() => new Uri(text));                                        // Error: UriFormatException
Result<Uri, UriFormatException> narrowed = Result.Catch<Uri, UriFormatException>(() => new Uri(text));   // that type only; any other propagates
Result<Uri, ApiFault> asFault = Result.Catch(() => new Uri(text)).MapError(_ => ApiFault.Invalid);     // Error: Invalid
Result<Exception> written = Result.Catch(() => File.WriteAllText(path, "receipt"));                     // Success, a command
Result<string, Exception> read = await Result.CatchAsync(() => File.ReadAllTextAsync(path));           // Success: receipt
```

An exception is for the code that called, not for a client: `ToHttpResult()` does not accept a
`Result<T, Exception>`, and `MapError` turns it into a fault that it does, as `asFault` shows.
`Catch<T, TException>` spells both type arguments, since C# infers all or none. `CatchAsync` awaits
the task, so an exception thrown after its first `await` is the error too; a delegate that returns a
`Task` given to `Catch` is a compile error naming `CatchAsync`, since the task's exception would be
thrown after `Catch` had returned. An `OperationCanceledException` passes through every overload,
since a cancellation is not an outcome, and so does an `AggregateException` that holds cancellations
alone, as `Wait()` and `.Result` throw for a cancelled task; one that holds anything beside them is the
error, as it was thrown. A cancellation over a `TimeoutException` is the error, wrapped or not: that is
how `HttpClient` reports its `Timeout` elapsing, the call failing, and `.Result` on such a call is the
error too.
`Map`, `Bind` and the rest are unchanged: a selector that throws propagates.

## Query syntax

```csharp
Result<Receipt, CheckoutFault> receipt =
    from cart in checkout.FindCart(cartId)
    from reservation in checkout.Reserve(cart)       // each from sees the earlier ranges
    select new Receipt(reservation.Id, reservation.Total);
```

`Select` and `SelectMany` make it work; there is no `where`, since a result has no error to give a
filtered-out value, which is what `Ensure(predicate, error)` is for.

## The surface

Every public member of `Result<T, TError>`, `Result<TError>`, the static class `Result`, and the
extensions in the `CodoMetis.TypeKit` namespace. `T` and `TError` are `notnull` throughout.

**Creating**

| Member | On | Returns |
|---|---|---|
| `Success()` | `Result` | a `Success` marker that converts to any `Result<TError>` |
| `Success(value)` | `Result` | a `Success<T>` marker that converts to any `Result<T, TError>` |
| `Error(error)` | `Result` | an `Error<TError>` marker that converts to either shape |
| `Success(value)`, `Error(error)` | `Result<T, TError>` | the concrete result, where the type is spelled |
| `Success()`, `Error(error)` | `Result<TError>` | likewise |
| implicit from `T`, `Success<T>`, `Error<TError>` | `Result<T, TError>` | a bare value is a success; a bare error is not, since it could be either |
| implicit from `TError`, `Success`, `Error<TError>` | `Result<TError>` | a bare error converts, and so does the marker |
| `Catch(action)`, `Catch<T, TException>(action)`, `Catch<TException>(action)` | `Result` | `Result<T, Exception>` or `Result<Exception>`; narrowed to one exception type |
| `CatchAsync(action)` and the same type-argument forms | `Result` | the same, awaiting the task |
| `ToResult(error)` | `Option<T>` | `Result<T, TError>` |
| `FirstOrError(predicate, error)`, `LastOrError(predicate, error)` | `IEnumerable<T>`; `FirstOrError` on `IQueryable<T>` too | the first or last match, or the error; on a query the provider runs the predicate, and `LastOrError` is a compile error |
| `Traverse(selector)` | `IEnumerable<T>` | `Result<IReadOnlyList<TResult>, TError>`, or `Result<TError>` for a command selector; stops at the first error |
| `Traverse(selector)` | `Option<T>` | `Result<Option<TResult>, TError>`: an optional input validated when present |
| `Traverse(selector)` | `Patch<T>` | `Result<Patch<TResult>, TError>`: a patch field validated when it carries a value; a clear or an absence passes through |
| `TraverseAsync(selector)`, `TraverseAsync(selector, cancellationToken)` | `IEnumerable<T>` | as `Traverse`, one operation awaited at a time; nothing starts after an error, or once the token is cancelled |
| `Transpose()` | `Option<Result<T, TError>>`, `Result<Option<T>, TError>` | the one turned inside out: `None` is a success without a value, an error stays the error |
| `Sequence()` | `IEnumerable<Result<T, TError>>`, `IEnumerable<Result<TError>>` | the list of values, or the first error |

**Reaching the outcome**

| Member | On | Returns |
|---|---|---|
| `State` | both | `ResultState`: `Success`, `Error` or `Uninitialized` |
| `Match(onSuccess, onError)` | both | what the branch returns; `onError` may take the error or nothing |
| `TryGetValue(out value, out error)` | `Result<T, TError>` | `bool`; `value` non-null when true, `error` when false |
| `TryGetError(out error)` | `Result<TError>` | `bool` |
| implicit to `bool` | `Result<TError>` only | true for a success |
| `AsEnumerable()` | `Result<T, TError>` | zero or one element |
| `ToOption()` | `Result<T, TError>` | `Some(value)`, or `None` for an error |

**Continuing**

| Member | On | Does |
|---|---|---|
| `Map(selector)` | both | a new value on a success; `Result<T, TError>` from a `Result<TError>` |
| `Bind(selector)` | both | the result the selector returns on a success; a valued one can bind to a command |
| `Ensure(predicate, error)` | `Result<T, TError>` | a success that fails the predicate becomes that error |
| `MapError(selector)` | both | a new error type |
| `Recover(selector)` | both | the selector's result on an error, a success or another error |
| `Tap(action)`, `TapError(action)` | both | runs the action on that branch, hands the result on |
| `Zip(b, …, selector)` | `Result<T, TError>` with two to six results | one value from all, or the first error |
| `Select`, `SelectMany` | `Result<T, TError>` | query syntax over `Map` and `Bind` |
| `MapAsync`, `BindAsync`, `RecoverAsync`, `TapAsync`, `TapErrorAsync` | both, and `Task<Result<…>>` | the same, taking or continuing a task; the chain is awaited once |
| `MapErrorAsync`, `EnsureAsync` | `Task<Result<…>>`; `EnsureAsync` on `Task<Result<T, TError>>` | `MapError` and `Ensure`, once the result is known |

Equality is a record struct's. `ToString()` prints the state and never the value or the error, so a
result is safe to log; the debugger shows both.
