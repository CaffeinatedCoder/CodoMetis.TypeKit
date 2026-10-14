using System.Text.Json;
using Spike.App;

var failures = 0;
void Claim(int number, string text, bool holds)
{
    Console.WriteLine($"{(holds ? "PASS" : "FAIL")} {number,2} {text}");
    if (!holds) failures++;
}

Claim(1, $"All in declaration order: {string.Join(",", OrderStatus.All.Select(s => s.Name))}", string.Join(",", OrderStatus.All.Select(s => s.Name)) == "Open,Shipped,Cancelled");
Claim(1, $"All holds the members themselves: {ReferenceEquals(OrderStatus.All[1], OrderStatus.Shipped)}", ReferenceEquals(OrderStatus.All[1], OrderStatus.Shipped));
Claim(2, $"Name from the field, set after the static field initializers: {OrderStatus.Open.Name}, {OrderFault.Shipped.Name}", OrderStatus.Open.Name == "Open" && OrderFault.Shipped.Name == "Shipped");
Claim(2, $"QualifiedName and ToString: {OrderFault.NotFound.QualifiedName}, {OrderStatus.Cancelled}", OrderFault.NotFound.QualifiedName == "OrderFault.NotFound" && OrderStatus.Cancelled.ToString() == "Cancelled");
Claim(3, $"constant data through the author's constructor: {OrderFault.Shipped.Kind} '{OrderFault.Shipped.Remedy}' / {OrderFault.Empty.Remedy ?? "<none>"}", OrderFault.Shipped.Kind == FaultKind.Conflict && OrderFault.Empty.Remedy is null);
Claim(4, $"Match with one delegate per member: {OrderStatus.Shipped.Match(open: () => "o", shipped: () => "s", cancelled: () => "c")}", OrderStatus.Shipped.Match(() => "o", () => "s", () => "c") == "s");
Claim(5, $"TryParse/Parse by name: {OrderStatus.TryParse("Shipped", out var parsed)} {parsed?.Name}; unknown: {OrderStatus.TryParse("Nope", out _)}", OrderStatus.TryParse("Shipped", out parsed) && ReferenceEquals(parsed, OrderStatus.Shipped) && !OrderStatus.TryParse("Nope", out _));
try { OrderStatus.Parse("Nope"); Claim(5, "Parse of an unknown name throws", false); }
catch (FormatException e) { Claim(5, $"Parse of an unknown name throws FormatException naming the type, not the input: {e.Message}", e.Message.Contains("OrderStatus") && !e.Message.Contains("Nope")); }
var json = JsonSerializer.Serialize(new Holder(OrderStatus.Shipped, OrderFault.Empty));
Claim(6, $"JSON writes the name: {json}", json == """{"Status":"Shipped","Fault":"Empty"}""");
var back = JsonSerializer.Deserialize<Holder>(json)!;
Claim(6, $"and reads back the member itself: {ReferenceEquals(back.Status, OrderStatus.Shipped)} {ReferenceEquals(back.Fault, OrderFault.Empty)}", ReferenceEquals(back.Status, OrderStatus.Shipped) && ReferenceEquals(back.Fault, OrderFault.Empty));
try { JsonSerializer.Deserialize<Holder>("""{"Status":"Nope"}"""); Claim(6, "unknown name refused", false); }
catch (JsonException e) { Claim(6, $"an unknown name is refused, the input not quoted: {e.Message[..Math.Min(80, e.Message.Length)]}", e.Message.Contains("OrderStatus") && !e.Message.Contains("Nope")); }
Claim(7, $"members are singletons: {OrderStatus.Open == OrderStatus.Parse("Open")}", OrderStatus.Open == OrderStatus.Parse("Open"));
Claim(8, $"the interface is satisfied generically: {Names<OrderFault>()}", Names<OrderFault>() == "NotFound,Shipped,Empty");

static string Names<T>() where T : class, Spike.Generators.IClosedSet<T> => string.Join(",", T.All.Select(m => m.Name));

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures;

public sealed record Holder(OrderStatus Status, OrderFault Fault);
