using System.Globalization;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A real SQL Server, as <see cref="PostgreSqlRoundTripTests"/> is a real PostgreSQL: every wrapped-type family survives
/// a round trip, stored values are read back without the rules, the translated queries return the rows they should,
/// and what SQL Server does differently from memory (design/knowledge KB-048 to KB-054) a value object does as the
/// type it wraps does.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Needs", "Docker")]
public sealed class SqlServerRoundTripTests(SqlServer server)
{
    private async Task<TestDb> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<TestDb>().UseSqlServer(await server.DatabaseAsync("typekit_round_trips")).UseTypeKit()
                                                           .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                                                           .Options;
        var db = new TestDb(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private async Task<(Customer Customer, Order Order)> SeedAsync(Action<Order>? adjust = null)
    {
        var customer = new Customer { Id = CustomerId.New(), Name = $"customer {Guid.NewGuid():N}" };
        var order    = Order.Sample(customer.Id);
        adjust?.Invoke(order);

        await using var db = await CreateAsync();
        db.AddRange(customer, order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (customer, order);
    }

    [Fact]
    public async Task Every_value_object_round_trips_and_the_navigation_loads()
    {
        var (customer, saved) = await SeedAsync();

        await using var db = await CreateAsync();
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

        // KB-054: datetime2 keeps the ticks, and reads a DateTime back without its kind, as a plain DateTime column does.
        loaded.PlacedAt.Value.Ticks.ShouldBe(saved.PlacedAt.Value.Ticks);
        loaded.PlacedAt.Value.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Fact]
    public async Task An_absent_optional_value_object_round_trips_as_null()
    {
        var (_, saved) = await SeedAsync(order => { order.Discount = null; order.Note = null; });

        await using var db = await CreateAsync();
        var loaded = await db.Orders.SingleAsync(o => o.Id == saved.Id, TestContext.Current.CancellationToken);

        loaded.Discount.ShouldBeNull();
        loaded.Note.ShouldBeNull();
    }

    /// <summary>A rule added after a row was written must not make it unreadable: the column is read through the materializer.</summary>
    [Fact]
    public async Task A_stored_value_the_rules_now_refuse_is_read_back_as_stored()
    {
        var (_, saved) = await SeedAsync();

        await using (var db = await CreateAsync())
            await db.Database.ExecuteSqlAsync($"UPDATE [Orders] SET [Code] = 'lower', [Discount] = 101 WHERE [Id] = {saved.Id.Value}", TestContext.Current.CancellationToken);

        await using var read = await CreateAsync();
        var loaded = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == saved.Id, TestContext.Current.CancellationToken);

        loaded.Code.Value.ShouldBe("lower");
        loaded.Discount!.Value.Value.ShouldBe(101);
        loaded.Code.Revalidate().TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.ShouldBe(ProbeCodeFault.NotUpperCase);
    }

    [Fact]
    public async Task The_translated_queries_find_the_row()
    {
        var (_, saved) = await SeedAsync(order => order.Code = ProbeCode.FromKnownGood("QUERY"));
        List<OrderId> ids = [saved.Id];
        var tag = saved.Tags[0];
        var cancellation = TestContext.Current.CancellationToken;

        await using var db = await CreateAsync();

        (await db.Orders.CountAsync(o => ids.Contains(o.Id), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Code.Value.StartsWith("QUE"), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Discount.ValueOrNull() > 10, cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Tags.Contains(tag), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => o.Id == saved.Id && o.Tags.Any(t => t.Value == "x"), cancellation)).ShouldBe(1);
        (await db.Orders.CountAsync(o => ids.Any(id => id.Value == o.Id.Value), cancellation)).ShouldBe(1);
    }

    /// <summary>A key over an integer is an identity column, as an <c>int</c> key is, and SQL Server numbers it.</summary>
    [Fact]
    public async Task An_integer_key_is_an_identity_column()
    {
        await using var db = await CreateAsync();

        SqlServerPropertyExtensions.GetValueGenerationStrategy(db.Model.FindEntityType(typeof(Shipment))!.FindProperty(nameof(Shipment.Id))!)
                                   .ShouldBe(SqlServerValueGenerationStrategy.IdentityColumn);

        var first  = new Shipment { Carrier = "a" };
        var second = new Shipment { Carrier = "b" };
        db.AddRange(first, second);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        first.Id.Value.ShouldBeGreaterThan(0);
        second.Id.ShouldNotBe(first.Id);
    }

    /// <summary>
    /// KB-048: under the default collation a value object over text is compared and matched ignoring case and trailing
    /// spaces, as the plain string column beside it is, where in memory its equality is ordinal.
    /// </summary>
    [Fact]
    public async Task Text_is_compared_by_the_collation_as_a_plain_string_column_is()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var (customer, saved) = await SeedAsync(order => { order.Note = ProbeLabel.From($"Fragile {marker}"); order.Tags = [Tag.From($"Needle {marker}")]; });
        var cancellation = TestContext.Current.CancellationToken;
        var shouted = ProbeLabel.From($"FRAGILE {marker}");
        List<ProbeLabel> notes = [shouted];

        shouted.ShouldNotBe(saved.Note, "in memory, a value object over text is equal ordinally");

        await using var db = await CreateAsync();
        var mine = db.Orders.Where(o => o.Id == saved.Id);

        (await mine.CountAsync(o => o.Note == shouted, cancellation)).ShouldBe(1);
        (await mine.CountAsync(o => o.Note == ProbeLabel.From($"Fragile {marker}  "), cancellation)).ShouldBe(1, "trailing spaces");
        (await mine.CountAsync(o => notes.Contains(o.Note!), cancellation)).ShouldBe(1);
        (await mine.CountAsync(o => o.Code.Value.StartsWith("ab"), cancellation)).ShouldBe(1);
        (await mine.CountAsync(o => o.Tags.Any(t => t.Value == $"needle {marker}"), cancellation)).ShouldBe(1);
        (await db.Customers.CountAsync(c => c.Id == customer.Id && c.Name == customer.Name.ToUpperInvariant(), cancellation)).ShouldBe(1, "the plain string column");
    }

    /// <summary>
    /// KB-050: a decimal value object without a configured precision is a <c>decimal(18,2)</c> column, as a plain
    /// <c>decimal</c> is: more fractional digits are rounded on write, and the value reads back at scale 2.
    /// </summary>
    [Fact]
    public async Task A_decimal_value_object_is_rounded_to_the_column_s_scale_as_a_plain_decimal_is()
    {
        var (_, saved) = await SeedAsync(order => order.Amount = ProbeAmount.From(12.505m));

        await using var db = await CreateAsync();
        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Amount))!.GetColumnType().ShouldBe("decimal(18,2)");
        var loaded = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == saved.Id, TestContext.Current.CancellationToken);

        loaded.Amount.Value.ToString(CultureInfo.InvariantCulture).ShouldBe("12.51");
    }
}
