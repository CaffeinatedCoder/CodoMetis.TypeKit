using CodoMetis.TypeKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Testcontainers.PostgreSql;

await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
await postgres.StartAsync();

var sqlite = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
sqlite.Open();

foreach (var (provider, configure) in new (string, Action<DbContextOptionsBuilder>)[]
         {
             ("SQLite", o => o.UseSqlite(sqlite)),
             ("PostgreSQL", o => o.UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = "efmapping" }.ConnectionString)),
         })
{
    foreach (var (mechanism, apply) in new (string, Func<DbContextOptionsBuilder, Action<DbContextOptionsBuilder>, DbContextOptionsBuilder>)[]
             {
                 ("selector, registered after the provider", (o, p) => { p(o); return o.ReplaceService<IValueConverterSelector, ValueObjectConverterSelector>(); }),
                 ("selector, registered before the provider", (o, p) => { o.ReplaceService<IValueConverterSelector, ValueObjectConverterSelector>(); p(o); return o; }),
                 ("none (control)", (o, p) => { p(o); return o; }),
                 ("selector, then another library's selector", (o, p) => { p(o); o.ReplaceService<IValueConverterSelector, ValueObjectConverterSelector>(); return o.ReplaceService<IValueConverterSelector, OtherLibrarySelector>(); }),
                 ("another library's selector, then the selector", (o, p) => { p(o); o.ReplaceService<IValueConverterSelector, OtherLibrarySelector>(); return o.ReplaceService<IValueConverterSelector, ValueObjectConverterSelector>(); }),
                 ("plugin", (o, p) => { p(o); ((Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsBuilderInfrastructure)o).AddOrUpdateExtension(new PluginExtension()); return o; }),
                 ("plugin, beside another library's selector", (o, p) => { p(o); ((Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsBuilderInfrastructure)o).AddOrUpdateExtension(new PluginExtension()); return o.ReplaceService<IValueConverterSelector, OtherLibrarySelector>(); }),
             })
    {
        var options = (DbContextOptions<Db>)apply(new DbContextOptionsBuilder<Db>(), configure).Options;
        Console.WriteLine($"=== {provider} / {mechanism}");
        await Measure(() => new Db(options), provider);
    }
}

static async Task Measure(Func<Db> create, string provider)
{
    IModel model;
    try
    {
        await using var db = create();
        model = db.Model;
    }
    catch (Exception e)
    {
        Console.WriteLine($"  model: FAIL {e.GetType().Name}: {First(e.Message)}");
        return;
    }

    var order = model.FindEntityType(typeof(Order))!;
    foreach (var name in new[] { nameof(Order.Id), nameof(Order.CustomerId), nameof(Order.Code), nameof(Order.Discount), nameof(Order.Tags) })
    {
        var property = order.FindProperty(name);
        if (property is null) { Console.WriteLine($"  {name,-11}: NOT MAPPED as a property"); continue; }

        var converter = property.GetTypeMapping().Converter;
        var element   = property.GetElementType();
        Console.WriteLine($"  {name,-11}: {property.GetColumnType(),-22} key={property.IsPrimaryKey(),-5} fk={property.IsForeignKey(),-5} nullable={property.IsNullable,-5} " +
                          $"converter={converter?.GetType().Name ?? "-"}{(element is null ? "" : $" element={element.ClrType.Name} elementConverter={element.GetTypeMapping().Converter?.GetType().Name ?? "-"}")}");
    }

    await Probe("round trip", async () =>
    {
        await using var db = create();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var customer = new Customer { Id = CustomerId.New(), Name = "c" };
        var saved = new Order { Id = OrderId.New(), CustomerId = customer.Id, Code = Code.FromKnownGood("ABC"), Discount = Quantity.From(5), Tags = [Tag.From("x"), Tag.From("y")] };
        db.AddRange(customer, saved);
        await db.SaveChangesAsync();

        await using var read = create();
        var loaded = await read.Orders.Include(o => o.Customer).SingleAsync();
        return loaded.Id == saved.Id && loaded.CustomerId == customer.Id && loaded.Customer!.Name == "c" && loaded.Code == saved.Code
            && loaded.Discount == saved.Discount && loaded.Tags.SequenceEqual(saved.Tags)
                   ? "values equal, navigation loaded"
                   : "MISMATCH";
    });

    await Probe("stored value Create refuses", async () =>
    {
        await using var db = create();
        var table = provider == "PostgreSQL" ? "\"Orders\"" : "Orders";
        var column = provider == "PostgreSQL" ? "\"Code\"" : "Code";
        await db.Database.ExecuteSqlRawAsync($"UPDATE {table} SET {column} = 'lower'");
        return (await db.Orders.AsNoTracking().SingleAsync()).Code.Value;
    });

    var id = OrderId.New();
    List<OrderId> ids = [id, OrderId.New()];
    var tag = Tag.From("x");
    await Query("equality", create, db => db.Orders.Where(o => o.Id == id));
    await Query("Contains over a list", create, db => db.Orders.Where(o => ids.Contains(o.Id)));
    await Query("nullable compare", create, db => db.Orders.Where(o => o.Discount == Quantity.From(5)));
    await Query("primitive collection Contains", create, db => db.Orders.Where(o => o.Tags.Contains(tag)));
    await Query("OrderBy", create, db => db.Orders.OrderBy(o => o.Id));
    await Query(".Value (no translator)", create, db => db.Orders.Where(o => o.Code.Value.StartsWith("A")));
}

static async Task Query(string name, Func<Db> create, Func<Db, IQueryable<Order>> query)
{
    await Probe(name, async () =>
    {
        await using var db = create();
        var q = query(db);
        var sql = q.ToQueryString().Split('\n').Last(line => line.Contains("WHERE") || line.Contains("ORDER BY") || line.Contains("FROM")).Trim();
        var count = await q.CountAsync();
        return $"{sql}   [{count} row(s)]";
    });
}

static async Task Probe(string name, Func<Task<string>> probe)
{
    try { Console.WriteLine($"  {name}: {await probe()}"); }
    catch (Exception e)
    {
        var inner = e; while (inner.InnerException is not null) inner = inner.InnerException;
        Console.WriteLine($"  {name}: FAIL {e.GetType().Name}: {First(e.Message)}{(inner == e ? "" : $" <- {inner.GetType().Name}: {First(inner.Message)}")}");
    }
}

static string First(string message) { var line = message.Split('\n')[0]; return line[..Math.Min(170, line.Length)]; }

public sealed class Db(DbContextOptions<Db> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
}

/// <summary>Registers the plugin the way the package's options extension would.</summary>
public sealed class PluginExtension : Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsExtension
{
    public Microsoft.EntityFrameworkCore.Infrastructure.DbContextOptionsExtensionInfo Info => new PluginInfo(this);

    public void ApplyServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
        new Microsoft.EntityFrameworkCore.Infrastructure.EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<Microsoft.EntityFrameworkCore.Storage.IRelationalTypeMappingSourcePlugin, ValueObjectTypeMappingSourcePlugin>();

    public void Validate(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions options) { }

    private sealed class PluginInfo(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsExtension e) : Microsoft.EntityFrameworkCore.Infrastructure.DbContextOptionsExtensionInfo(e)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "plugin ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(Microsoft.EntityFrameworkCore.Infrastructure.DbContextOptionsExtensionInfo other) => other is PluginInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["plugin"] = "1";
    }
}
