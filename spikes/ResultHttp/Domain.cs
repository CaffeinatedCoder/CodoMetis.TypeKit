using CodoMetis.TypeKit;

namespace ResultHttp;

// What a domain project holds: no reference to ASP.NET.
public enum OrderFault { NotFound, AlreadyShipped, NotYours }

public sealed record Order(int Id, string Item);

public static class Orders
{
    public static Result<Order, OrderFault> Find(int id) => id switch
    {
        1 => new Order(1, "Book"),
        2 => Result.Error(OrderFault.AlreadyShipped),
        3 => Result.Error(OrderFault.NotYours),
        _ => Result.Error(OrderFault.NotFound),
    };

    public static Task<Result<Order, OrderFault>> FindAsync(int id) => Task.FromResult(Find(id));

    public static Result<OrderFault> Cancel(int id) =>
        id == 1 ? Result.Success() : Result.Error(OrderFault.AlreadyShipped);
}
