#if PROBE_VERSION
#error version
#endif

#if !NO_GENERATORS
using System.Text.Json;
using CodoMetis.TypeKit.ValueObjects;

var id = OrderId.From(Guid.Parse("0b9f7d3a-5c1e-4f5a-9d8e-2a7c6b4e1f03"));
var parsed = OrderId.Parse("0b9f7d3a-5c1e-4f5a-9d8e-2a7c6b4e1f03", null);
var json = JsonSerializer.Serialize(id);
var read = JsonSerializer.Deserialize<OrderId>(json);
Console.WriteLine($"From: {id}");
Console.WriteLine($"Parse equal: {parsed == id}");
Console.WriteLine($"JSON: {json}, read back equal: {read == id}");
#endif

#if DECLARE_UNION
Shape shape = new Circle(1.5);
Console.WriteLine(shape switch
{
    Circle c => $"declared union: circle {c.Radius}",
    Square s => $"declared union: square {s.Side}",
});
#endif

#if HOLD_UNION
var held = UnionLib.Pets.Adopt();
Console.WriteLine($"held: {held}");
#endif

#if MATCH_UNION
Console.WriteLine(UnionLib.Pets.Adopt() switch
{
    UnionLib.Cat c => $"matched union: cat {c.Name}",
    UnionLib.Dog d => $"matched union: dog {d.Name}",
});
#endif

#if NO_GENERATORS
Console.WriteLine("control: no TypeKit, no Metalama");
#endif

#if !NO_GENERATORS
public readonly partial record struct OrderId : IValue<Guid>;
#endif

#if DECLARE_UNION
public sealed record Circle(double Radius);

public sealed record Square(double Side);

public union Shape(Circle, Square);
#endif
