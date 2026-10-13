# CodoMetis.TypeKit.Serilog

Serilog integration for [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit) value
objects and closed sets: each is logged as what it prints, also where Serilog destructures it.

```bash
dotnet add package CodoMetis.TypeKit.Serilog
```

It needs Serilog 4.0.0 or later.

```csharp
using Serilog;

using var logger = new LoggerConfiguration()
    .Destructure.WithTypeKit()
    .WriteTo.Console()
    .CreateLogger();

logger.Information("Created {@Client}", new Client(ApiKey.From("k-123"), "Ann"));   // Created {"Key": "ApiKey(***)", "Name": "Ann", "$type": "Client"}
```

- **Why.** Serilog destructures an object (`{@Key}`, or a record holding one) by reading its public
  properties, and a value object's `Value` is public: without this, a value object that masks itself
  with a hand-written `ToString()` is logged unmasked there, as `{ Value: "k-123" }`, and nowhere else.
  With it, Serilog logs value objects as Microsoft.Extensions.Logging does, as their text.
- **Every value object**, masked or not, is then a string in the event: an `OrderId` is
  `"0199aaaa-…"`, not `{ Value: "0199aaaa-…" }`, and a `Quantity` is `"3"`. A list or dictionary of them
  is a list or dictionary of strings.
- **A closed set** is logged as its name, or as what its own `ToString()` prints, never by its
  properties: `{@Order}` holds `Status: "Shipped"`, not the set's constant data. An instance the
  set's own code made beside its members has no name, and Serilog logs that it could not capture it.
- **Nothing per type.** A value object or a closed set is recognised by the attribute the generators
  put on it, so the host needs this package and the base package, not the generators. Other types
  are left to Serilog.
- To mask a value object, declare `ToString()`; the
  [Generators README](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#a-value-that-must-not-be-printed)
  says what else then still carries the value.
