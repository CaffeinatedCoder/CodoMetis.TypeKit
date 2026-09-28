using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Http;

namespace ReadmeSamples.Base.Outcomes;

// sample: CodoMetis.TypeKit/result
public enum OrderFault { Empty, CustomerUnknown, Unpaid, Closed }

// end sample
public sealed class OrderService(ICustomers customers, IOrders orders)
{
    // sample: CodoMetis.TypeKit/result
    public Result<Order, OrderFault> Place(CustomerId customer, IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0) return Result.Error(OrderFault.Empty);
        if (!customers.Exists(customer)) return Result.Error(OrderFault.CustomerUnknown);

        return new Order(customer, lines);   // a bare value is a success
    }

    public Result<OrderFault> Cancel(OrderId id) =>
        orders.Remove(id) ? Result.Success() : OrderFault.CustomerUnknown;
    // end sample
}

public static class ResultUse
{
    public static void Use(OrderService service, CustomerId customer, IReadOnlyList<Line> lines, OrderId id, IBilling billing, IMailer mailer)
    {
        // sample: CodoMetis.TypeKit/result-use
        var placed = service.Place(customer, lines);

        IResult response = placed.Match(
            order => Results.Created($"/orders/{order.Id}", order),
            fault => Results.BadRequest(fault.ToString()));

        if (placed.TryGetValue(out var order, out var fault)) { /* order is non-null here */ }

        if (service.Cancel(id)) { /* a Result<TError> converts to bool, true for a success */ }

        Result<Invoice, OrderFault> invoiced = placed.Bind(order => billing.Invoice(order));
        Result<OrderFault> confirmed = placed.Bind(order => mailer.Confirm(order));   // a command after a query
        Result<Order, ApiFault> forApi = placed.MapError(ApiFault.From);             // across layers
        Option<Order> maybe = placed.ToOption();
        // end sample
    }
}

public static class Pipelines
{
    public static async Task Use(
        IOrders orders,
        OrderId id,
        IChargeLog log,
        IPayments payments,
        Result<Product, OrderFault> product,
        Result<int, OrderFault> quantity,
        ICatalog catalog,
        IPricing pricing,
        string sku,
        CustomerId customer,
        OrderInput input,
        IEnumerable<Result<Sku, SkuFault>> parsed)
    {
        // sample: CodoMetis.TypeKit/pipelines
        Result<OrderFault> charged = await orders.FindAsync(id)         // Task<Result<Order, OrderFault>>
            .EnsureAsync(order => order.IsOpen, OrderFault.Closed)       // a rule, on a pending result
            .MapAsync(order => order.Total)                              // a synchronous step
            .TapAsync(total => log.Charging(total))
            .BindAsync(total => payments.ChargeAsync(total))             // a command: Result<OrderFault> remains
            .TapErrorAsync(fault => log.NotCharged(id, fault));

        Result<Quote, OrderFault> quote =                                // query syntax: each step sees the earlier ones
            from item in catalog.Find(sku)
            from price in pricing.For(item, customer)
            select new Quote(item, price);

        Result<Line, OrderFault> line = product.Zip(quantity, (p, q) => new Line(p, q));
        Result<IReadOnlyList<Sku>, SkuFault> skus = input.Skus.Traverse(Sku.Create);
        Result<IReadOnlyList<Sku>, SkuFault> all = parsed.Sequence();
        // end sample
    }
}

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct CustomerId : IValue<Guid>;

public enum SkuFault { Blank }

public readonly partial record struct Sku : IValidatedValue<Sku, string, SkuFault>
{
    public static Result<Sku, SkuFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(SkuFault.Blank) : new Sku(value.Trim());
}

public sealed record Product(Sku Sku);

public sealed record Line(Product Product, int Quantity);

public sealed class Order(CustomerId customer, IReadOnlyList<Line> lines)
{
    public OrderId Id { get; } = OrderId.New();

    public CustomerId Customer { get; } = customer;

    public IReadOnlyList<Line> Lines { get; } = lines;

    public decimal Total => Lines.Sum(line => line.Quantity);

    public bool IsOpen => true;
}

public sealed record Quote(Product Product, decimal Price);

public interface ICatalog
{
    Result<Product, OrderFault> Find(string sku);
}

public interface IPricing
{
    Result<decimal, OrderFault> For(Product product, CustomerId customer);
}

public sealed record Invoice(OrderId Order);

public sealed record OrderInput(IReadOnlyList<string> Skus);

public sealed record ApiFault(string Code)
{
    public static ApiFault From(OrderFault fault) => new(fault.ToString());
}

public interface ICustomers
{
    bool Exists(CustomerId customer);
}

public interface IOrders
{
    bool Remove(OrderId id);

    Task<Result<Order, OrderFault>> FindAsync(OrderId id);
}

public interface IBilling
{
    Result<Invoice, OrderFault> Invoice(Order order);
}

public interface IMailer
{
    Result<OrderFault> Confirm(Order order);
}

public interface IChargeLog
{
    void Charging(decimal total);

    void NotCharged(OrderId id, OrderFault fault);
}

public interface IPayments
{
    Task<Result<OrderFault>> ChargeAsync(decimal total);
}
