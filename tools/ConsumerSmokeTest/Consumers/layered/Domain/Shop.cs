using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace Shop;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Quantity : IValue<int>;

public enum CodeFault { Blank, NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);
        if (value != value.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);
        return new ProductCode(value);
    }
}
