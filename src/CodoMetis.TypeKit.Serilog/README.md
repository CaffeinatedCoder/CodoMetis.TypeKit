# CodoMetis.TypeKit.Serilog

Serilog logs your [CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit) value objects
and closed sets as what they print, also where it destructures them. A value object that masks
itself stays masked.

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

- **Why you need it.** Serilog destructures an object (`{@Key}`, or a record holding one) by reading
  its public properties, and a value object's `Value` is public. Without this package, a value object
  that masks itself with a hand-written `ToString()` is logged unmasked there, as
  `{ Value: "k-123" }`. That is the only place it leaks. With the package, Serilog logs value objects
  as their text, as Microsoft.Extensions.Logging does.
- **Every value object becomes a string**, masked or not. An `OrderId` is `"0199aaaa-..."`, not
  `{ Value: "0199aaaa-..." }`, and a `Quantity` is `"3"`. A list or a dictionary of them becomes a
  list or a dictionary of strings.
- **A closed set is logged as its name**, or as what its own `ToString()` prints, never by its
  properties. `{@Order}` holds `Status: "Shipped"`, not the set's constant data. An instance that
  the set's own code made beside its members has no name, and Serilog logs that it could not capture
  it.
- **Nothing to register per type.** The package recognises a value object or a closed set by the
  attribute the generators put on it. So the host needs this package and the base package, not the
  generators. Serilog handles every other type as before.
- **To mask a value object, write its `ToString()`.** The
  [Generators README](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#a-value-that-must-not-be-printed)
  says what else still carries the value then.
