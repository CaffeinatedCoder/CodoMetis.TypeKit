using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Root.Intro;

public enum EmailFault { NoAt }

// sample: README/intro
public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}
// end sample

// sample: README/closed-set
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}
// end sample
