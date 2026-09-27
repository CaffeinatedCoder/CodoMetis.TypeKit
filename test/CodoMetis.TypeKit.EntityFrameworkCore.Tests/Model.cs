using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct CustomerId : IValue<Guid>;

public readonly partial record struct Tag : IValue<string>;

public sealed class Customer
{
    public CustomerId Id { get; set; }

    public string Name { get; set; } = "";

    public List<Order> Orders { get; } = [];
}

/// <summary>One property per wrapped-type family, plus a key, a foreign key, a primitive collection and a plain nullable int.</summary>
public sealed class Order
{
    public OrderId Id { get; set; }

    public CustomerId CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public ProbeCode Code { get; set; }

    public ProbePercentage? Discount { get; set; }

    public ProbeCount Quantity { get; set; }

    public ProbeAmount Amount { get; set; }

    public ProbeFlag Urgent { get; set; }

    public ProbeTimestamp PlacedAt { get; set; }

    public ProbeDate DueOn { get; set; }

    public ProbeMoment ConfirmedAt { get; set; }

    public ProbeTime Slot { get; set; }

    public ProbeLabel? Note { get; set; }

    public List<Tag> Tags { get; set; } = [];

    /// <summary>Not a value object: its <c>.Value</c> must stay EF's own translation.</summary>
    public int? Priority { get; set; }

    public static Order Sample(CustomerId customer) => new()
    {
        Id          = OrderId.New(),
        CustomerId  = customer,
        Code        = ProbeCode.FromKnownGood("ABC"),
        Discount    = ProbePercentage.FromKnownGood(15),
        Quantity    = ProbeCount.From(3),
        Amount      = ProbeAmount.From(12.50m),
        Urgent      = ProbeFlag.From(true),
        PlacedAt    = ProbeTimestamp.From(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc)),
        DueOn       = ProbeDate.From(new DateOnly(2026, 10, 1)),
        ConfirmedAt = ProbeMoment.From(new DateTimeOffset(2026, 9, 27, 12, 30, 0, TimeSpan.Zero)),
        Slot        = ProbeTime.From(new TimeOnly(9, 30)),
        Note        = ProbeLabel.From("fragile"),
        Tags        = [Tag.From("x"), Tag.From("y")],
        Priority    = 5,
    };
}

public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();

    public static DbContextOptions<TestDb> Sqlite(Microsoft.Data.Sqlite.SqliteConnection connection) =>
        new DbContextOptionsBuilder<TestDb>().UseSqlite(connection).UseTypeKit().Options;

    /// <summary>For SQL text only: <c>ToQueryString</c> never opens the connection.</summary>
    public static DbContextOptions<TestDb> NpgsqlWithoutServer() =>
        new DbContextOptionsBuilder<TestDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options;
}
