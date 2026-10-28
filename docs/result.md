# Result

A `Result` tells you how an operation went. `Result<T, TError>` holds a value, or an error of a type
you choose. `Result<TError>` is for a command that produces no value: it holds a success or an error.

Neither has a `.Value` or an `.Error`. You reach the outcome through `Match`, `TryGetValue` and the
other members on this page, so you handle a failure where you use the value. Both types live in the
`CodoMetis.TypeKit` namespace, and the
[base README](../src/CodoMetis.TypeKit/README.md#resultt-terror-and-resultterror) is their reference.
When a value can be missing for no particular reason, use an [Option](option.md) instead.

The examples import `CodoMetis.TypeKit` and follow a checkout: find a cart, reserve its lines
against stock, and charge for them.

```csharp
public readonly partial record struct CartId : IValue<Guid>;

public readonly partial record struct OrderId : IValue<Guid>;

public enum CheckoutFault { CartUnknown, CartEmpty, OutOfStock, PaymentRefused }

public sealed record Line(string Sku, int Quantity, decimal Price);

public sealed record Cart(CartId Id, IReadOnlyList<Line> Lines);

public sealed record Reservation(OrderId Id, decimal Total);

public sealed record Receipt(OrderId Id, decimal Total);
```

## Pick a shape

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

Return a bare value for a success. Wrap an error in `Result.Error(...)`, because in a
`Result<long, int>` a bare `5` could be either. A `Result<TError>` has no value to confuse it with,
so there a bare error converts on its own, and so does the `Result.Success()` marker.

You choose the error type, which TypeKit calls a fault. Pick the simplest one that fits:

- **An enum** for most operations.
- **A closed set** when each fault carries constant data.
- **A `Fault<TFault>`** when one occurrence carries a sentence of its own.
- **A `Fault<T1, T2>`** when a boundary answers with its own faults and with the ones shared code
  decides.
- **A record** when a fault carries the rule's data ([Value objects](value-objects.md#errors)).

`ToHttpResult()` accepts the first four. Keep the input out of a fault, because a fault ends up in
messages and logs.

## Get the outcome out

```csharp
var reserved = checkout.Reserve(cart);

string outcome = reserved.Match(reservation => $"reserved {reservation.Total}", fault => $"refused: {fault}");   // "reserved 30"
ResultState state = reserved.State;                                                                                 // Success

if (reserved.TryGetValue(out var reservation, out _) && checkout.Charge(reservation.Total)) audit.Add("charged");   // a Result<TError> converts to bool; a valued one never does

Option<Reservation> maybe = reserved.ToOption();   // the fault dropped, where only presence matters
```

`Match` takes one branch for a success and one for an error, and returns what they return.
`TryGetValue(out value, out error)` gives you the branch and the content in one call. `value` is
non-null when it returns true, and `error` is non-null when it returns false.

Only `Result<TError>` converts to `bool`, so `if (checkout.Charge(total))` reads as it should. A
result with a value doesn't convert. Otherwise `if (await users.IsEmailTakenAsync(email))` over a
`Result<bool, DbFault>` would take the branch for a success that holds `false`.

`State` tells the three states apart without throwing: `Success`, `Error`, and `Uninitialized`.

**Good to know**

- **A `default` result is uninitialized.** An array slot or an unassigned field still produces one.
  Every branching member throws on it, rather than inventing a `default(TError)`.
- **The analyzer points out where they come from.** It reports an array of results created with a
  length (CMTK0005), and a `default` written in your code (CMTK0001).

## Chain steps

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

Each step runs on one branch and passes the other one through unchanged:

- **On a success:** `Map`, `Bind`, `Tap` and `Ensure`. An error passes through them.
- **On an error:** `MapError`, `TapError` and `Recover`. A success passes through them.

The first error stops the chain, and every step after it is skipped. The `empty` line shows this:
`Reserve` never ran.

`Recover` is the error branch's `Bind`. Its selector returns a success or another error, which gives
the operation a second chance:

```csharp
Result<CheckoutFault> settled = charged.Recover(fault => fault == CheckoutFault.PaymentRefused ? checkout.Invoice(cartId) : fault);   // Success
```

**Good to know**

- **A lambda that mixes a bare value and an error needs its type argument.** C# infers a lambda's
  return type from its body alone. So a lambda that returns a bare value on one branch and
  `Result.Error(...)` on the other needs one:
  `.Bind<Receipt>(r => r.Total > 0 ? r : Result.Error(CheckoutFault.CartEmpty))`.
  `.Ensure(r => r.Total > 0, CheckoutFault.CartEmpty)` says the same without one.

## Translate errors between layers

A fault names what the operation found. What a client is told is up to the host. `MapError` is where
you translate one into the other:

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

In ASP.NET Core, `ToHttpResult()` turns the result into a response. The fault becomes problem
details, with the status the host maps it to ([ASP.NET Core](aspnet-core.md#a-result-as-a-response)).

## Async chains

The asynchronous members continue a `Task<Result<...>>` without an `await` at each step. You await
the chain once, at its end:

```csharp
Result<CheckoutFault> chargedAsync = await checkout.FindCartAsync(cartId)        // Task<Result<Cart, CheckoutFault>>
    .EnsureAsync(cart => cart.Lines.Count > 0, CheckoutFault.CartEmpty)          // a rule on a pending result
    .BindAsync(cart => checkout.ReserveAsync(cart))                              // an asynchronous step
    .MapAsync(reservation => reservation.Total)                                  // a synchronous step on a pending result
    .BindAsync(total => checkout.ChargeAsync(total))                             // a command: Result<CheckoutFault> remains
    .TapErrorAsync(fault => audit.Add($"not charged: {fault}"));

string asyncLedger = chargedAsync.Match(() => "charged", fault => $"not charged: {fault}");   // "charged"
```

The asynchronous steps take callbacks that return a `Task`, and wait for them. So whatever a callback
throws comes out of the chain's `await`.

**Good to know**

- **A callback that returns a `ValueTask` needs `.AsTask()` or an `async` lambda.** Otherwise
  `TapAsync` and `TapErrorAsync` bind to their synchronous overload and don't wait, and `MapAsync`
  keeps the `ValueTask` itself as the value.

## Where a callback runs

Every `await` inside the package uses `ConfigureAwait(false)`. Nothing resumes on your
`SynchronizationContext`. If you block on a chain with `.Result` or `.Wait()`, the package's own
awaits don't deadlock you.

As a result, a callback runs where the task it waits for finished:

- **The task had already finished.** The callback runs at once, on your thread and context.
- **The task was still running.** The callback runs wherever the task finishes, usually on a
  thread-pool thread, without your context.

That holds for every continuation of a pending result, for `TraverseAsync` after its first
operation, and for every step after one that waited. Your own `await` of the chain returns to your
context as always.

In a Blazor component or a WPF window, a callback that touches state bound to the renderer or the
dispatcher would then work or fail depending on timing. There, await the task and continue with the
synchronous members, or marshal inside the callback (`InvokeAsync`, `Dispatcher.Invoke`):

```csharp
Result<Cart, CheckoutFault> found = await checkout.FindCartAsync(cartId);   // the await returns to the caller's context
string lineCount = found.Map(cart => $"{cart.Lines.Count} lines").Match(text => text, fault => $"refused: {fault}");   // "2 lines", the selector on that context too
```

## Collect many results

```csharp
Result<IReadOnlyList<Line>, CheckoutFault> lines = requested.Traverse(checkout.ToLine);   // every request to a line, or the first fault
Result<IReadOnlyList<Reservation>, CheckoutFault> all = reservations.Sequence();          // a list of results to a result of a list
Result<decimal, CheckoutFault> amount = unitPrice.Zip(quantity, (price, count) => price * count);   // both successes, or the first error
Result<Line, CheckoutFault> bulk = cart.Lines.FirstOrError(line => line.Quantity >= 10, CheckoutFault.OutOfStock);   // the first match, or the error

string summary = requested.Traverse(checkout.ToLine).Match(list => $"{list.Count} lines", fault => $"refused: {fault}");   // "2 lines"
string stopped = refused.Traverse(checkout.ToLine).Match(list => $"{list.Count} lines", fault => $"refused: {fault}");    // "refused: OutOfStock"
```

`Traverse`, `Sequence` and `Zip` (two to six results) stop at the first error, in the order of the
sequence or the arguments. `Traverse` doesn't call its selector for anything after that error.

None of them collects errors. To return several faults together, make the fault type a list of your
own.

`TraverseAsync` runs an asynchronous operation one element at a time. It awaits each operation before
it starts the next, so nothing starts after a fault. Give it a cancellation token, and it checks the
token before each operation and hands it on. A cancellation propagates as an exception and doesn't
become an error. When the operations may all run at once, use `Task.WhenAll` over their tasks and
`Sequence()` over what it returns.

On an `Option` or a `Patch`, `TraverseAsync` runs the operation once, when there is a value. `None`,
a clear and an absent field call nothing and pass through.

```csharp
Result<IReadOnlyList<Reservation>, CheckoutFault> reserved = await carts.TraverseAsync(checkout.ReserveAsync);     // one at a time, in order; nothing starts after a fault
Result<CheckoutFault> charged = await totals.TraverseAsync(checkout.ChargeAsync);                                   // a command per element, likewise
Result<IReadOnlyList<Cart>, CheckoutFault> found = await ids.TraverseAsync(checkout.LoadCartAsync, cancellationToken);   // the token checked before each, and handed to it
Result<Option<Cart>, CheckoutFault> saved = await savedCart.TraverseAsync(checkout.FindCartAsync);           // None calls nothing; a value is looked up
Result<Patch<Cart>, CheckoutFault> moved = await movedTo.TraverseAsync(checkout.FindCartAsync);              // a clear or an absence passes through
```

## An exception as an outcome

Without help, the only way from a call that throws into a `Result` is a `try`/`catch` you write
yourself. `Result.Catch` runs the call and gives you its value, or the exception as the error:

```csharp
Result<Uri, Exception> url = Result.Catch(() => new Uri(text));                                        // Error: UriFormatException
Result<Uri, UriFormatException> narrowed = Result.Catch<Uri, UriFormatException>(() => new Uri(text));   // that type only; any other propagates
Result<Uri, ApiFault> asFault = Result.Catch(() => new Uri(text)).MapError(_ => ApiFault.Invalid);     // Error: Invalid
Result<Exception> written = Result.Catch(() => File.WriteAllText(path, "receipt"));                     // Success, a command
Result<string, Exception> read = await Result.CatchAsync(() => File.ReadAllTextAsync(path));           // Success: receipt
```

An exception is for the code that called, not for a client. `ToHttpResult()` doesn't accept a
`Result<T, Exception>`, so turn the exception into a fault with `MapError` first, as `asFault` does.

**Good to know**

- **Name both type arguments to narrow.** `Catch<T, TException>` spells both, since C# infers all
  type arguments or none.
- **Use `CatchAsync` for a task.** It awaits the task, so an exception thrown after the task's first
  `await` is the error too. A `ValueTask` works the same way: `Result.CatchAsync(() => stream.ReadAsync(buffer))`. Passing a delegate that returns a `Task` to `Catch` fails to compile, and
  the error names `CatchAsync`. The task's exception would otherwise be thrown after `Catch` had
  returned.
- **A cancellation is not an outcome.** An `OperationCanceledException` passes through every
  overload. So does an `AggregateException` that holds only cancellations, as `Wait()` and `.Result`
  throw for a cancelled task. An `AggregateException` that holds anything else is the error, as it was
  thrown.
- **A timeout is an error.** A cancellation over a `TimeoutException` is the error, wrapped or not.
  That is how `HttpClient` reports that its `Timeout` elapsed and the call failed. `.Result` on such
  a call gives the error too.
- **The other members don't catch.** `Map`, `Bind` and the rest are unchanged: a selector that throws
  propagates.

`CatchAsync`'s error is what `await` throws: the exception `try { await task; } catch (Exception e)`
would catch. A task that faulted with several exceptions throws only the first of them. `Task.WhenAll`
does this when more than one operation fails, and a narrowed `CatchAsync` looks at that first one
alone. The task's `Exception` holds them all, so when every failure matters, keep hold of the task:

```csharp
Task both = Task.WhenAll(File.WriteAllTextAsync(draft, "a"), File.WriteAllTextAsync(copy, "b"));
Result<Exception> saved = await Result.CatchAsync(() => both);   // Error: DirectoryNotFoundException, the first of the two
int failures = both.Exception?.InnerExceptions.Count ?? 0;        // 2, every failure is on the task
```

## Use query syntax

```csharp
Result<Receipt, CheckoutFault> receipt =
    from cart in checkout.FindCart(cartId)
    from reservation in checkout.Reserve(cart)       // each from sees the earlier ranges
    select new Receipt(reservation.Id, reservation.Total);
```

`Select` and `SelectMany` make query syntax work. There is no `where`: a result has no error to give
a value that is filtered out. Use `Ensure(predicate, error)` for that.

## The surface

These tables list every public member of `Result<T, TError>`, `Result<TError>`, the static class
`Result`, and the extensions in the `CodoMetis.TypeKit` namespace. `T` and `TError` are `notnull`
throughout.

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
| `CatchAsync(action)` and the same type-argument forms | `Result` | the same, awaiting the `Task` or `ValueTask` |
| `ToResult(error)` | `Option<T>` | `Result<T, TError>` |
| `FirstOrError(predicate, error)`, `LastOrError(predicate, error)` | `IEnumerable<T>`; `FirstOrError` on `IQueryable<T>` too | the first or last match, or the error; on a query the provider runs the predicate, and `LastOrError` is a compile error |
| `Traverse(selector)` | `IEnumerable<T>` | `Result<IReadOnlyList<TResult>, TError>`, or `Result<TError>` for a command selector; stops at the first error |
| `Traverse(selector)` | `Option<T>` | `Result<Option<TResult>, TError>`: an optional input validated when present |
| `Traverse(selector)` | `Patch<T>` | `Result<Patch<TResult>, TError>`: a patch field validated when it carries a value; a clear or an absence passes through |
| `TraverseAsync(selector)`, `TraverseAsync(selector, cancellationToken)` | `IEnumerable<T>` | as `Traverse`, one operation awaited at a time; nothing starts after an error, or once the token is cancelled |
| `TraverseAsync(selector)`, `TraverseAsync(selector, cancellationToken)` | `Option<T>`, `Patch<T>` | as `Traverse`, the operation awaited; nothing is called without a value |
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
| `Zip(b, ..., selector)` | `Result<T, TError>` with two to six results | one value from all, or the first error |
| `Select`, `SelectMany` | `Result<T, TError>` | query syntax over `Map` and `Bind` |
| `MapAsync`, `BindAsync`, `RecoverAsync`, `TapAsync`, `TapErrorAsync` | both, and `Task<Result<...>>` | the same, taking or continuing a task; the chain is awaited once |
| `MapErrorAsync`, `EnsureAsync` | `Task<Result<...>>`; `EnsureAsync` on `Task<Result<T, TError>>` | `MapError` and `Ensure`, once the result is known |

Equality works as for any record struct. `ToString()` prints the state and never the value or the
error, so a result is safe to log. The debugger shows both.
