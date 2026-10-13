using CodoMetis.TypeKit;
using System.Reflection;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Docs.Options;

// sample: option/types
public readonly partial record struct CustomerId : IValue<Guid>;

public sealed record Address(string Town, string Country);

public sealed record Customer(CustomerId Id, string Name, string Tier, Option<string> Nickname, Option<Address> Address);
// end sample

public enum LookupFault { Unknown }

// sample: option/boundary
public sealed record CustomerDto(string Name, string? Nickname);   // the wire says absent with null

// end sample
public sealed class Shop
{
    public Customer Ann { get; } = new(CustomerId.New(), "Ann", "gold", Option.Some("Annie"), Option.Some(new Address("Berlin", "DE")));

    public Dictionary<CustomerId, Customer> Customers { get; }

    public List<CustomerId> Visits { get; } = [];

    public Shop() => Customers = new Dictionary<CustomerId, Customer> { [Ann.Id] = Ann };

    public (Option<Customer> Found, Option<Customer> Unknown, Option<Customer> Gold, Option<string> Nickname, Option<int> Parsed, Option<int> None) Create(string text)
    {
        var customers = Customers;
        var ann = Ann;

        // sample: option/create
        Option<Customer> found = customers.GetValueOrNone(ann.Id);                      // Some
        Option<Customer> unknown = customers.GetValueOrNone(CustomerId.New());          // None
        Option<Customer> gold = customers.Values.FirstOrNone(c => c.Tier == "gold");    // Some, the first match (or None)
        Option<string> nickname = ann.Nickname;                                         // Some
        Option<int> parsed = int.TryParse(text, out var number) ? Option.Some(number) : Option.None();   // None, taking its type from the conditional
        Option<int> none = Option.None<int>();                                          // where nothing supplies the type
        // end sample

        return (found, unknown, gold, nickname, parsed, none);
    }

    public (string Greeting, string Tier, int? Limit, int Sum, bool Known) Reach(Option<Customer> found, Option<Customer> unknown, Option<int> parsed)
    {
        var visits = Visits;

        // sample: option/reach
        string greeting = found.Match(customer => $"Hello again {customer.Name}", () => "Hello");   // "Hello again Ann"
        string tier = unknown.Map(customer => customer.Tier).Or("standard");                       // "standard"
        int? limit = parsed.OrNull();                                                               // null, an int? for an Option<int>
        int sum = found.Map(customer => customer.Name.Length).OrDefault();                          // 3, a struct's default for None
        bool known = found.IsSome();                                                                // true

        if (found.TryGetValue(out var customer)) visits.Add(customer.Id);   // customer is non-null in the branch

        foreach (var each in unknown.AsEnumerable()) visits.Add(each.Id);   // runs once for Some, never for None
        // end sample

        return (greeting, tier, limit, sum, known);
    }

    public (Option<decimal> Discount, Option<string> Country, Option<(string Name, string Town)> Label, Option<Customer> Seen, Option<string> Greeting, List<string> Nicknames, List<Customer> Known) Combine(Option<Customer> found)
    {
        var visits = Visits;
        var customers = Customers;
        var ids = new List<CustomerId> { Ann.Id, CustomerId.New() };

        // sample: option/combine
        Option<decimal> discount = found.Filter(customer => customer.Tier == "gold").Map(_ => 0.1m);   // Some, Ann is gold
        Option<string> country = found.Bind(customer => customer.Address).Map(address => address.Country);   // Some, the Option<Address> flattened and then mapped
        Option<(string Name, string Town)> label = found.Zip(found.Bind(c => c.Address), (c, a) => (c.Name, a.Town));   // Some, only when both are
        Option<Customer> seen = found.Tap(customer => visits.Add(customer.Id));   // runs for Some, hands the option on
        IEnumerable<string> nicknames = customers.Values.Choose(customer => customer.Nickname);   // "Annie", the nicknames of those who have one: a filter and a map in one
        IEnumerable<Customer> known = ids.Choose(id => customers.GetValueOrNone(id));          // the customers the ids find, the unknown ones dropped

        Option<string> greeting =
            from customer in found
            from address in customer.Address            // a later from sees the earlier ones
            where address.Country == "DE"
            select $"{customer.Name} in {address.Town}";   // Some
        // end sample

        return (discount, country, label, seen, greeting, nicknames.ToList(), known.ToList());
    }

    public Task<Option<Customer>> FindAsync(CustomerId id) => Task.FromResult(Customers.GetValueOrNone(id));

    public async Task<string> Pending()
    {
        var shop = this;
        var ann = Ann;

        // sample: option/pending
        Option<Customer> loaded = await shop.FindAsync(ann.Id);              // the await returns to the caller's context
        string shown = loaded.Map(customer => customer.Name).Or("unknown");   // "Ann", the selector on that context too
        // end sample

        return shown;
    }

    // sample: option/boundary
    static Customer FromDto(CustomerDto dto) => new(CustomerId.New(), dto.Name, "standard", dto.Nickname.ToOption(), Option.None());

    static CustomerDto ToDto(Customer customer) => new(customer.Name, customer.Nickname.OrNull());
    // end sample

    public (Customer FromWire, CustomerDto ToWire, Result<Customer, LookupFault> Required) Boundary()
    {
        var customers = Customers;
        var fromWire = FromDto(new CustomerDto("Bo", null));
        var toWire = ToDto(Ann);

        // sample: option/to-result
        Result<Customer, LookupFault> required = customers.GetValueOrNone(CustomerId.New()).ToResult(LookupFault.Unknown);   // Error: Unknown
        // end sample

        return (fromWire, toWire, required);
    }
}

public sealed class OptionTests
{
    private const string Readme = "docs/option.md";

    private static string SomeOrNone<T>(Option<T> option) where T : notnull => option.IsSome() ? "Some" : "None";

    private static string Quoted(string text) => $"\"{text}\"";

    [Fact]
    public void Creating_answers_what_the_comments_say()
    {
        var shop = new Shop();
        var customers = shop.Customers;
        var ann = shop.Ann;
        var text = "x";

        customers.GetValueOrNone(ann.Id).ShouldBeShownIn(Readme, SomeOrNone);
        customers.GetValueOrNone(CustomerId.New()).ShouldBeShownIn(Readme, SomeOrNone);
        customers.Values.FirstOrNone(c => c.Tier == "gold").ShouldBeShownIn(Readme, SomeOrNone);
        ann.Nickname.ShouldBeShownIn(Readme, SomeOrNone);
        (int.TryParse(text, out var number) ? Option.Some(number) : Option.None()).ShouldBeShownIn(Readme, SomeOrNone);

        var (found, unknown, gold, nickname, parsed, none) = shop.Create(text);
        (found, unknown.IsNone(), gold, nickname, parsed.IsNone(), none.IsNone()).ShouldBe((Option.Some(ann), true, Option.Some(ann), Option.Some("Annie"), true, true));
    }

    [Fact]
    public void Reaching_the_value_answers_what_the_comments_say()
    {
        var shop = new Shop();
        var (found, unknown, _, _, parsed, _) = shop.Create("x");

        found.Match(customer => $"Hello again {customer.Name}", () => "Hello").ShouldBeShownIn(Readme, Quoted);
        unknown.Map(customer => customer.Tier).Or("standard").ShouldBeShownIn(Readme, Quoted);
        parsed.OrNull().ShouldBeShownIn(Readme, limit => limit?.ToString() ?? "null");
        found.Map(customer => customer.Name.Length).OrDefault().ShouldBeShownIn(Readme, sum => sum.ToString());
        found.IsSome().ShouldBeShownIn(Readme, known => known ? "true" : "false");

        var (greeting, tier, limit, sum, known) = shop.Reach(found, unknown, parsed);
        (greeting, tier, limit, sum, known).ShouldBe(("Hello again Ann", "standard", null, 3, true));
        shop.Visits.ShouldBe([shop.Ann.Id], "TryGetValue took the branch once, and the foreach over None ran never");
    }

    [Fact]
    public void Combining_answers_what_the_comments_say()
    {
        var shop = new Shop();
        var (found, _, _, _, _, _) = shop.Create("x");

        found.Filter(customer => customer.Tier == "gold").Map(_ => 0.1m).ShouldBeShownIn(Readme, SomeOrNone);
        found.Bind(customer => customer.Address).Map(address => address.Country).ShouldBeShownIn(Readme, SomeOrNone);
        found.Zip(found.Bind(c => c.Address), (c, a) => (c.Name, a.Town)).ShouldBeShownIn(Readme, SomeOrNone);

        var (discount, country, label, seen, greeting, nicknames, known) = shop.Combine(found);
        (discount, country, label, seen, greeting).ShouldBe((Option.Some(0.1m), Option.Some("DE"), Option.Some(("Ann", "Berlin")), found, Option.Some("Ann in Berlin")));
        shop.Visits.ShouldBe([shop.Ann.Id], "Tap ran once");
        nicknames.ShouldBe(["Annie"], "Choose kept the one nickname and dropped nothing else");
        known.ShouldBe([shop.Ann], "the unknown id was dropped");
    }

    [Fact]
    public async Task A_pending_option_answers_what_the_comment_says()
    {
        var shop = new Shop();

        var shown = await shop.Pending();

        shown.ShouldBeShownIn(Readme, Quoted, "loaded.Map(customer => customer.Name).Or(\"unknown\")");
    }

    [Fact]
    public void The_boundary_converts_as_the_page_says()
    {
        var shop = new Shop();
        var customers = shop.Customers;

        customers.GetValueOrNone(CustomerId.New()).ToResult(LookupFault.Unknown)
                 .ShouldBeShownIn(Readme, result => result.Match(customer => $"Success: {customer.Name}", fault => $"Error: {fault}"));

        var (fromWire, toWire, required) = shop.Boundary();
        fromWire.Nickname.IsNone().ShouldBeTrue();
        toWire.ShouldBe(new CustomerDto("Ann", "Annie"));
        required.TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(LookupFault.Unknown);
    }

    /// <summary>
    /// The page's surface table names every public member of <c>Option&lt;T&gt;</c>, of the static class
    /// <c>Option</c> and of the extensions, read from the types, so a member added to the package without
    /// a row fails here.
    /// </summary>
    [Fact]
    public void The_surface_table_names_every_public_member()
    {
        var page = SampleOutputs.Readme(Readme);
        var surface = page[page.IndexOf("## The surface", StringComparison.Ordinal)..];

        var members = PublicMembers(typeof(Option<>), typeof(Option), typeof(ValueTypeOptionExtensions));

        members.Length.ShouldBeGreaterThanOrEqualTo(20, string.Join(", ", members));
        members.Where(member => !Regex.IsMatch(surface, $@"`{Regex.Escape(member)}[`(<,]")).ShouldBeEmpty("Every public member has a row in the surface table.");
    }

    /// <summary>Public method names, operators, accessors and the record's own members aside.</summary>
    internal static string[] PublicMembers(params Type[] types) =>
    [
        .. types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Where(name => name is not ("Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "PrintMembers"))
                .Distinct()
                .Order(StringComparer.Ordinal)
    ];
}
