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
| CMTK0001 | Error | `default`, `default(T)`, `new T()`, `new()` and `new T { }` of a value object, an `Option`, a `Result`, or a struct marked `[RequireCustomInitialization]`. Also through a type parameter whose constraints make it one of those. |
| CMTK0002 | Error | A type implements `IValue<T>` or `IValidatedValue<,,>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so nothing is generated for it: no field, no `Value`, no factory. |
| CMTK0003 | Warning | A `Result` or `Option` that a call returns, dropped by a statement, awaited or not. |
| CMTK0004 | Error | `Materialize`, which rebuilds a value object without its rules, called or referenced anywhere but the EF Core satellite. |
| CMTK0005 | Warning | An array or span of a value object, `Option`, `Result` or `[RequireCustomInitialization]` struct created with a length, so every slot starts as `default`. |
| CMTK0006 | Warning | A field or auto-property of such a type in a class that no initializer, `required` or constructor sets. |
| CMTK0007 | Info | `FromKnownGood` given a value that comes straight from a parameter. |

The ids are public contract and never change meaning. The generators' own build errors,
CMTK1000 to CMTK1009, come from `CodoMetis.TypeKit.Generators` and are listed in its README.

## CMTK0001

A `default` value object wraps `default(T)` and passed no rule. A `default` result is neither a
success nor an error, and every branching member throws on it.

```csharp
OrderId id = default;                       // CMTK0001: create it with OrderId.From
var code = new ProductCode();               // CMTK0001: create it with Create, TryFrom or FromKnownGood
Result<Order, OrderFault> pending = new();  // CMTK0001

T Empty<T>() where T : struct, IValue<Guid> => default;   // CMTK0001, through the constraint
```

```csharp
OrderId id = OrderId.New();
var code = ProductCode.FromKnownGood("ABC");
Result<Order, OrderFault> pending = OrderFault.Empty;
```

Code inside the type itself is exempt, since its factories have to construct it. A `Nullable<T>`
of a value object is a null, not an instance, and is not reported. What the rule cannot see, such
as an array created with a length or an unassigned field of a class, is why an uninitialized
`Result` throws instead of reporting an error it never had.

To mark your own struct, whose `default` is not a valid instance:

```csharp
[RequireCustomInitialization("Use Money.Of(amount, currency)")]
public readonly record struct Money { /* … */ }
```

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

`Tap` returns its receiver unchanged, so `result.Tap(log);` on a variable is silent;
`Find(id).Tap(log);` drops the result and is reported.

## CMTK0004

`IValueObjectMaterializer<,>.Materialize` and `ValueObjectConverter<,>.Materialize` rebuild a value
object without `Create`, for rows the application stored itself. Anywhere else they let input past
the rules. The EF Core satellite's own call is compiled into the satellite, and the compiled model EF
generates in your project is generated code, which the rule skips. A test that means it suppresses the
rule where it calls it.

## CMTK0005

```csharp
var ids = new OrderId[count];                // CMTK0005: count default instances
OrderId[] kept = [.. rows.Select(r => OrderId.From(r.Id))];   // built from values
```

`stackalloc`, `GC.AllocateUninitializedArray`, `GC.AllocateArray` and `Array.Resize` are reported
too. A warning, since filling such an array in a loop straight after is correct and indistinguishable.

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
and precompiled queries, and makes System.Text.Json refuse a document without the property.

## CMTK0007

`FromKnownGood` throws on a value that breaks the rules: right for a constant, wrong for input, where a
request that fails validation becomes an exception. The rule reports a value that comes straight from
a parameter of the enclosing method or lambda (`input`, `request.Email`, `args[0]`). A value the code
produced itself is not reported. A suggestion, since a test theory's parameters are reported too.

## Configuration

Severity follows the usual `.editorconfig` mechanism, for instance
`dotnet_diagnostic.CMTK0001.severity = warning`. The rules that guard against an instance that
passed no factory or no rules are errors by default on purpose: a warning is what gets ignored. The
others are warnings or a suggestion because correct code can look the same.

Metalama runs analyzers on the source before it weaves, where `TryFrom` and `FromKnownGood` of a
value object in the same project do not exist yet. CMTK0003 and CMTK0007 recognise those calls by the
generated member's name on a value object's type, so they report in the declaring project too.

The analyzer resolves the types it looks for by symbol, so a type of your own named `IValue<T>` in
another namespace is not mistaken for ours, and a value object that implements a marker through a
derived interface is still seen.
