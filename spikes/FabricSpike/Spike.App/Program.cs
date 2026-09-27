using Spike.Abstractions;
using Spike.Domain;

var order = OrderId.From(Guid.Parse("11111111-1111-1111-1111-111111111111"));
var sku   = Sku.From("SKU-1");

Console.WriteLine($"OrderId.Value={order.Value}");
Console.WriteLine($"Sku.Value={sku.Value}");
Console.WriteLine($"EmailAddress.IsValid(\"a@b\")={EmailAddress.IsValid("a@b")}, IsValid(\"x\")={EmailAddress.IsValid("x")}");
Console.WriteLine($"Sku is IValueObject<Sku,string>: {sku is IValueObject<Sku, string>}");
Console.WriteLine($"Generic From via IValueWrapper: {Wrap<OrderId, Guid>(Guid.Empty).Value}");

static TVO Wrap<TVO, T>(T v) where TVO : IValueWrapper<TVO, T>, IValueObject<TVO, T> where T : notnull => TVO.From(v);

/// <summary>Declared in a project that only references ValueObjects transitively.</summary>
public readonly partial record struct Sku : IValue<string>;
