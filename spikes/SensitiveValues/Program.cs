using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SensitiveValues;
using ILogger = Microsoft.Extensions.Logging.ILogger;

const string Secret = "ann@example.com";
var email = Email.FromKnownGood(Secret);
var masked = MaskedEmail.FromKnownGood(Secret);
var classified = ClassifiedEmail.FromKnownGood(Secret);
var signup = new Signup(email, masked, "Ann");

void Show(string sink, string text) => Console.WriteLine($"  {(text.Contains(Secret) ? "LEAK" : "safe")}  {sink}: {text.ReplaceLineEndings(" ")}");

Console.WriteLine("==> formatting");
foreach (var (name, value) in new (string, object)[] { ("Email", email), ("MaskedEmail", masked), ("ClassifiedEmail", classified) })
{
    Show($"{name} ToString()", value.ToString()!);
    Show($"{name} interpolation", $"{value}");
    Show($"{name} string.Format", string.Format("{0}", value));
    Show($"{name} Convert.ToString", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!);
    Show($"{name} TypeConverter", TypeDescriptor.GetConverter(value.GetType()).ConvertToInvariantString(value)!);
    Show($"{name} IFormattable implemented", (value is IFormattable).ToString());
    Show($"{name} JSON (the wire, must carry it)", JsonSerializer.Serialize(value, value.GetType()));
}
Show("record Signup ToString()", signup.ToString());

Console.WriteLine("==> debugger (what the type declares; the debugger shows ToString() and expands public members)");
foreach (var type in new[] { typeof(Email), typeof(MaskedEmail) })
{
    var display = type.GetCustomAttribute<DebuggerDisplayAttribute>()?.Value ?? "none";
    var valueBrowsable = type.GetProperty("Value")!.GetCustomAttribute<DebuggerBrowsableAttribute>()?.State.ToString() ?? "default (shown)";
    Console.WriteLine($"  {type.Name}: [DebuggerDisplay] {display}; Value [DebuggerBrowsable] {valueBrowsable}; public Value: {type.GetProperty("Value")!.GetMethod!.IsPublic}");
}

Console.WriteLine("==> Microsoft.Extensions.Logging");
var captured = new List<string>();
var services = new ServiceCollection();
services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddProvider(new CaptureProvider(captured));
    logging.EnableRedaction();
});
services.AddRedaction(redaction => redaction.SetRedactor<ErasingRedactor>(new Microsoft.Extensions.Compliance.Classification.DataClassificationSet(Taxonomy.Private)));
using (var provider = services.BuildServiceProvider())
{
    var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Spike");
    logger.LogInformation("Template {Email} {Masked} {Classified}", email, masked, classified);
    logger.LogInformation("Template record {Signup}", signup);
    Log.SignedUp(logger, email, masked);
    Log.SignedUpClassifiedParameter(logger, email);
    Log.Properties(logger, signup);
}
foreach (var line in captured) Show(line[..line.IndexOf('|')], line[(line.IndexOf('|') + 1)..]);

Console.WriteLine("==> Microsoft.Extensions.Logging JSON console formatter");
var console = new StringWriter();
var original = Console.Out;
Console.SetOut(console);
using (var factory = LoggerFactory.Create(logging => logging.AddJsonConsole()))
{
    var logger = factory.CreateLogger("Spike");
    logger.LogInformation("Template {Email} {Masked} {Signup}", email, masked, signup);
}
Console.SetOut(original);
Show("json console", console.ToString().Trim());

Console.WriteLine("==> Serilog");
var events = new List<LogEvent>();
using (var serilog = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger())
{
    serilog.Information("Scalar {Email} {Masked}", email, masked);
    serilog.Information("Destructured {@Email} {@Masked}", email, masked);
    serilog.Information("Record {@Signup}", signup);
}
foreach (var e in events)
    Show($"serilog '{e.MessageTemplate.Text}'", $"rendered={e.RenderMessage()} properties={string.Join(", ", e.Properties.Select(p => $"{p.Key}={p.Value}"))}");

Console.WriteLine("==> Serilog with the prototype destructuring policy");
events.Clear();
using (var serilog = new LoggerConfiguration().Destructure.With<ValueObjectDestructuringPolicy>().WriteTo.Sink(new ListSink(events)).CreateLogger())
{
    serilog.Information("Destructured {@Email} {@Masked}", email, masked);
    serilog.Information("Record {@Signup}", signup);
    serilog.Information("Scalar {Masked}", masked);
}
foreach (var e in events)
    Show($"serilog+policy '{e.MessageTemplate.Text}'", $"rendered={e.RenderMessage()}");

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Email} {Masked}")]
    public static partial void SignedUp(ILogger logger, Email email, MaskedEmail masked);

#if CLASSIFIED_TYPE
    // Finding 6: LOGGEN035, and nothing is generated (CS8795).
    [LoggerMessage(Level = LogLevel.Information, Message = "Generated, classified type {Classified}")]
    public static partial void SignedUpClassifiedType(ILogger logger, ClassifiedEmail classified);
#endif

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated, classified parameter {Email}")]
    public static partial void SignedUpClassifiedParameter(ILogger logger, [PrivateData] Email email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated with properties")]
    public static partial void Properties(ILogger logger, [Microsoft.Extensions.Logging.LogProperties] Signup signup);
}

internal sealed class CaptureProvider(List<string> lines) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Capture(lines);
    public void Dispose() { }

    private sealed class Capture(List<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IEnumerable<KeyValuePair<string, object?>> list
                ? string.Join(", ", list.Select(p => $"{p.Key}={p.Value}"))
                : "";
            lines.Add($"MEL '{formatter(state, exception)[..Math.Min(40, formatter(state, exception).Length)]}…'|message={formatter(state, exception)} state=[{pairs}]");
        }
    }
}

internal sealed class ListSink(List<LogEvent> events) : ILogEventSink
{
    public void Emit(LogEvent logEvent) => events.Add(logEvent);
}
