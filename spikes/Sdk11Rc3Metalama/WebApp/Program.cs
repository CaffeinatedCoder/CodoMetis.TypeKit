using CodoMetis.TypeKit.ValueObjects;

var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/orders/{id}", (OrderId id) => id);
app.Run();

public readonly partial record struct OrderId : IValue<Guid>;
