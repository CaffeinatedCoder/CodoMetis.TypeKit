using CodoMetis.TypeKit;
using Microsoft.EntityFrameworkCore;
using Shop;

async Task Check(string name, Func<Task<string>> body)
{
    try { Console.WriteLine($"{name}={await body()}"); }
    catch (Exception e) { Console.WriteLine($"{name}=THREW {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}"); }
}

var id = OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee"));

await using var db = new StoreDb();
await db.Database.OpenConnectionAsync();
await db.Database.ExecuteSqlRawAsync("CREATE TABLE Orders (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL, Quantity INTEGER NOT NULL, Status TEXT NOT NULL, Delivery TEXT NOT NULL)");
await Check("ef-insert", async () =>
{
    db.Orders.Add(new Order { Id = id, Code = Code.FromKnownGood("ABC"), Quantity = Quantity.From(2), Status = OrderStatus.Shipped, Delivery = new() { Status = OrderStatus.Open, Quantity = Quantity.From(3) } });
    return (await db.SaveChangesAsync()).ToString();
});
db.ChangeTracker.Clear();
await Check("ef-set-stored", async () =>
{
    await using var command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = "SELECT Status || ' ' || Delivery FROM Orders";
    return (string)(await command.ExecuteScalarAsync())!;
});
var shipped = OrderStatus.Shipped;
await Check("ef-set-parameter", async () => (await db.Orders.Where(o => o.Status == shipped).SingleAsync()).Status.Name);
db.ChangeTracker.Clear();
await Check("ef-json-column", async () =>
{
    var order = await db.Orders.Where(o => o.Delivery.Status == OrderStatus.Open).SingleAsync();
    return $"{order.Delivery.Status.Name} {order.Delivery.Quantity.Value}";
});
db.ChangeTracker.Clear();
await Check("ef-value", async () => (await db.Orders.Where(o => o.Code.Value.StartsWith("AB")).SingleAsync()).Quantity.Value.ToString());
db.ChangeTracker.Clear();
await Check("ef-parameter", async () => (await db.Orders.Where(o => o.Id == id).SingleAsync()).Code.Value);
db.ChangeTracker.Clear();
await Check("ef-stored", async () =>
{
    await db.Database.ExecuteSqlRawAsync("UPDATE Orders SET Code = 'lower'");
    return (await db.Orders.Where(o => o.Id == id).SingleAsync()).Code.Value;
});
db.ChangeTracker.Clear();
// The query helpers compose the query inside the package, where EF's precompiler, which works on
// the application's own call sites, never sees it: measured here, with the workaround the docs give.
await Check("ef-first-or-none", () => Task.FromResult(db.Orders.FirstOrNone(o => o.Code.Value.StartsWith("lo")).IsSome().ToString()));
db.ChangeTracker.Clear();
await Check("ef-first-or-none-async", async () => (await db.Orders.FirstOrNoneAsync(o => o.Code.Value.StartsWith("lo"))).IsSome().ToString());
db.ChangeTracker.Clear();
await Check("ef-first-or-none-written", async () => (await db.Orders.Where(o => o.Code.Value.StartsWith("lo")).Take(1).ToListAsync()).FirstOrNone().IsSome().ToString());
db.ChangeTracker.Clear();
await db.Database.ExecuteSqlRawAsync("CREATE TABLE Shipments (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Carrier TEXT NOT NULL)");
await Check("ef-identity", async () =>
{
    var first  = new Shipment { Carrier = "a" };
    var second = new Shipment { Carrier = "b" };
    db.Shipments.AddRange(first, second);
    await db.SaveChangesAsync();
    return $"{first.Id.Value},{second.Id.Value}";
});

// Not sealed: EF 10's precompiled queries cast an entity to an internal interface, which does not
// compile for a sealed class.
public class Order
{
    public OrderId Id { get; set; }
    public Code Code { get; set; } = null!;
    public Quantity Quantity { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public Delivery Delivery { get; set; } = new();
}

// Mapped to a JSON column: a closed set and a value object inside it.
public class Delivery
{
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public Quantity Quantity { get; set; }
}

// A key over an integer, which UseTypeKit() makes an identity key as it would be for an int.
public class Shipment
{
    public ShipmentNo Id { get; set; }
    public string Carrier { get; set; } = "";
}

public sealed class StoreDb : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:").UseTypeKit();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>().ComplexProperty(o => o.Delivery, delivery => delivery.ToJson());
}
