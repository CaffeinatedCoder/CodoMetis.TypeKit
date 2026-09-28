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
| CMTK0001 | Error | `default`, `default(T)`, `new T()`, `new()` and `new T { }` of a value object, an `Option`, a `Result`, or a struct marked `[RequireCustomInitialization]`. Also through a type parameter whose constraints make it one of those. A default that is only compared (`id == default`, `id.Equals(default)`) is a guard and is not reported. |
| CMTK0002 | Error | A type implements `IValue<T>` or `IValidatedValue<,,>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so nothing is generated for it: no field, no `Value`, no factory. |
| CMTK0003 | Warning | A `Result` or `Option` that a call returns, dropped by a statement, awaited or not, or by converting its `Task` to a plain `Task`. |
| CMTK0004 | Error | `Materialize`, which rebuilds a value object without its rules, called or referenced anywhere but the EF Core satellite. |
| CMTK0005 | Warning | An array or span of a value object, `Option`, `Result` or `[RequireCustomInitialization]` struct created with a length, so every slot starts as `default`. A constant length of zero is not reported. |
| CMTK0006 | Warning | A field or auto-property of such a type in a class that no initializer, `required` or constructor sets, or a `required` one that a `[SetsRequiredMembers]` constructor does not set. |
| CMTK0007 | Info | `FromKnownGood` given a value that comes straight from a parameter. |
| CMTK0008 | Warning | The wrapped values of two different value objects compared, `order.CustomerId.Value == product.Id.Value`, or the two value objects themselves through `Equals(object)`. |
| CMTK0009 | Warning | A call that returns the `default` of such a type when it finds nothing: `FirstOrDefault()`, `GetValueOrDefault()`, `Option<T>.OrDefault()` and their kind. |

The ids are public contract and never change meaning. The generators' own build errors,
CMTK1000 to CMTK1011, come from `CodoMetis.TypeKit.Generators` and are listed in its README.

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

A default that is only compared is a guard against exactly those, and is not reported:

```csharp
if (id == default) throw new ArgumentException("An order id is required.", nameof(id));
if (option != default) { /* … */ }
if (id.Equals(default(OrderId)) || EqualityComparer<OrderId>.Default.Equals(id, default)) { /* … */ }
```

That is an operand of `==` or `!=`, or an argument of a call named `Equals`. Anything else, such as
`id == default ? default : id`, still reports the second `default`.

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
an argument (`Task.WhenAny(orders.CancelAsync(id), timeout)`). A task you still hold is not dropped,
and an explicit `(Task)` cast says the drop is intended. A `ValueTask<T>` has no conversion to
`ValueTask`.

`Tap`, `TapAsync` and `TapError` return their receiver unchanged, and so do the `Task`
continuations `TapAsync` and `TapErrorAsync`, so `result.Tap(log);` and `await pending.TapAsync(log);`
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
| `Enumerable.FirstOrDefault`, `LastOrDefault` | `FirstOrNone`, `LastOrNone`, which return an `Option`, or the overload that takes a default value |
| `Enumerable.SingleOrDefault`, `DefaultIfEmpty` | the overload that takes a default value |
| `Enumerable.ElementAtOrDefault` | `Skip(index).FirstOrNone()` |
| The same on `Queryable` | select a nullable first, `Select(x => (OrderId?)x).FirstOrDefault()`, which a query provider translates |
| `Nullable<T>.GetValueOrDefault()` | `GetValueOrDefault(fallback)`, or check `HasValue` |
| `dictionary.GetValueOrDefault(key)` | `GetValueOrNone(key)`, which returns an `Option`, or pass a default value |
| `Activator.CreateInstance<T>()`, `Activator.CreateInstance(typeof(T))`, `RuntimeHelpers.GetUninitializedObject(typeof(T))` | one of the type's factories |
| `Option<T>.OrDefault()` | `Or(fallback)` or `Match`, which say what `None` becomes |

The element type is judged as CMTK0001 judges a `default`: a value object, `Option`, `Result`,
`[RequireCustomInitialization]` struct, or a type parameter constrained to be one. An overload given
a default value, and an element type whose default is null or an ordinary value, are not reported.
A warning, since code that checks the sequence is not empty first is correct and the rule cannot tell.

## Configuration

Severity follows the usual `.editorconfig` mechanism, for instance
`dotnet_diagnostic.CMTK0001.severity = warning`. No `.editorconfig` section reaches the C# generated
for `.razor` and `.cshtml` files, not `[*.cs]`, not `[*.razor]`, not even `[*]`. To set a severity
there too, put the line in a `.globalconfig` file, which applies to every file of the project.

The errors are the rules where the code is wrong whatever surrounds it: a `default` written out
(CMTK0001), a value object nothing generates (CMTK0002), and validation bypassed (CMTK0004). A
warning is what gets ignored, so these fail the build. The others are warnings, or for CMTK0007 Info,
because correct code can look the same: an array filled in a loop straight after it is created
(CMTK0005), a member a framework sets (CMTK0006), a sequence known not to be empty (CMTK0009), a
drop that nothing depends on (CMTK0003), or a comparison of two identities that really are shared
(CMTK0008).

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
