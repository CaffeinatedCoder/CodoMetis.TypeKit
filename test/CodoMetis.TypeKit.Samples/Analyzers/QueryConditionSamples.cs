using CodoMetis.TypeKit;
using Microsoft.EntityFrameworkCore;

namespace ReadmeSamples.Analyzers.QueryConditions;

public sealed class Order
{
    public int Id { get; init; }

    public bool Open { get; set; }
}

public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public static class QueryConditionSamples
{
    public static void DelegateCondition(OrdersDb db)
    {
#pragma warning disable CMTK0009 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0009-query
        Func<Order, bool> isOpen = o => o.Open;
        var first = db.Orders.FirstOrNone(isOpen);           // CMTK0009: binds to the IEnumerable<T> overload and reads every row
        var found = db.Orders.FirstOrNone(o => o.Open);      // the database runs the condition and returns one row
        // end sample
#pragma warning restore CMTK0009
    }
}
