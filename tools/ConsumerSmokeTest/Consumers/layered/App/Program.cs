using System.Text.Json;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Serilog;
using Shop;

var id = OrderId.New();
Console.WriteLine($"guid-version={id.Value.Version}");

var line = new Line(ProductCode.FromKnownGood("ABC"), Quantity.From(3));
Console.WriteLine($"json={JsonSerializer.Serialize(line)}");
Console.WriteLine($"tryfrom={ProductCode.TryFrom("abc").Match(_ => "accepted", () => "refused")}");

try
{
    JsonSerializer.Deserialize<Line>("""{"Code":"abc","Quantity":1}""");
    Console.WriteLine("json-read=accepted");
}
catch (JsonException refusal)
{
    Console.WriteLine($"json-read=refused, names the rule: {refusal.Message.Contains(nameof(CodeFault.NotUpperCase))}");
}

Console.WriteLine($"parsed={Quantity.Parse("42", null).Value}");

// Declared here, in a project that reaches the generators only through Domain: the transitive
// fabric has to generate it, or From does not exist and this does not compile.
Console.WriteLine($"local={Sku.From("X-1").Value}");

// Serilog destructures a record holding value objects: each is logged as what it prints, a masked one
// as its mask, never through its public Value.
var events = new List<Serilog.Events.LogEvent>();
using (var log = new Serilog.LoggerConfiguration().Destructure.WithTypeKit().WriteTo.Sink(new ListSink(events)).CreateLogger())
    log.Information("{@Held}", new Held(Secret.From("hunter2"), Quantity.From(3)));
Console.WriteLine($"serilog={events.Single().Properties["Held"]}");

public sealed record Line(ProductCode Code, Quantity Quantity);

public readonly partial record struct Sku : IValue<string>;

public sealed partial record Secret : IValue<string>
{
    public override string ToString() => "***";
}

public sealed record Held(Secret Secret, Quantity Quantity);

sealed class ListSink(List<Serilog.Events.LogEvent> events) : Serilog.Core.ILogEventSink
{
    public void Emit(Serilog.Events.LogEvent logEvent) => events.Add(logEvent);
}
