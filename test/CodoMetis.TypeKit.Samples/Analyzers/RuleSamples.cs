using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Analyzers.Rules;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct CustomerId : IValue<Guid>;

public readonly partial record struct ProductId : IValue<Guid>;

public enum EmailFault { NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value) : Result.Error(EmailFault.NoAt);
}

public enum OrderFault { Unknown }

public interface IOrders
{
    Task<Result<OrderFault>> CancelAsync(OrderId id);

    Result<OrderFault> Cancel(OrderId id);
}

public interface IOrderIds
{
    Task<Result<OrderFault>> CancelAsync(Guid id);
}

public interface IBus
{
    void Subscribe<T>(Func<T, Task> handler);
}

public interface ICache
{
    Option<string> Remove(string key);
}

public sealed record Row(Guid Id);

public sealed record Order(OrderId Id, CustomerId CustomerId);

public sealed record Product(ProductId Id);

public sealed record Customer(CustomerId Id);

public static class Rules
{
    public static async Task IgnoredOutcome(string input, IOrders orders, OrderId id, ICache cache, string key)
    {
#pragma warning disable CMTK0003 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0003
        Email.Create(input);                         // CMTK0003: validated, and the verdict forgotten
        await orders.CancelAsync(id);                // CMTK0003: it may have failed
        _ = cache.Remove(key);                       // an explicit discard is silent
        // end sample
#pragma warning restore CMTK0003
    }

    public static void DroppedByConversion(IOrderIds orders, Guid id)
    {
#pragma warning disable CMTK0003 // what the sample shows the rule reporting
#pragma warning disable CS8321 // the sample declares local functions and calls none
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0003-task
        Task Cancel(Guid id) => orders.CancelAsync(id);           // CMTK0003: the Result is gone
        Func<Task> cancel = () => orders.CancelAsync(id);         // CMTK0003
        async Task CancelQuietly(Guid id) => _ = await orders.CancelAsync(id);   // an explicit discard is silent
        // end sample
#pragma warning restore CS8321
#pragma warning restore CMTK0003
    }

    public static void DroppedByMethodGroup(IOrderIds orders, IBus bus)
    {
#pragma warning disable CMTK0003 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0003-method-group
        Func<Guid, Task> cancel = orders.CancelAsync;             // CMTK0003
        bus.Subscribe<Guid>(orders.CancelAsync);                  // CMTK0003: the parameter is a Func<T, Task>
        // end sample
#pragma warning restore CMTK0003
    }

    /// <summary>The silent part is compiled outside the suppression, so a report on it fails the build.</summary>
    public static async Task DroppedCollections(IOrders orders, IEnumerable<OrderId> ids, OrderId first, OrderId second)
    {
#pragma warning disable CMTK0003 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0003-collections
        await Task.WhenAll(ids.Select(orders.CancelAsync));                 // CMTK0003: a Result[] nobody reads
        await Task.WhenAll(orders.CancelAsync(first), orders.CancelAsync(second));   // CMTK0003
        ids.Select(orders.Cancel).ToList();                                 // CMTK0003: a list of them, forgotten

        // end sample
#pragma warning restore CMTK0003
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0003-collections
        var cancelFirst = orders.CancelAsync(first);
        var cancelSecond = orders.CancelAsync(second);
        await Task.WhenAll(cancelFirst, cancelSecond);                      // silent: both tasks still hold their results
        // end sample
    }

    public static void DefaultFilledArray(int count, IEnumerable<Row> rows)
    {
#pragma warning disable CMTK0005 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0005
        var ids = new OrderId[count];                // CMTK0005: count default instances
        OrderId[] kept = [.. rows.Select(r => OrderId.From(r.Id))];   // built from values
        // end sample
#pragma warning restore CMTK0005
    }

    public static void MixedComparison(IQueryable<Order> orders, Product product, Customer customer)
    {
#pragma warning disable CMTK0008 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0008
        orders.Where(o => o.CustomerId.Value == product.Id.Value);  // CMTK0008: a customer id against a product id
        orders.Where(o => o.CustomerId == customer.Id);             // what was meant
        // end sample
#pragma warning restore CMTK0008
    }

    public static void MixedJoin(IQueryable<Order> orders, IQueryable<Customer> customers)
    {
#pragma warning disable CMTK0008 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0008-join
        var wrong = from o in orders join c in customers on o.Id.Value equals c.Id.Value select o;  // CMTK0008: an order id joined to a customer id
        var meant = from o in orders join c in customers on o.CustomerId equals c.Id select o;      // what was meant
        orders.Join(customers, o => o.Id.Value, c => c.Id.Value, (o, c) => o);                     // CMTK0008
        // end sample
#pragma warning restore CMTK0008
    }

    public static void MixedObjects(Order order, CustomerId customerId)
    {
#pragma warning disable CMTK0008 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0008-equals
        if (order.Id.Equals(customerId)) { /* never */ }            // CMTK0008
        if (Equals(order.Id, customerId)) { /* never */ }           // CMTK0008
        // end sample
#pragma warning restore CMTK0008
    }

    public static void DefaultProducingCalls(string input, List<OrderId> ids)
    {
#pragma warning disable CMTK0009 // what the sample shows the rule reporting
        // sample: CodoMetis.TypeKit.Analyzers/cmtk0009
        var code = ProductCode.TryFrom(input).OrDefault();   // CMTK0009: a ProductCode that never passed Create
        var first = ids.FirstOrDefault();                    // CMTK0009: an OrderId nobody made, for an empty list
        // end sample
#pragma warning restore CMTK0009
    }
}

public enum CodeFault { Blank }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(CodeFault.Blank) : new ProductCode(value);
}
