using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

public readonly partial record struct OrderId : IValue<Guid>;
public readonly partial record struct CustomerId : IValue<Guid>;
public readonly partial record struct Tag : IValue<string>;
public readonly partial record struct Quantity : IValue<int>;

public enum CodeFault { NotUpperCase }

public readonly partial record struct Code : IValidatedValue<Code, string, CodeFault>
{
    public static Result<Code, CodeFault> Create(string value) =>
        value == value.ToUpperInvariant() ? new Code(value) : Result.Error(CodeFault.NotUpperCase);
}

public sealed class Customer
{
    public CustomerId Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; } = [];
}

public sealed class Order
{
    public OrderId Id { get; set; }
    public CustomerId CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Code Code { get; set; }
    public Quantity? Discount { get; set; }
    public List<Tag> Tags { get; set; } = [];
}
