using Spike.Generators;

namespace Spike.App;

/// <summary>Plain: no data, no constructor; the generators add a private one.</summary>
public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Open = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}

public enum FaultKind { NotFound, Conflict, Invalid }

/// <summary>With constant data per member, through a private constructor the author writes.</summary>
public sealed partial class OrderFault : IClosedSet<OrderFault>
{
    public static readonly OrderFault NotFound = new(FaultKind.NotFound, "The order does not exist.");
    public static readonly OrderFault Shipped = new(FaultKind.Conflict, "The order has shipped and cannot change.");
    public static readonly OrderFault Empty = new(FaultKind.Invalid, null);

    private OrderFault(FaultKind kind, string? remedy)
    {
        Kind = kind;
        Remedy = remedy;
    }

    public FaultKind Kind { get; }

    public string? Remedy { get; }
}
