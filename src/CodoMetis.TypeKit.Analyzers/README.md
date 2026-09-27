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

Both ids are public contract and never change meaning. The generators' own build errors,
CMTK1000 to CMTK1008, come from `CodoMetis.TypeKit.Generators` and are listed in its README.

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

## Configuration

Severity follows the usual `.editorconfig` mechanism, for instance
`dotnet_diagnostic.CMTK0001.severity = warning`. Both rules are errors by default on purpose: a
warning is what gets ignored.

The analyzer resolves the types it looks for by symbol, so a type of your own named `IValue<T>` in
another namespace is not mistaken for ours, and a value object that implements a marker through a
derived interface is still seen.
