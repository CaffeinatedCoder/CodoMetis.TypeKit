# Option

`Option<T>` holds a value or nothing. Use it where a value may be missing, like a customer's
nickname or the result of a lookup. The compiler then makes sure you handle the missing case before
you use the value.

There is no `.Value` to read. You reach the content through `Match`, `TryGetValue` and the other
members on this page, and each of them asks what should happen when there is nothing. An option that
says it has a value always has one: `T` is `notnull`, `Option.Some(null)` throws, and a
`default(Option<T>)` is `None`. `ToString()` never prints the content.

`Option<T>` lives in the `CodoMetis.TypeKit` namespace, and the
[base README](../src/CodoMetis.TypeKit/README.md#optiont) is its reference. The examples import that
namespace and share a customer with an optional nickname and an optional address:

```csharp
public readonly partial record struct CustomerId : IValue<Guid>;

public sealed record Address(string Town, string Country);

public sealed record Customer(CustomerId Id, string Name, string Tier, Option<string> Nickname, Option<Address> Address);
```

## Create one

```csharp
Option<Customer> found = customers.GetValueOrNone(ann.Id);                      // Some
Option<Customer> unknown = customers.GetValueOrNone(CustomerId.New());          // None
Option<Customer> gold = customers.Values.FirstOrNone(c => c.Tier == "gold");    // Some, the first match (or None)
Option<string> nickname = ann.Nickname;                                         // Some
Option<int> parsed = int.TryParse(text, out var number) ? Option.Some(number) : Option.None();   // None, taking its type from the conditional
Option<int> none = Option.None<int>();                                          // where nothing supplies the type
```

Most options come from a lookup:

- **`GetValueOrNone(key)`** on a dictionary.
- **`FirstOrNone()` and `LastOrNone()`** on a sequence, with or without a predicate.
- **`SingleOrNone()`**, which throws when there is more than one element.
- **`ElementAtOrNone(index)`**, for the element at a position.
- **`ToOption()`** on a nullable, such as an `int?` or a `string?`. A null becomes `None`.

To build one yourself, use `Option.Some(value)` and `Option.None()`. `None()` takes its type from
where it goes: a `return`, a conditional beside a `Some`, or a parameter. When nothing tells it the
type, as with `var`, write `Option.None<T>()`.

### In a database query

`FirstOrNone`, `SingleOrNone` and `ElementAtOrNone` also work on an `IQueryable<T>`. The database
provider runs the predicate and fetches only the rows they need. `LastOrNone` on a query fails to
compile, because a query has no last row until you order it.

The predicate runs in the database only when it is an expression, which a lambda written at the call
is. A `Func<T, bool>` held in a variable, or a method group, binds to the sequence overload instead.
That reads every row and checks the condition in memory, and the analyzer warns at the call
(CMTK0009). Pass an `Expression<Func<T, bool>>` there.

**Good to know**

- **Prefer these to `FirstOrDefault()`.** For a value object or a `Result`, `FirstOrDefault()` and its
  kind hand you a `default` instance that went through no factory. The analyzer points them out
  (CMTK0009).

## Get the value out

```csharp
string greeting = found.Match(customer => $"Hello again {customer.Name}", () => "Hello");   // "Hello again Ann"
string tier = unknown.Map(customer => customer.Tier).Or("standard");                       // "standard"
int? limit = parsed.OrNull();                                                               // null, an int? for an Option<int>
int sum = found.Map(customer => customer.Name.Length).OrDefault();                          // 3, a struct's default for None
bool known = found.IsSome();                                                                // true

if (found.TryGetValue(out var customer)) visits.Add(customer.Id);   // customer is non-null in the branch

foreach (var each in unknown.AsEnumerable()) visits.Add(each.Id);   // runs once for Some, never for None
```

`Match` takes one branch for a value and one for nothing, and returns what they return. It is the one
call that can't forget the missing case.

- **`Or(fallback)` and `OrDefault()`** give you the value, or a fallback when there is none.
- **`OrNull()`** gives you a nullable: an `int?` for an `Option<int>`, a `string?` for an
  `Option<string>`.
- **`TryGetValue(out value)`** fits an `if`, and the value is non-null inside the branch.
- **`AsEnumerable()`** gives you zero or one element, for a `foreach` or LINQ.
- **`IsSome()` and `IsNone()`** answer the question on its own. Use them for a guard or an assertion.
  To branch and then read the value, use `Match` or `TryGetValue`.

## Combine options

```csharp
Option<decimal> discount = found.Filter(customer => customer.Tier == "gold").Map(_ => 0.1m);   // Some, Ann is gold
Option<string> country = found.Bind(customer => customer.Address).Map(address => address.Country);   // Some, the Option<Address> flattened and then mapped
Option<(string Name, string Town)> label = found.Zip(found.Bind(c => c.Address), (c, a) => (c.Name, a.Town));   // Some, only when both are
Option<Customer> seen = found.Tap(customer => visits.Add(customer.Id));   // runs for Some, hands the option on
IEnumerable<string> nicknames = customers.Values.Choose(customer => customer.Nickname);   // "Annie", the nicknames of those who have one: a filter and a map in one
IEnumerable<Customer> known = ids.Choose(id => customers.GetValueOrNone(id));          // the customers the ids find, the unknown ones dropped

Option<string> greeting =
    from customer in found
    from address in customer.Address            // a later from sees the earlier ones
    where address.Country == "DE"
    select $"{customer.Name} in {address.Town}";   // Some
```

- **`Map`** changes the value, and a missing value stays missing.
- **`Bind`** takes a selector that returns an option itself, so you never end up with an
  `Option<Option<T>>`.
- **`Filter`** turns a value that fails the predicate into `None`.
- **`Zip`** combines two to six options into one value, when every one of them has a value.
- **`Tap`** runs an action on the value and hands the option on unchanged.
- **`Choose`** works on a sequence. It keeps, in order, the values the selector returns `Some` for, and
  drops the rest: a filter and a map in one. It runs lazily, like any LINQ operator. On a sequence of
  options, `Choose()` without a selector keeps the values they hold.

`Select`, `SelectMany` and `Where` make query syntax work, as in the last example. Each `from` after
the first can use the ones before it.

## Continue a pending lookup

A lookup that is still running is a `Task<Option<T>>`. `MapAsync`, `BindAsync`, `FilterAsync`,
`TapAsync` and `ToResultAsync` continue it the way [Result's async chains](result.md#async-chains)
continue a pending result. You await the whole chain once, at the end.

A callback in such a chain runs where the task it waits for finished. If the lookup had already
finished, the callback runs at once, on your synchronization context. If it had not, the callback runs
without that context, usually on a thread-pool thread
([Where a callback runs](result.md#where-a-callback-runs)).

In a Blazor component or a WPF window, await the lookup first, then continue with the synchronous
members:

```csharp
Option<Customer> loaded = await shop.FindAsync(ann.Id);              // the await returns to the caller's context
string shown = loaded.Map(customer => customer.Name).Or("unknown");   // "Ann", the selector on that context too
```

## At the boundary

Keep options inside your code, and use a nullable `T?` wherever data crosses a boundary. An option
is not a wire type, a column or a route parameter. System.Text.Json rejects it in both directions
([JSON](json.md#option-and-result-at-the-wire)), and EF Core can't map it
([EF Core](ef-core.md#option-and-result)).

```csharp
public sealed record CustomerDto(string Name, string? Nickname);   // the wire says absent with null

static Customer FromDto(CustomerDto dto) => new(CustomerId.New(), dto.Name, "standard", dto.Nickname.ToOption(), Option.None());

static CustomerDto ToDto(Customer customer) => new(customer.Name, customer.Nickname.OrNull());
```

Use `ToOption()` on the way in and `OrNull()` on the way out. The option lives between the two
shapes.

```csharp
Result<Customer, LookupFault> required = customers.GetValueOrNone(CustomerId.New()).ToResult(LookupFault.Unknown);   // Error: Unknown
```

When a missing value is an error the caller has to act on, `ToResult(error)` turns the option into a
[Result](result.md), and the Result page takes it from there.

## When to use something else

- **The caller needs to know why.** Use a [Result](result.md), which carries the error.
  `Email.TryFrom(text)` tells you whether the text is valid. `Email.Create(text)` tells you which
  rule it broke.
- **Zero to many values.** Use a collection. An empty one says "none" without a special type.
- **A value that is always there once it is set.** Use a `required` property.
- **A property of an entity or a DTO.** Use `T?`. EF Core and the serializer both reject an `Option`
  property there. Call `ToOption()` where your domain code wants an option.

## The surface

This table lists every public member: on `Option<T>`, on the static class `Option`, and the
extensions in the `CodoMetis.TypeKit` namespace. `T` is `notnull` throughout, with one exception: the
elements `Choose(selector)` reads may be null, and only what the selector finds counts as a value.

| Member | On | Returns |
|---|---|---|
| `Some(value)` | `Option` | `Option<T>`; throws `ArgumentNullException` for null |
| `None()` | `Option` | a `None` marker that converts to any `Option<T>` |
| `None<T>()` | `Option` | `Option<T>` |
| `ToOption()` | `T?` (a nullable struct or a reference) | `Option<T>`: `None` for null |
| `FirstOrNone()`, `FirstOrNone(predicate)` | `IEnumerable<T>`, `IQueryable<T>` | the first element, or the first matching one; `None` for none. On a query the provider runs the predicate and returns one row, when the predicate is an expression; a `Func` variable or a method group reads every row |
| `LastOrNone()`, `LastOrNone(predicate)` | `IEnumerable<T>` | the last, likewise; a compile error on a query, which has no last row until it is ordered |
| `SingleOrNone()`, `SingleOrNone(predicate)` | `IEnumerable<T>`, `IQueryable<T>` | the only element, or `None` for none; throws for more than one. A query is asked for two rows |
| `ElementAtOrNone(index)` | `IEnumerable<T>` (an `int` or an `Index`), `IQueryable<T>` (an `int`) | the element at the index, or `None` past either end; an `Index` on a query is a compile error |
| `GetValueOrNone(key)` | `IReadOnlyDictionary<TKey, TValue>` | `Option<TValue>` |
| `Choose(selector)` | `IEnumerable<T>`, its elements nullable or not | the values the selector answers `Some` for, in order and lazily: a filter and a map in one |
| `Choose()` | `IEnumerable<Option<T>>` | the values of the options that hold one, in order and lazily |
| `IsSome()`, `IsNone()` | `Option<T>` | `bool` |
| `Match(onSome, onNone)` | `Option<T>` | what the branch returns |
| `TryGetValue(out value)` | `Option<T>` | `bool`; `value` is non-null when true |
| `Or(fallback)` | `Option<T>` | `T` |
| `OrDefault()` | `Option<T>` where `T : struct` | `T`, `default` for `None`; CMTK0009 for a value object, whose `default` is no value |
| `OrNull()` | `Option<T>` | `T?`: `int?` for an `Option<int>`, `string?` for an `Option<string>` |
| `AsEnumerable()` | `Option<T>` | zero or one element |
| `Map(selector)` | `Option<T>` | `Option<TResult>`; throws if the selector returns null |
| `Bind(selector)` | `Option<T>` | the `Option<TResult>` the selector returns, or `None` |
| `Filter(predicate)` | `Option<T>` | the same option, or `None` when the predicate fails |
| `Tap(action)` | `Option<T>` | the same option, after running the action on a value |
| `Zip(b, ..., selector)` | `Option<T>` with two to six options | `Option<TResult>`, `Some` only when every one is |
| `ToResult(error)` | `Option<T>` | `Result<T, TError>`: the error for `None` |
| `MapAsync(selector)`, `BindAsync(selector)`, `TapAsync(action)` | `Option<T>`, `Task<Option<T>>` | the same over a callback that returns a `Task`, or continuing a pending option; the chain is awaited once |
| `FilterAsync(predicate)` | `Task<Option<T>>` | `Filter`, once the option is known |
| `ToResultAsync(error)` | `Task<Option<T>>` | `Task<Result<T, TError>>`: a pending lookup into a `Result` pipeline |
| `Select`, `SelectMany`, `Where` | `Option<T>` | query syntax over `Map`, `Bind` and `Filter` |
| implicit from `None` | `Option<T>` | `None` |

Two options are equal when both are `None`, or when both hold equal values, as with any record
struct. A `default(Option<T>)` is `None`, but the analyzer still rejects writing it (CMTK0001). Write
`Option.None()` instead.
