// The orders service's endpoints. docs/walkthrough.md shows each region; test/CodoMetis.TypeKit.Samples
// compiles this file too, for WalkthroughTests.
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Walkthrough;

// The value objects are declared beside the endpoints, so ASP.NET's route analyzer reads OrderId before the
// generators make it parsable (docs/aspnet-core.md). In a domain project of their own they would need no pragma.
#pragma warning disable ASP0020
public static class OrderEndpoints
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        // sample: walkthrough/place
        app.MapPost("/orders", async (PlaceOrder request, OrdersDb db, Serilog.ILogger log) =>
        {
            var order = new Order { Id = OrderId.New(), Email = request.Email, Quantity = request.Quantity, Status = OrderStatus.Placed };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            var view = OrderView.Of(order);
            log.Information("Placed {@Order}", view);   // Placed {"Id": "…", "Email": "a***@example.com", "Quantity": "2", "Status": "Placed", "$type": "OrderView"}
            return TypedResults.Created($"/orders/{order.Id}", view);
        });
        // end sample

        // sample: walkthrough/find
        app.MapGet("/orders/{id}", async (OrderId id, OrdersDb db) =>
            (await db.Orders.FirstOrNoneAsync(order => order.Id == id))   // an Option: the order, or none
                .ToResult(OrderFault.NotFound)
                .Map(OrderView.Of)
                .ToHttpResult())
           .WithoutFaults(OrderFault.AlreadyShipped, OrderFault.Cancelled, OrderFault.Cleared);   // finding one reports only NotFound
        // end sample

        // sample: walkthrough/list
        app.MapGet("/orders", async (Quantity minimum, OrdersDb db) =>
            await db.Orders.Where(order => order.Quantity.Value >= minimum.Value)   // WHERE "o"."Quantity" >= @minimum_Value
                           .Select(order => OrderView.Of(order))
                           .ToListAsync());
        // end sample

        // sample: walkthrough/change
        app.MapPatch("/orders/{id}", async (OrderId id, OrderPatch patch, OrdersDb db) =>
        {
            var changed = (await db.Orders.FirstOrNoneAsync(order => order.Id == id))
                .ToResult(OrderFault.NotFound)
                .Bind(order => order.Apply(patch));
            await db.SaveChangesAsync();
            return changed.Map(OrderView.Of).ToHttpResult();
        });
        // end sample
    }
}
#pragma warning restore ASP0020
