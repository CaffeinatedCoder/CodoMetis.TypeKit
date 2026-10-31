#if PROBE_VERSION
#error version
#endif

using System.Text.Json;
using CodoMetis.TypeKit.ValueObjects;

var target = AppContext.TargetFrameworkName;
var id = OrderId.From(Guid.Parse("0b9f7d3a-5c1e-4f5a-9d8e-2a7c6b4e1f03"));
var json = JsonSerializer.Serialize(id);
Console.WriteLine($"{target}: From {id}, Parse equal {OrderId.Parse(id.ToString(), null) == id}, JSON {json} read back equal {JsonSerializer.Deserialize<OrderId>(json) == id}");

#if IF_UNION && NET11_0_OR_GREATER
Pet pet = new Cat("Tom");
Console.WriteLine(pet switch
{
    Cat c => $"{target}: union under #if: cat {c.Name}",
    Dog d => $"{target}: union under #if: dog {d.Name}",
});
#endif

#if EXCLUDED_FILE && NET11_0_OR_GREATER
Console.WriteLine($"{target}: union in a net11.0-only file: {Shapes.Describe(new Circle(1.5))}");
#endif

public readonly partial record struct OrderId : IValue<Guid>;

#if IF_UNION && NET11_0_OR_GREATER
public sealed record Cat(string Name);

public sealed record Dog(string Name);

public union Pet(Cat, Dog);
#endif
