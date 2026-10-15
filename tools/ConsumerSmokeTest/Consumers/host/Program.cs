using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shop;

// OpenAPI: a real host, its own document, and the binding behind it.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddOpenApi(options => options.AddTypeKit());

await using (var app = builder.Build())
{
    app.MapOpenApi();
    app.MapGet("/orders/{id}", (OrderId id) => new OrderView(id, [Quantity.From(1)]));
    await app.StartAsync();

    using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
    // "missing" rather than a NullReferenceException, so a document without the node fails the
    // assertion that names it.
    var document = JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json"))!;
    var schemas = document["components"]?["schemas"];
    Console.WriteLine($"openapi-component={schemas?["OrderId"]?.ToJsonString() ?? "missing"}");
    Console.WriteLine($"openapi-parameter={document["paths"]?["/orders/{id}"]?["get"]?["parameters"]?[0]?["schema"]?.ToJsonString() ?? "missing"}");
    Console.WriteLine($"openapi-items={schemas?["OrderView"]?["properties"]?["quantities"]?["items"]?.ToJsonString() ?? "missing"}");
    Console.WriteLine($"bound={(int)(await http.GetAsync($"/orders/{Guid.NewGuid()}")).StatusCode}");
    Console.WriteLine($"malformed={(int)(await http.GetAsync("/orders/nope")).StatusCode}");
    await app.StopAsync();
}

// EF Core: mapped with nothing registered per type, round-tripped, and .Value translated.
await using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var options = new DbContextOptionsBuilder<ShopDb>().UseSqlite(connection).UseTypeKit().Options;

await using (var db = new ShopDb(options))
{
    db.Database.EnsureCreated();
    db.Orders.Add(new Order { Id = OrderId.New(), Code = ProductCode.FromKnownGood("ABC"), Quantity = Quantity.From(2) });
    await db.SaveChangesAsync();
}

await using (var db = new ShopDb(options))
{
    var query = db.Orders.Where(o => o.Code.Value.StartsWith("AB"));
    Console.WriteLine($"sql={query.ToQueryString().ReplaceLineEndings(" ")}");
    var found = await query.SingleAsync();
    Console.WriteLine($"found={found.Code.Value}:{found.Quantity.Value}");
}

public sealed record OrderView(OrderId Id, List<Quantity> Quantities);

public sealed class Order
{
    public OrderId Id { get; set; }
    public ProductCode Code { get; set; }
    public Quantity Quantity { get; set; }
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
