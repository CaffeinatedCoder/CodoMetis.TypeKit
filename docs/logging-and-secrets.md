# Logging and secrets

Some value objects hold a secret: a token, a key, a PIN. This page shows how to keep that value out
of your logs and error messages.

A secret becomes a string on its way to a log the moment it is interpolated, destructured or quoted
in an exception. Three things keep it out:

- **A value object prints only what its `ToString()` says.** No other path turns it into a string.
- **`Option` and `Result` print nothing of their content.**
- **No rejection quotes the input.** That holds for JSON, parsing, `FromKnownGood` and the type
  converter.

The [Generators README](../src/CodoMetis.TypeKit.Generators/README.md#a-value-that-must-not-be-printed)
and the [Serilog README](../src/CodoMetis.TypeKit.Serilog/README.md) are the reference. The examples
import `CodoMetis.TypeKit` and `CodoMetis.TypeKit.ValueObjects`.

## Mask a value when it is printed

Write your own `ToString()`. The generators keep it, and then generate no formatting interface. So
interpolation, `string.Format`, `Convert.ToString`, a record that holds the value object, and every
Microsoft.Extensions.Logging path, `[LogProperties]` included, all print your `ToString()`.

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

**Good to know**

- **Conversions still carry the value.** JSON, the type converter and the database column are
  conversions, not display, so they write the real value. A `Session` written as JSON carries the
  token, so the shape you log is not the shape you send.
- **The debugger shows both.** It displays `ToString()`, but you can expand `Value`.

## Log with Serilog

Serilog destructures an object, such as `{@Session}` or a record that holds a value object, by
reading its public properties. `Value` is public. So without the Serilog package, the token above is
logged as `{ Value: "tok-123" }`, and your `ToString()` is skipped.

`Destructure.WithTypeKit()` logs every value object as what it prints, masked or not. There is
nothing to register per type:

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

With it, an `OrderId` shows up in the event as `"0199aaaa-..."` rather than
`{ Value: "0199aaaa-..." }`. A list or a dictionary of value objects becomes a list or a dictionary
of strings, which is what your queries over the logs expect.

## Log with Microsoft.Extensions.Logging

Every path prints `ToString()`: a message template, the logging source generator, and
`[LogProperties]`.

```csharp
using var factory = LoggerFactory.Create(logging => logging.AddProvider(sink));   // sink: a provider of your own that keeps the formatted messages
ILogger log = factory.CreateLogger("sessions");

log.LogInformation("Opened for {Token}", token);
string message = sink.Messages.Single();   // "Opened for AccessToken(***)"
```

**Good to know**

- **Classify the parameter, not the type.** Microsoft.Extensions.Compliance classifies a logging
  method's parameter, not its type. Put a `DataClassificationAttribute` on the value object's type,
  and the logging generator reports LOGGEN035 and generates nothing for the method.

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

When a value is rejected, the message names the value object and the rule, or the underlying type
it couldn't read. It carries no inner exception that quotes the text, as the underlying type's own
`FormatException` would. So what reaches a log through this package is the type and the fault.

**Good to know**

- **The fault's text is yours.** Keep the input out of it ([Value objects](value-objects.md#errors)).
- **`FromKnownGood` names the expression you passed.** Given a literal, it names the literal, because
  the literal is the expression.
- **ASP.NET Core's own rejection of a route or query value quotes the input**, as it does for a
  `Guid`. In MVC that is the model-state message ("The value '...' is not valid."), which ends up in
  the response. In a minimal API it is the log entry and the developer exception page.
- **A body rejected by the JSON converter doesn't quote it**
  ([ASP.NET Core](aspnet-core.md#what-a-refused-value-answers)), with one exception that comes from
  System.Text.Json. A value object used as a dictionary key is read from the property name. The
  `JsonException` for a rejected or unreadable key carries that key in its `Path` (`$.items.SE`), and
  MVC uses the path as the error's name in its 400. Keep secrets out of a dictionary's keys.

## What the client sees in a fault response

When you answer with `ToHttpResult()`, the problem's `fault` field names the fault's member. It does
so for every member, whatever its status or description, because the name is meant for the client.

Some failures the client must not be able to tell apart, such as an unknown user and a wrong
password. Turn those into one fault before the response, as
[ASP.NET Core](../src/CodoMetis.TypeKit.AspNetCore/README.md#faults-a-client-must-not-tell-apart)
shows. Your domain keeps both for its own log.
