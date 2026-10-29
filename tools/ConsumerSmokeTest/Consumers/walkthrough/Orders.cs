// The orders service's domain: its value objects, what it stores, and what it exchanges. docs/walkthrough.md
// shows each region; test/CodoMetis.TypeKit.Samples compiles this file too, for WalkthroughTests.
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Walkthrough;

// sample: walkthrough/values
public readonly partial record struct OrderId : IValue<Guid>;

public enum EmailFault { Blank, Hidden, NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.AsText()                                                                    // trimmed, composed, no hidden characters
             .MapError(fault => fault == TextFault.Blank ? EmailFault.Blank : EmailFault.Hidden)
             .Ensure(text => text.IndexOf('@') > 0, EmailFault.NoAt)
             .Map(text => new Email(text));

    public override string ToString() => $"{Value[0]}***{Value[Value.IndexOf('@')..]}";   // what a log shows
}

public enum QuantityFault { NotPositive }

public readonly partial record struct Quantity : IValidatedValue<Quantity, int, QuantityFault>
{
    public static Result<Quantity, QuantityFault> Create(int value) =>
        value > 0 ? new Quantity(value) : Result.Error(QuantityFault.NotPositive);
}

public sealed partial class OrderStatus : IClosedSet<OrderStatus>
{
    public static readonly OrderStatus Placed = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}
// end sample

// sample: walkthrough/storage
public sealed class Order
{
    public required OrderId Id { get; init; }
    public required Email Email { get; set; }
    public required Quantity Quantity { get; set; }
    public required OrderStatus Status { get; set; }
}

public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
// end sample

// sample: walkthrough/messages
public sealed record PlaceOrder(Email Email, Quantity Quantity);

public sealed record OrderView(OrderId Id, Email Email, Quantity Quantity, OrderStatus Status)
{
    public static OrderView Of(Order order) => new(order.Id, order.Email, order.Quantity, order.Status);
}

public enum OrderFault { NotFound, AlreadyShipped, Cancelled, Cleared }
// end sample

// sample: walkthrough/patch
public sealed record OrderPatch
{
    public Patch<Quantity> Quantity { get; init; }
    public Patch<OrderStatus> Status { get; init; }
}

public static class OrderPatching
{
    public static Result<Order, OrderFault> Apply(this Order order, OrderPatch patch)
    {
        if (order.Status == OrderStatus.Shipped) return Result.Error(OrderFault.AlreadyShipped);
        if (order.Status == OrderStatus.Cancelled) return Result.Error(OrderFault.Cancelled);
        if (patch.Quantity.State == PatchState.Null || patch.Status.State == PatchState.Null) return Result.Error(OrderFault.Cleared);   // neither field can be cleared

        patch.Quantity.Tap(quantity => order.Quantity = quantity);
        patch.Status.Tap(status => order.Status = status);
        return order;
    }
}
// end sample
