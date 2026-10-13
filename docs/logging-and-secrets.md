# Logging and secrets

A value object over a secret, a token, a key, a PIN, is a string on its way to a log the moment it
is interpolated, destructured or quoted in an exception. Three things keep it out: a value object
prints what its `ToString()` says and nothing else reaches a string path; `Option` and `Result` print
nothing of their content; and no refusal, in JSON, parsing, `FromKnownGood` or the type converter,
quotes the input. The [Generators README](../src/CodoMetis.TypeKit.Generators/README.md#a-value-that-must-not-be-printed)
and the [Serilog README](../src/CodoMetis.TypeKit.Serilog/README.md) are the reference. The examples
import `CodoMetis.TypeKit` and `CodoMetis.TypeKit.ValueObjects`.

## A value that must not be printed

Declare `ToString()`. It is kept, and then no formatting interface is generated, so interpolation,
`string.Format`, `Convert.ToString`, a record that holds it and every Microsoft.Extensions.Logging
path reach it too, `[LogProperties]` included.

```csharp
public readonly partial record struct AccessToken : IValue<string>
{
    public override string ToString() => "AccessToken(***)";   // kept; no formatting interface is generated, so every string path reaches this
}

public sealed record Session(AccessToken Token, string Name);
```

```csharp
var token = AccessToken.From("tok-123");

string interpolated = $"token {token}";                                 // "token AccessToken(***)"
string converted = Convert.ToString(token, CultureInfo.InvariantCulture)!;   // "AccessToken(***)"
string record = new Session(token, "Ann").ToString();                   // Session { Token = AccessToken(***), Name = Ann }
string json = JsonSerializer.Serialize(token);                          // "tok-123", a conversion, not display
```

What still carries the value is what must: its JSON, the type converter and the database column,
which are conversions; and the debugger, which shows `ToString()` but expands `Value`. A `Session`
written as JSON carries the token, so the shape that is logged is not the shape that is sent.

## Serilog

Serilog destructures an object, `{@Session}` or a record holding a value object, by reading its
public properties, and `Value` is public: without the satellite the token above is logged as
`{ Value: "tok-123" }`, past the `ToString()`. `Destructure.WithTypeKit()` logs every value object as
what it prints, masked or not, and nothing is registered per type:

```csharp
using Serilog;
```

```csharp
var events = new ListLogSink();   // an ILogEventSink of your own that keeps the rendered messages
using var logger = new LoggerConfiguration()
    .Destructure.WithTypeKit()
    .WriteTo.Sink(events)
    .CreateLogger();

logger.Information("Opened {@Session}", new Session(token, "Ann"));
string logged = events.Messages.Single();   // Opened {"Token":"AccessToken(***)","Name":"Ann","$type":"Session"}
```

An `OrderId` is then `"0199aaaa-…"` in the event rather than `{ Value: "0199aaaa-…" }`, and a list
or dictionary of value objects a list or dictionary of strings, which is what the host's queries
expect.

## Microsoft.Extensions.Logging

Every path prints `ToString()`: a message template, the logging source generator, `[LogProperties]`.

```csharp
using var factory = LoggerFactory.Create(logging => logging.AddProvider(sink));   // sink: a provider of your own that keeps the formatted messages
ILogger log = factory.CreateLogger("sessions");

log.LogInformation("Opened for {Token}", token);
string message = sink.Messages.Single();   // "Opened for AccessToken(***)"
```

Microsoft.Extensions.Compliance classifies a logging method's parameter, not its type: a
`DataClassificationAttribute` on the value object's type makes the logging generator report
LOGGEN035 and generate nothing for the method. Classify the parameter.

## Refusals never quote the input

```csharp
public enum PinFault { TooShort }

public readonly partial record struct Pin : IValidatedValue<Pin, string, PinFault>
{
    public static Result<Pin, PinFault> Create(string value) =>
        value.Length >= 4 ? new Pin(value) : Result.Error(PinFault.TooShort);
}
```

```csharp
Pin.Parse("12", null);                              // FormatException naming Pin and TooShort, never the digits
JsonSerializer.Deserialize<Pin>("\"12\"");          // JsonException naming Pin and TooShort

var typed = "12";
Pin.FromKnownGood(typed);                           // InvalidOperationException naming typed and TooShort, the expression and not what it held

string printed = Option.Some(token).ToString();     // nothing of the token: an option prints no content, and neither does a result
```

A refusal names the value object and the rule, or the wrapped type it could not read, and carries no
inner exception that quotes the text: the wrapped type's own `FormatException` would. What reaches a
log through this package is therefore the type and the fault. The fault's text is yours, so keep the
input out of it ([Value objects](value-objects.md#faults)), and a `FromKnownGood` given a literal names
the literal, since the literal is the expression. ASP.NET's own 400 for a refused route value quotes
the input, as it does for a `Guid`; a body refused by the JSON converter does not
([ASP.NET Core](aspnet-core.md#what-a-refused-value-answers)).
