using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Docs.Migrating;

public readonly partial record struct OrderId : IValue<Guid>;

public enum OrderFault { Unknown }

// sample: migrating-to-2/abstract
// public abstract partial record Money : IValue<decimal>;     1.5 built these; 2.0: CMTK1022
// public sealed partial record Euro : Money;

public abstract record Money;
public sealed partial record Euro : Money, IValue<decimal>;    // the marker on the sealed type, which is generated
// end sample

public interface IOrders
{
    Task<Result<OrderFault>> CancelAsync(OrderId id);

    IAsyncEnumerable<Result<OrderFault>> CancelEachAsync(IEnumerable<OrderId> ids);
}

public sealed record Order(OrderId Id, decimal Total, DateTimeOffset Placed)
{
    public bool IsLarge() => Total > 1_000;
}

public static class Usage
{
    /// <summary>The silent part is compiled outside the suppression, so a report on it fails the build.</summary>
    public static async Task Dropped(IOrders orders, OrderId id, IEnumerable<OrderId> ids)
    {
#pragma warning disable CMTK0003 // what the page shows the rule reporting
        // sample: migrating-to-2/dropped
        await orders.CancelAsync(id);                                 // CMTK0003: an error since 2.0
        orders.CancelAsync(id).Wait();                                // CMTK0003: new in 2.0, waited for and forgotten

        // end sample
#pragma warning restore CMTK0003
        // sample: migrating-to-2/dropped
        _ = await orders.CancelAsync(id);                             // the drop, said in the code
        await foreach (var _ in orders.CancelEachAsync(ids)) { }      // a loop variable named _ says it too
        // end sample
    }

    public static (Option<Order> Large, Option<Order> Local, Option<Order> Latest) Queries(IQueryable<Order> orders)
    {
        // sample: migrating-to-2/queries
        Option<Order> large = orders.FirstOrNone(o => o.Total > 100);                    // 2.0: WHERE and LIMIT 1 in the query; 1.5 read every row
        Option<Order> local = orders.AsEnumerable().FirstOrNone(o => o.IsLarge());       // a condition the database cannot run, in memory as before
        Option<Order> latest = orders.OrderByDescending(o => o.Placed).FirstOrNone();    // where 1.5 took LastOrNone(), now a compile error on a query
        // end sample

        return (large, local, latest);
    }
}
