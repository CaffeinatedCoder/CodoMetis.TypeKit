# Option

`Option<T>` is a value that may be absent. It has no `.Value`: the content is reached where
absence is handled, through `Match`, `TryGetValue` and the combinators, so a caller cannot read a
value that is not there. `T` is `notnull`, `Some(null)` throws, and a `default(Option<T>)` is `None`,
so an option that reports a value always has one. `ToString()` never prints the content. It lives in
`CodoMetis.TypeKit`; the [base README](../src/CodoMetis.TypeKit/README.md#optiont) is its reference.

The examples import `CodoMetis.TypeKit` and share a customer with an optional nickname and an optional address:

```csharp
public readonly partial record struct CustomerId : IValue<Guid>;

public sealed record Address(string Town, string Country);

public sealed record Customer(CustomerId Id, string Name, string Tier, Option<string> Nickname, Option<Address> Address);
```

## Creating one

```csharp
Option<Customer> found = customers.GetValueOrNone(ann.Id);                      // Some
Option<Customer> unknown = customers.GetValueOrNone(CustomerId.New());          // None
Option<Customer> gold = customers.Values.FirstOrNone(c => c.Tier == "gold");    // Some, the first match (or None)
Option<string> nickname = ann.Nickname;                                         // Some
Option<int> parsed = int.TryParse(text, out var number) ? Option.Some(number) : Option.None();   // None, taking its type from the conditional
Option<int> none = Option.None<int>();                                          // where nothing supplies the type
```

`Option.Some(value)` and `Option.None()` build one by hand; `None()` takes its type from where it goes,
a `return`, a conditional beside a `Some`, a parameter, and where nothing supplies one, as with `var`,
`Option.None<T>()` says it. The lookups do the rest: `GetValueOrNone(key)` on a dictionary,
`FirstOrNone()` and `LastOrNone()` on a sequence, with or without a predicate, and `ToOption()` on a
nullable, `int?` or `string?`, which turns a null into `None`. CMTK0009 points at `FirstOrDefault()`
and its kind, whose `default` for a value object or a `Result` is an instance that passed no factory.

## Reaching the value

```csharp
string greeting = found.Match(customer => $"Hello again {customer.Name}", () => "Hello");   // "Hello again Ann"
string tier = unknown.Map(customer => customer.Tier).Or("standard");                       // "standard"
int? limit = parsed.OrNull();                                                               // null, an int? for an Option<int>
int sum = found.Map(customer => customer.Name.Length).OrDefault();                          // 3, a struct's default for None
bool known = found.IsSome();                                                                // true

if (found.TryGetValue(out var customer)) visits.Add(customer.Id);   // customer is non-null in the branch

foreach (var each in unknown.AsEnumerable()) visits.Add(each.Id);   // runs once for Some, never for None
```

`Match` takes both branches and returns what they return, so it is the one call that cannot forget
absence. `Or(fallback)` and `OrDefault()` unwrap with a fallback, `OrNull()` into a nullable, `int?`
for an `Option<int>` and `string?` for an `Option<string>`. `IsSome()` and `IsNone()` answer the
question alone, for a guard or an assertion, never for a branch that then reads the value.

## Combining

```csharp
Option<decimal> discount = found.Filter(customer => customer.Tier == "gold").Map(_ => 0.1m);   // Some, Ann is gold
Option<string> country = found.Bind(customer => customer.Address).Map(address => address.Country);   // Some, the Option<Address> flattened and then mapped
Option<(string Name, string Town)> label = found.Zip(found.Bind(c => c.Address), (c, a) => (c.Name, a.Town));   // Some, only when both are
Option<Customer> seen = found.Tap(customer => visits.Add(customer.Id));   // runs for Some, hands the option on

Option<string> greeting =
    from customer in found
    from address in customer.Address            // a later from sees the earlier ones
    where address.Country == "DE"
    select $"{customer.Name} in {address.Town}";   // Some
```

`Map` changes the value and keeps absence; `Bind` takes a selector that is itself optional, so an
`Option<Option<T>>` never appears; `Filter` turns a value that fails the predicate into `None`;
`Zip` combines two to six options into one value when every one of them is `Some`; `Tap` runs an
action on a value and returns the option unchanged. `Select`, `SelectMany` and `Where` make query
syntax work, and each `from` after the first sees the earlier ranges, as above.

## At the boundary

An option is not a wire type, not a column and not a route parameter: System.Text.Json refuses it in
both directions ([JSON](json.md#option-and-result-at-the-wire)), and EF Core cannot map it
([EF Core](ef-core.md#option-and-result)). A shape that crosses a boundary says absent with `T?`, and
the option lives between the shapes.

```csharp
public sealed record CustomerDto(string Name, string? Nickname);   // the wire says absent with null

static Customer FromDto(CustomerDto dto) => new(CustomerId.New(), dto.Name, "standard", dto.Nickname.ToOption(), Option.None());

static CustomerDto ToDto(Customer customer) => new(customer.Name, customer.Nickname.OrNull());
```

```csharp
Result<Customer, LookupFault> required = customers.GetValueOrNone(CustomerId.New()).ToResult(LookupFault.Unknown);   // Error: Unknown
```

`ToOption()` on the way in and `OrNull()` on the way out; `ToResult(error)` where absence is an error
the caller has to act on, which hands the rest to [Result](result.md).

## When it is the wrong type

- The caller has to know why: a [Result](result.md) carries the fault. `Email.TryFrom(text)` answers
  valid or not; `Email.Create(text)` says which rule refused it.
- Zero to many: a collection, possibly empty, says it without a type.
- A value that is always there once set: a `required` property. An `Option` property on an entity
  or a DTO is refused by EF Core and by the serializer; a `T?` there is the shape, and `ToOption()`
  lifts it where the domain wants an option.

## The surface

Every public member, on `Option<T>`, on the static class `Option`, and as extensions in the
`CodoMetis.TypeKit` namespace. `T` is `notnull` throughout.

| Member | On | Returns |
|---|---|---|
| `Some(value)` | `Option` | `Option<T>`; throws `ArgumentNullException` for null |
| `None()` | `Option` | a `None` marker that converts to any `Option<T>` |
| `None<T>()` | `Option` | `Option<T>` |
| `ToOption()` | `T?` (a nullable struct or a reference) | `Option<T>`: `None` for null |
| `FirstOrNone()`, `FirstOrNone(predicate)` | `IEnumerable<T>` | the first element, or the first matching one; `None` for none |
| `LastOrNone()`, `LastOrNone(predicate)` | `IEnumerable<T>` | the last, likewise |
| `GetValueOrNone(key)` | `IReadOnlyDictionary<TKey, TValue>` | `Option<TValue>` |
| `IsSome()`, `IsNone()` | `Option<T>` | `bool` |
| `Match(onSome, onNone)` | `Option<T>` | what the branch returns |
| `TryGetValue(out value)` | `Option<T>` | `bool`; `value` is non-null when true |
| `Or(fallback)` | `Option<T>` | `T` |
| `OrDefault()` | `Option<T>` where `T : struct` | `T`, `default` for `None` |
| `OrNull()` | `Option<T>` | `T?`: `int?` for an `Option<int>`, `string?` for an `Option<string>` |
| `AsEnumerable()` | `Option<T>` | zero or one element |
| `Map(selector)` | `Option<T>` | `Option<TResult>`; throws if the selector returns null |
| `Bind(selector)` | `Option<T>` | the `Option<TResult>` the selector returns, or `None` |
| `Filter(predicate)` | `Option<T>` | the same option, or `None` when the predicate fails |
| `Tap(action)` | `Option<T>` | the same option, after running the action on a value |
| `Zip(b, …, selector)` | `Option<T>` with two to six options | `Option<TResult>`, `Some` only when every one is |
| `ToResult(error)` | `Option<T>` | `Result<T, TError>`: the error for `None` |
| `Select`, `SelectMany`, `Where` | `Option<T>` | query syntax over `Map`, `Bind` and `Filter` |
| implicit from `None` | `Option<T>` | `None` |

Equality is a record struct's: two options are equal when both are `None` or hold equal values.
A `default(Option<T>)` is `None`, and the analyzer still refuses writing it (CMTK0001): say
`Option.None()`.
