using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
// sample: logging-and-secrets/serilog-using
using Serilog;
// end sample
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ReadmeSamples.Docs.Logging;

// sample: logging-and-secrets/token
public readonly partial record struct AccessToken : IValue<string>
{
    public override string ToString() => "AccessToken(***)";   // kept; no formatting interface is generated, so every string path reaches this
}

public sealed record Session(AccessToken Token, string Name);
// end sample

// sample: logging-and-secrets/pin
public enum PinFault { TooShort }

public readonly partial record struct Pin : IValidatedValue<Pin, string, PinFault>
{
    public static Result<Pin, PinFault> Create(string value) =>
        value.Length >= 4 ? new Pin(value) : Result.Error(PinFault.TooShort);
}
// end sample

/// <summary>A Serilog sink that keeps every message rendered as the console sink would (<c>{Message:lj}</c>): what the Serilog sample reads back.</summary>
public sealed class ListLogSink : ILogEventSink
{
    private readonly MessageTemplateTextFormatter formatter = new("{Message:lj}", null);

    public List<string> Messages { get; } = [];

    public void Emit(LogEvent logEvent)
    {
        var rendered = new StringWriter();
        formatter.Format(logEvent, rendered);
        Messages.Add(rendered.ToString());
    }
}

/// <summary>A logger provider that keeps every formatted message: what the Microsoft.Extensions.Logging sample reads back.</summary>
public sealed class ListSink : ILoggerProvider, ILogger
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));

    public void Dispose()
    {
    }
}

public static class Usage
{
    public static (string Interpolated, string Converted, string Record, string Json) Printing()
    {
        // sample: logging-and-secrets/printing
        var token = AccessToken.From("tok-123");

        string interpolated = $"token {token}";                                 // "token AccessToken(***)"
        string converted = Convert.ToString(token, CultureInfo.InvariantCulture)!;   // "AccessToken(***)"
        string record = new Session(token, "Ann").ToString();                   // Session { Token = AccessToken(***), Name = Ann }
        string json = JsonSerializer.Serialize(token);                          // "tok-123", a conversion, not display
        // end sample

        return (interpolated, converted, record, json);
    }

    public static string Serilog(AccessToken token)
    {
        // sample: logging-and-secrets/serilog
        var events = new ListLogSink();   // an ILogEventSink of your own that keeps the rendered messages
        using var logger = new LoggerConfiguration()
            .Destructure.WithTypeKit()
            .WriteTo.Sink(events)
            .CreateLogger();

        logger.Information("Opened {@Session}", new Session(token, "Ann"));
        string logged = events.Messages.Single();   // Opened {"Token":"AccessToken(***)","Name":"Ann","$type":"Session"}
        // end sample

        return logged;
    }

    public static string Mel(AccessToken token, ListSink sink)
    {
        // sample: logging-and-secrets/mel
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(sink));   // sink: a provider of your own that keeps the formatted messages
        ILogger log = factory.CreateLogger("sessions");

        log.LogInformation("Opened for {Token}", token);
        string message = sink.Messages.Single();   // "Opened for AccessToken(***)"
        // end sample

        return message;
    }

    public static string Refusals(AccessToken token)
    {
        // sample: logging-and-secrets/refusals
        Pin.Parse("12", null);                              // FormatException naming Pin and TooShort, never the digits
        JsonSerializer.Deserialize<Pin>("\"12\"");          // JsonException naming Pin and TooShort

        var typed = "12";
        Pin.FromKnownGood(typed);                           // InvalidOperationException naming typed and TooShort, the expression and not what it held

        string printed = Option.Some(token).ToString();     // nothing of the token: an option prints no content, and neither does a result
        // end sample

        return printed;
    }
}

public sealed class LoggingTests
{
    private const string Readme = "docs/logging-and-secrets.md";

    private static string Quoted(string text) => $"\"{text}\"";

    [Fact]
    public void Every_string_path_prints_the_mask_and_JSON_carries_the_value()
    {
        var (interpolated, converted, record, json) = Usage.Printing();
        var token = AccessToken.From("tok-123");

        $"token {token}".ShouldBeShownIn(Readme, Quoted);
        Convert.ToString(token, CultureInfo.InvariantCulture)!.ShouldBeShownIn(Readme, Quoted, "Convert.ToString(token, CultureInfo.InvariantCulture)!");
        new Session(token, "Ann").ToString().ShouldBe(SampleOutputs.Shown(Readme, "new Session(token, \"Ann\").ToString()"));
        JsonSerializer.Serialize(token).ShouldBeShownIn(Readme, text => text);
        (interpolated, converted, record, json).ShouldBe(("token AccessToken(***)", "AccessToken(***)", "Session { Token = AccessToken(***), Name = Ann }", "\"tok-123\""));
    }

    [Fact]
    public void Serilog_logs_the_mask_where_it_destructures()
    {
        var logged = Usage.Serilog(AccessToken.From("tok-123"));

        logged.ShouldBe(SampleOutputs.Shown(Readme, "events.Messages.Single()"));
        logged.ShouldNotContain("tok-123");
    }

    [Fact]
    public void Microsoft_Extensions_Logging_prints_the_mask()
    {
        var message = Usage.Mel(AccessToken.From("tok-123"), new ListSink());

        message.ShouldBeShownIn(Readme, Quoted, "sink.Messages.Single()");
        message.ShouldNotContain("tok-123");
    }

    [Fact]
    public void No_refusal_quotes_the_input()
    {
        var typed = "12";

        SampleOutputs.ShouldRefuseAsShownIn(() => Pin.Parse("12", null), Readme, input: "12");
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Pin>("\"12\""), Readme, input: "12");
        SampleOutputs.ShouldRefuseAsShownIn(() => Pin.FromKnownGood(typed), Readme, input: "12");
        Should.Throw<FormatException>(() => Usage.Refusals(AccessToken.From("tok-123")));

        Option.Some(AccessToken.From("tok-123")).ToString().ShouldNotContain("tok-123");
        Result<AccessToken, PinFault>.Success(AccessToken.From("tok-123")).ToString().ShouldNotContain("tok-123");
    }
}
