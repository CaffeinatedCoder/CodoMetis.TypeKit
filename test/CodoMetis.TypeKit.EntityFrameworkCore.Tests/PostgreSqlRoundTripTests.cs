using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A real PostgreSQL: every wrapped-type family survives a round trip, stored values are read back
/// without the rules, and the translated queries return the rows they should.
/// </summary>
public sealed class PostgreSqlRoundTripTests(PostgreSqlRoundTripTests.Database database) : IClassFixture<PostgreSqlRoundTripTests.Database>
{
    [Fact]
    public async Task Every_value_object_round_trips_and_the_navigation_loads()
    {
        var (customer, saved) = await database.Seed();

        await using var db = database.Create();
        var loaded = await db.Orders.Include(o => o.Customer).SingleAsync(o => o.Id == saved.Id, TestContext.Current.CancellationToken);

        loaded.CustomerId.ShouldBe(customer.Id);
        loaded.Customer!.Name.ShouldBe(customer.Name);
        loaded.Code.ShouldBe(saved.Code);
        loaded.Discount.ShouldBe(saved.Discount);
        loaded.Quantity.ShouldBe(saved.Quantity);
        loaded.Amount.ShouldBe(saved.Amount);
        loaded.Urgent.ShouldBe(saved.Urgent);
        loaded.PlacedAt.ShouldBe(saved.PlacedAt);
        loaded.DueOn.ShouldBe(saved.DueOn);
        loaded.ConfirmedAt.ShouldBe(saved.ConfirmedAt);
        loaded.Slot.ShouldBe(saved.Slot);
        loaded.Note.ShouldBe(saved.Note);
        loaded.Tags.ShouldBe(saved.Tags);
    }

    [Fact]
    public async Task An_absent_optional_value_object_round_trips_as_null()
    {
        var (_, saved) = await database.Seed(order => { order.Discount = null; order.Note = null; });

        await using var db = database.Create();
        var loaded = await db.Orders.SingleAsync(o => o.Id == saved.Id, TestContext.Current.CancellationToken);

        loaded.Discount.ShouldBeNull();
        loaded.Note.ShouldBeNull();
    }

    /// <summary>
    /// A rule added after a row was written must not make it unreadable: the column is read through
    /// the materializer. Both values here are ones <c>Create</c> refuses today.
    /// </summary>
    [Fact]
    public async Task A_stored_value_the_rules_now_refuse_is_read_back_as_stored()
    {
        var (_, saved) = await database.Seed();

        await using (var db = database.Create())
            await db.Database.ExecuteSqlAsync($"UPDATE \"Orders\" SET \"Code\" = 'lower', \"Discount\" = 101 WHERE \"Id\" = {saved.Id.Value}", TestContext.Current.CancellationToken);

        await using var read = database.Create();
        var loaded = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == saved.Id, TestContext.Current.CancellationToken);

        loaded.Code.Value.ShouldBe("lower");
        loaded.Discount!.Value.Value.ShouldBe(101);

        // And found again: Revalidate applies today's rules to what was read.
        loaded.Code.Revalidate().TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(ProbeCodeFault.NotUpperCase);
        loaded.Discount.Value.Revalidate().TryGetValue(out _, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_translated_queries_find_the_row()
    {
        var (_, saved) = await database.Seed(order => order.Code = ProbeCode.FromKnownGood("QUERY"));
        List<OrderId> ids = [saved.Id];
        var tag = saved.Tags[0];
        var cancellation = TestContext.Current.CancellationToken;

        await using var db = database.Create();

        (await db.Orders.CountAsync(o => ids.Contains(o.Id), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Code.Value.StartsWith("QUE"), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Discount.ValueOrNull() > 10, cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Tags.Contains(tag), cancellation)).ShouldBe(1);
    }

    /// <summary>
    /// <c>.Value</c> on the element of a collection: a column of the order, and a parameter list.
    /// Both failed to translate while the element column was re-typed.
    /// </summary>
    [Fact]
    public async Task Value_on_a_collection_element_finds_the_row()
    {
        var (_, saved) = await database.Seed(order => order.Tags = [Tag.From("needle"), Tag.From("hay")]);
        List<OrderId> ids = [saved.Id];
        var cancellation = TestContext.Current.CancellationToken;

        await using var db = database.Create();

        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Tags.Any(t => t.Value == "needle"), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Tags.Any(t => t.Value.StartsWith("nee")), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => ids.Any(id => id.Value == o.Id.Value), cancellation)).ShouldBe(1);
    }

    /// <summary>One PostgreSQL container and one schema for the class.</summary>
    public sealed class Database : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        private DbContextOptions<TestDb>? _options;

        public TestDb Create() => new(_options ?? throw new InvalidOperationException("The database is not started."));

        public async Task<(Customer Customer, Order Order)> Seed(Action<Order>? adjust = null)
        {
            var customer = new Customer { Id = CustomerId.New(), Name = $"customer {Guid.NewGuid():N}" };
            var order    = Order.Sample(customer.Id);
            adjust?.Invoke(order);

            await using var db = Create();
            db.AddRange(customer, order);
            await db.SaveChangesAsync();

            return (customer, order);
        }

        public async ValueTask InitializeAsync()
        {
            await _container.StartAsync();

            // Not the container's default database, which EnsureCreated could not drop and recreate.
            var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "typekit" }.ConnectionString;
            _options = new DbContextOptionsBuilder<TestDb>().UseNpgsql(connectionString).UseTypeKit().Options;

            await using var db = Create();
            await db.Database.EnsureCreatedAsync();
        }

        public async ValueTask DisposeAsync() => await _container.DisposeAsync();
    }
}
