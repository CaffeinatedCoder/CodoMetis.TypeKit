using CodoMetis.TypeKit;
using CodoMetis.TypeKit.EntityFrameworkCore;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace ReadmeSamples.EfCore;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct CustomerId : IValue<Guid>;

public enum CodeFault { Blank }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value) =>
        string.IsNullOrWhiteSpace(value) ? Result.Error(CodeFault.Blank) : new ProductCode(value);
}

public readonly partial record struct Discount : IValue<int>;

public readonly partial record struct Amount : IValue<decimal>;

public readonly partial record struct PlacedAt : IValue<DateTime>;

public readonly partial record struct Tag : IValue<string>;

// sample: CodoMetis.TypeKit.EntityFrameworkCore/entity
public class Order
{
    public required OrderId Id { get; init; }              // key: uuid
    public required CustomerId CustomerId { get; set; }    // foreign key: uuid
    public required ProductCode Code { get; set; }         // text, keeps HasMaxLength(10)
    public Discount? Discount { get; set; }                // nullable integer
    public required Amount Total { get; set; }             // numeric
    public required PlacedAt PlacedAt { get; set; }        // timestamp with time zone on PostgreSQL
    public List<Tag> Tags { get; set; } = [];              // primitive collection: text[] on PostgreSQL, JSON elsewhere
}
// end sample

/// <summary>The principal of the order's foreign key.</summary>
public sealed class Customer
{
    public required CustomerId Id { get; init; }

    public List<Order> Orders { get; } = [];
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>The facet the entity sample says is kept.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>().Property(o => o.Code).HasMaxLength(10);
}

/// <summary>A model that names the converter itself, apart from <see cref="ShopDb"/>, whose SQL the query sample states.</summary>
public sealed class ExplicitConverterDb(DbContextOptions<ExplicitConverterDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // sample: CodoMetis.TypeKit.EntityFrameworkCore/explicit-converter
        modelBuilder.Entity<Order>().Property(o => o.Code).HasConversion<ValueObjectConverter<ProductCode, string>>();
        // end sample
    }
}

public interface IRefusalLog
{
    void StoredCodeNowRefused(OrderId order);
}

public static class EntityFrameworkCoreSamples
{
    public static void AddShopDb(IServiceCollection services, string connectionString)
    {
        // sample: CodoMetis.TypeKit.EntityFrameworkCore/setup
        services.AddDbContext<ShopDb>(options =>
            options.UseNpgsql(connectionString)
                   .UseTypeKit());
        // end sample
    }

    public static IServiceProvider InternalServices()
    {
        // sample: CodoMetis.TypeKit.EntityFrameworkCore/internal-service-provider
        var internalServices = new ServiceCollection()
            .AddEntityFrameworkNpgsql()
            .AddEntityFrameworkTypeKit()
            .BuildServiceProvider();
        // end sample

        return internalServices;
    }

    public static void Query(ShopDb db, OrderId id, List<OrderId> ids, Tag tag)
    {
        // sample: CodoMetis.TypeKit.EntityFrameworkCore/queries
        db.Orders.Where(o => o.Id == id);                            // WHERE o."Id" = @id
        db.Orders.Where(o => ids.Contains(o.Id));                    // WHERE o."Id" = ANY (@ids)
        db.Orders.Where(o => o.Code.Value.StartsWith("A"));          // WHERE o."Code" LIKE 'A%'
        db.Orders.Where(o => o.Total.GetValue() > 100m);             // WHERE o."Total" > 100.0
        db.Orders.Where(o => o.Discount.ValueOrNull() > 10);         // WHERE o."Discount" > 10
        db.Orders.Where(o => o.Tags.Contains(tag));                  // WHERE @tag = ANY (o."Tags")
        db.Orders.Where(o => o.Tags.Any(t => t.Value == "x"));       // WHERE EXISTS (SELECT 1 FROM unnest(o."Tags") AS t(value) WHERE t.value::text = 'x')
        db.Orders.OrderBy(o => o.PlacedAt);
        // end sample
    }

    public static async Task FindRefused(ShopDb db, IRefusalLog log)
    {
        // sample: CodoMetis.TypeKit.EntityFrameworkCore/revalidate
        var orders = await db.Orders.AsNoTracking().ToListAsync();
        foreach (var order in orders.Where(o => !o.Code.Revalidate()))
            log.StoredCodeNowRefused(order.Id);
        // end sample
    }
}

public sealed class EntityFrameworkCoreTests
{
    private const string Readme = "src/CodoMetis.TypeKit.EntityFrameworkCore/README.md";

    /// <summary>Each property's comment in the entity sample names what the model maps it to on PostgreSQL.</summary>
    [Fact]
    public void The_columns_are_what_the_entity_s_comments_say()
    {
        using var db = new ShopDb(SampleOutputs.NpgsqlWithoutServer<ShopDb>());
        var order = db.Model.FindEntityType(typeof(Order))!;
        var comments = SampleOutputs.PropertyComments(Readme, "public class Order");
        IProperty Property(string name) => order.FindProperty(name)!;

        comments.Keys.ShouldBe(order.GetProperties().Select(property => property.Name), ignoreOrder: true);

        order.FindPrimaryKey()!.Properties.ShouldBe([Property(nameof(Order.Id))]);
        comments[nameof(Order.Id)].ShouldBe($"key: {Property(nameof(Order.Id)).GetColumnType()}");

        order.GetForeignKeys().SelectMany(key => key.Properties).ShouldBe([Property(nameof(Order.CustomerId))]);
        comments[nameof(Order.CustomerId)].ShouldBe($"foreign key: {Property(nameof(Order.CustomerId)).GetColumnType()}");

        comments[nameof(Order.Code)].ShouldContain($"HasMaxLength({Property(nameof(Order.Code)).GetMaxLength()})");

        Property(nameof(Order.Discount)).IsNullable.ShouldBeTrue();
        comments[nameof(Order.Discount)].ShouldBe($"nullable {Property(nameof(Order.Discount)).GetColumnType()}");

        comments[nameof(Order.Total)].ShouldBe(Property(nameof(Order.Total)).GetColumnType());
        comments[nameof(Order.PlacedAt)].ShouldBe($"{Property(nameof(Order.PlacedAt)).GetColumnType()} on PostgreSQL");
        comments[nameof(Order.Tags)].ShouldContain($"{Property(nameof(Order.Tags)).GetColumnType()} on PostgreSQL");
    }

    [Fact]
    public void The_queries_are_the_SQL_the_comments_say()
    {
        using var db = new ShopDb(SampleOutputs.NpgsqlWithoutServer<ShopDb>());
        var id = OrderId.New();
        List<OrderId> ids = [id];
        var tag = Tag.From("x");

        db.Orders.Where(o => o.Id == id).ShouldBeTheSqlShownIn(Readme);
        db.Orders.Where(o => ids.Contains(o.Id)).ShouldBeTheSqlShownIn(Readme);
        db.Orders.Where(o => o.Code.Value.StartsWith("A")).ShouldBeTheSqlShownIn(Readme);
        db.Orders.Where(o => o.Total.GetValue() > 100m).ShouldBeTheSqlShownIn(Readme);
        db.Orders.Where(o => o.Discount.ValueOrNull() > 10).ShouldBeTheSqlShownIn(Readme);
        db.Orders.Where(o => o.Tags.Contains(tag)).ShouldBeTheSqlShownIn(Readme);
        db.Orders.Where(o => o.Tags.Any(t => t.Value == "x")).ShouldBeTheSqlShownIn(Readme);
    }

    /// <summary>The internal service provider the sample builds maps value objects, with nothing else registered.</summary>
    [Fact]
    public void The_internal_service_provider_maps_value_objects()
    {
        var options = new DbContextOptionsBuilder<ShopDb>()
                      .UseNpgsql(SampleOutputs.ConnectionStringWithoutServer)
                      .UseInternalServiceProvider(EntityFrameworkCoreSamples.InternalServices())
                      .Options;
        using var db = new ShopDb(options);

        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Id))!.GetColumnType().ShouldBe("uuid");
    }

    [Fact]
    public void The_explicit_converter_is_the_one_named()
    {
        using var db = new ExplicitConverterDb(SampleOutputs.NpgsqlWithoutServer<ExplicitConverterDb>());

        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Code))!.GetValueConverter().ShouldBeOfType<ValueObjectConverter<ProductCode, string>>();
    }
}
