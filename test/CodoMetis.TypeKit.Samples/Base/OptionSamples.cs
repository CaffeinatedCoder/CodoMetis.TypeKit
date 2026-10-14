// sample: CodoMetis.TypeKit/option
using CodoMetis.TypeKit;

// end sample
namespace ReadmeSamples.Base.Options;

public interface ICustomerRepository
{
    Option<Customer> FindByEmail(string email);

    Task<Option<Customer>> FindByEmailAsync(string email);
}

public interface IAudit
{
    Task SeenAsync(Customer customer);
}

public enum SignInFault { Unknown }

public sealed class Customer
{
    public required string Name { get; init; }

    public bool IsActive { get; init; }

    public Tier Tier { get; init; }

    public Option<Address> Address { get; init; } = Option.None();

    public void RecordVisit() { }
}

public sealed record Address(string Town);

public enum Tier { Standard, Gold }

public static class OptionSample
{
    public static void Use(ICustomerRepository repository, string email, IReadOnlyDictionary<Tier, decimal> discounts)
    {
        // sample: CodoMetis.TypeKit/option
        Option<Customer> customer = repository.FindByEmail(email);   // Some(...) or None

        string greeting = customer.Match(
            c  => $"Welcome back, {c.Name}",
            () => "Welcome");

        if (customer.TryGetValue(out var found))
            found.RecordVisit();

        Option<string> town = customer
            .Bind(c => c.Address)          // Option<Address>, itself optional
            .Map(a => a.Town)
            .Filter(t => t.Length > 0);

        var name = from c in customer where c.IsActive select c.Name;   // query syntax works too

        Option<decimal> discount =                                       // a later from sees the earlier ones
            from c in customer
            from rate in discounts.GetValueOrNone(c.Tier)                // TryGetValue as an option
            select rate;
        // end sample
    }

    public static async Task<Result<Customer, SignInFault>> UseAsync(ICustomerRepository repository, IAudit audit, string email)
    {
        // sample: CodoMetis.TypeKit/option-async
        Result<Customer, SignInFault> active = await repository.FindByEmailAsync(email)   // Task<Option<Customer>>
            .FilterAsync(c => c.IsActive)
            .TapAsync(c => audit.SeenAsync(c))                                             // waited for
            .ToResultAsync(SignInFault.Unknown);                                           // into a Result pipeline
        // end sample

        return active;
    }
}
