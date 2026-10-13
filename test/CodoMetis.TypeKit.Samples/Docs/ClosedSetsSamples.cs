using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Docs.ClosedSets;

// sample: closed-sets/declaring
public sealed partial class Shipping : IClosedSet<Shipping>
{
    public static readonly Shipping Standard = new(days: 5, fee: 0m);
    public static readonly Shipping Express = new(days: 1, fee: 9.90m);
    public static readonly Shipping Pickup = new(days: 0, fee: 0m);

    private Shipping(int days, decimal fee) { Days = days; Fee = fee; }

    public int Days { get; }
    public decimal Fee { get; }

    public DateOnly ArrivesBy(DateOnly ordered) => ordered.AddDays(Days);
}
// end sample

// sample: closed-sets/wire
public sealed record Quote(Shipping Method, Dictionary<Shipping, decimal> Fees);
// end sample

// sample: closed-sets/database
public sealed class Order
{
    public int Id { get; set; }
    public Shipping Method { get; set; } = Shipping.Standard;
    public List<Shipping> Offered { get; set; } = [];
}
// end sample

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

// sample: closed-sets/configuration
public sealed class CheckoutOptions
{
    public Shipping Default { get; set; } = Shipping.Standard;
}
// end sample

public static class Usage
{
    public static (string Name, string Qualified, string All, bool Same, bool Known) Members()
    {
        // sample: closed-sets/members
        string name = Shipping.Express.Name;                               // "Express"
        string qualified = Shipping.Express.QualifiedName;                 // "Shipping.Express"
        string all = string.Join(" ", Shipping.All.Select(s => s.Name));   // "Standard Express Pickup"
        bool same = Shipping.Parse("Express") == Shipping.Express;         // true, members are singletons
        bool known = Shipping.TryParse("Drone", out _);                    // false, names compare ordinally
        // end sample

        return (name, qualified, all, same, known);
    }

    /// <summary>The block's last line, which throws: the same region, apart, so the lines above can run.</summary>
    public static void ParseRefused()
    {
        // sample: closed-sets/members
        Shipping.Parse("Drone");                                           // FormatException naming Shipping and member, never the name
        // end sample
    }

    public static string Label(Shipping shipping)
    {
        // sample: closed-sets/match
        string label = shipping.Match(
            standard: () => "Ships within a week",
            express: () => "Arrives tomorrow",
            pickup: () => "Ready at the counter");
        // end sample

        return label;
    }

    public static string Wire()
    {
        // sample: closed-sets/wire-use
        string json = JsonSerializer.Serialize(new Quote(Shipping.Express, new() { [Shipping.Standard] = 0m, [Shipping.Express] = 9.90m }));   // {"Method":"Express","Fees":{"Standard":0,"Express":9.90}}
        // end sample

        return json;
    }

    public static void WireRefused()
    {
        // sample: closed-sets/wire-use
        JsonSerializer.Deserialize<Quote>("""{"Method":"Drone","Fees":{}}""");                                                                   // JsonException naming Shipping and member, never the name
        // end sample
    }

    public static IQueryable<Order> Query(ShopDb db) =>
        // sample: closed-sets/database-use
        db.Orders.Where(o => o.Method == Shipping.Express);   // WHERE o."Method" = 'Express'
        // end sample

    public static string Configured()
    {
        // sample: closed-sets/configuration-use
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Checkout:Default"] = "Express" }).Build();
        string chosen = configuration.GetSection("Checkout").Get<CheckoutOptions>()!.Default.Name;   // "Express"
        // end sample

        return chosen;
    }
}

public sealed class ClosedSetsTests
{
    private const string Page = "docs/closed-sets.md";

    private static string Quoted(string text) => $"\"{text}\"";

    private static string Lower(bool value) => value ? "true" : "false";

    [Fact]
    public void A_set_answers_what_the_comments_say()
    {
        var (name, qualified, all, same, known) = Usage.Members();

        name.ShouldBeShownIn(Page, Quoted, "Shipping.Express.Name");
        qualified.ShouldBeShownIn(Page, Quoted, "Shipping.Express.QualifiedName");
        all.ShouldBeShownIn(Page, Quoted, "string.Join(\" \", Shipping.All.Select(s => s.Name))");
        same.ShouldBeShownIn(Page, Lower, "Shipping.Parse(\"Express\") == Shipping.Express");
        known.ShouldBeShownIn(Page, Lower, "Shipping.TryParse(\"Drone\", out _)");
        SampleOutputs.ShouldRefuseAsShownIn(() => Shipping.Parse("Drone"), Page, input: "Drone");
        Should.Throw<FormatException>(Usage.ParseRefused);

        Shipping.Express.ArrivesBy(new DateOnly(2026, 10, 13)).ShouldBe(new DateOnly(2026, 10, 14));
    }

    [Fact]
    public void Match_picks_the_member_s_branch()
    {
        Usage.Label(Shipping.Express).ShouldBe("Arrives tomorrow");
        Usage.Label(Shipping.Pickup).ShouldBe("Ready at the counter");
    }

    [Fact]
    public void On_the_wire_a_member_is_its_name_as_the_comments_say()
    {
        Usage.Wire().ShouldBeShownIn(Page, json => json, "JsonSerializer.Serialize(new Quote(Shipping.Express, new() { [Shipping.Standard] = 0m, [Shipping.Express] = 9.90m }))");
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Quote>("""{"Method":"Drone","Fees":{}}"""), Page, input: "Drone");
        Should.Throw<JsonException>(Usage.WireRefused);

        var back = JsonSerializer.Deserialize<Quote>(Usage.Wire())!;
        ReferenceEquals(back.Method, Shipping.Express).ShouldBeTrue();
        back.Fees[Shipping.Express].ShouldBe(9.90m);
    }

    [Fact]
    public void The_query_is_the_SQL_the_comment_says()
    {
        using var db = new ShopDb(SampleOutputs.NpgsqlWithoutServer<ShopDb>());

        db.Orders.Where(o => o.Method == Shipping.Express).ShouldBeTheSqlShownIn(Page);
        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Method))!.GetColumnType().ShouldBe("text");
        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Offered))!.GetColumnType().ShouldBe("text[]", "a primitive collection of names: text[] on PostgreSQL");
    }

    [Fact]
    public void Configuration_binds_a_set_as_the_comment_says() =>
        Usage.Configured().ShouldBeShownIn(Page, Quoted, "configuration.GetSection(\"Checkout\").Get<CheckoutOptions>()!.Default.Name");

    /// <summary>What the page says of the document: a string enum of the names, with nothing registered per set.</summary>
    [Fact]
    public async Task The_document_describes_a_set_as_a_string_enum()
    {
        var document = await SampleOutputs.OpenApiDocumentAsync(
            builder => builder.Services.AddOpenApi(options => options.AddTypeKit()),
            app => app.MapPost("/quotes", (Quote quote) => quote.Method.Name));

        document.Component(nameof(Shipping)).ToJsonString().ShouldBe("""{"enum":["Standard","Express","Pickup"],"type":"string"}""");
    }

    /// <summary>What the page says of a route: a minimal-API parameter binds through the generated <c>TryParse</c>, and an unknown name is a 400.</summary>
    [Fact]
    public async Task A_route_parameter_binds_by_name()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
#pragma warning disable ASP0020 // Shipping has TryParse once generated: the route analyzer reads the source before weaving
        app.MapGet("/delivery/{method}", (Shipping method) => method.Days);
#pragma warning restore ASP0020
        await app.StartAsync(TestContext.Current.CancellationToken);

        (await app.GetTestClient().GetFromJsonAsync<int>("/delivery/Express", TestContext.Current.CancellationToken)).ShouldBe(1);
        (await app.GetTestClient().GetAsync("/delivery/Drone", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }
}
