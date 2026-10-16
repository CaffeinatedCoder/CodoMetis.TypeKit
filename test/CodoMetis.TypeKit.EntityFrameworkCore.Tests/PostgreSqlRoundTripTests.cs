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

    /// <summary>A key over an integer is an identity column, as an <c>int</c> key is.</summary>
    [Fact]
    public async Task An_integer_key_is_an_identity_column()
    {
        var cancellation = TestContext.Current.CancellationToken;

        await using var db = database.Create();

        NpgsqlPropertyExtensions.GetValueGenerationStrategy(db.Model.FindEntityType(typeof(Shipment))!.FindProperty(nameof(Shipment.Id))!)
                                .ShouldBe(Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

        var first  = new Shipment { Carrier = "a" };
        var second = new Shipment { Carrier = "b" };
        db.AddRange(first, second);
        await db.SaveChangesAsync(cancellation);

        first.Id.Value.ShouldBeGreaterThan(0);
        second.Id.ShouldNotBe(first.Id);
    }

    /// <summary>
    /// A key over each integer type PostgreSQL has an identity column for, added twice: <c>sbyte</c> as
    /// <c>smallint</c>, <c>ushort</c> as <c>integer</c>, <c>uint</c> as <c>bigint</c>, as their own keys are.
    /// Not <c>ulong</c>, a <c>numeric(20,0)</c> that cannot be an identity, nor <c>char</c>, a
    /// <c>character(1)</c> PostgreSQL refuses one on, either of them for a plain key too.
    /// </summary>
    [Fact]
    public async Task A_key_over_each_integer_type_with_an_identity_is_numbered_by_the_database()
    {
        var cancellation = TestContext.Current.CancellationToken;

        await using var db = new PostgreSqlIntegerKeysDb(database.Options<PostgreSqlIntegerKeysDb>("typekit_keys"));
        await db.Database.EnsureCreatedAsync(cancellation);

        KeyedByInt16[]  shorts  = [new(), new()];
        KeyedBySByte[]  sbytes  = [new(), new()];
        KeyedByUInt16[] ushorts = [new(), new()];
        KeyedByUInt32[] uints   = [new(), new()];
        db.AddRange([.. shorts, .. sbytes, .. ushorts, .. uints]);
        await db.SaveChangesAsync(cancellation);

        shorts.Select(e => e.Id.Value).ShouldBe([(short)1, (short)2], ignoreOrder: true);
        sbytes.Select(e => e.Id.Value).ShouldBe([(sbyte)1, (sbyte)2], ignoreOrder: true);
        ushorts.Select(e => e.Id.Value).ShouldBe([(ushort)1, (ushort)2], ignoreOrder: true);
        uints.Select(e => e.Id.Value).ShouldBe([1u, 2u], ignoreOrder: true);
    }

    /// <summary>
    /// An abstract TPC root's key is numbered from the hierarchy's sequence, across its derived types'
    /// tables, as a <c>long</c> key is. Without the generation the first car was stored with 0 and the
    /// second refused as a duplicate.
    /// </summary>
    [Fact]
    public async Task An_abstract_TPC_root_s_key_is_numbered_from_its_sequence()
    {
        var cancellation = TestContext.Current.CancellationToken;

        await using var db = new AbstractTpcDb(database.Options<AbstractTpcDb>("typekit_tpc"));
        await db.Database.EnsureCreatedAsync(cancellation);

        AbstractTpcByValueObject[] vehicles =
            [new AbstractTpcByValueObjectCar { Name = "a" }, new AbstractTpcByValueObjectBike { Name = "b" }, new AbstractTpcByValueObjectCar { Name = "c" }];
        db.AddRange(vehicles);
        await db.SaveChangesAsync(cancellation);

        vehicles.Select(vehicle => vehicle.Id.Value).ShouldBe([1L, 2L, 3L], ignoreOrder: true);
    }

    /// <summary>
    /// A key over a Guid or a string configured <c>ValueGeneratedOnAdd()</c> gets a version 7 Guid from Npgsql, as
    /// the plain key does, and is stored and read back as generated. Npgsql chooses the generator by the
    /// property's CLR type, which gave the value object EF's random version 4 Guid.
    /// </summary>
    [Fact]
    public async Task A_key_generated_on_add_gets_a_version_7_Guid_as_the_plain_key_does()
    {
        var cancellation = TestContext.Current.CancellationToken;

        await using (var db = new PostgreSqlGeneratedKeysDb(database.Options<PostgreSqlGeneratedKeysDb>("typekit_generated_keys")))
        {
            await db.Database.EnsureCreatedAsync(cancellation);
            db.AddRange(new GeneratedByGuid(), new GeneratedByGuid(), new GeneratedByWrapsGuid(), new GeneratedByWrapsGuid(),
                        new GeneratedByString(), new GeneratedByString(), new GeneratedByWrapsString(), new GeneratedByWrapsString());
            await db.SaveChangesAsync(cancellation);
        }

        await using var read = new PostgreSqlGeneratedKeysDb(database.Options<PostgreSqlGeneratedKeysDb>("typekit_generated_keys"));
        (await read.Set<GeneratedByGuid>().Select(e => e.Id).ToListAsync(cancellation)).Select(id => id.Version).ShouldBe([7, 7]);
        (await read.Set<GeneratedByWrapsGuid>().Select(e => e.Id).ToListAsync(cancellation)).Select(id => id.Value.Version).ShouldBe([7, 7]);
        (await read.Set<GeneratedByString>().Select(e => e.Id).ToListAsync(cancellation)).Select(id => Guid.Parse(id).Version).ShouldBe([7, 7]);
        (await read.Set<GeneratedByWrapsString>().Select(e => e.Id).ToListAsync(cancellation)).Select(id => Guid.Parse(id.Value).Version).ShouldBe([7, 7]);
    }

    /// <summary>One PostgreSQL container and one schema for the class.</summary>
    public sealed class Database : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        private DbContextOptions<TestDb>? _options;

        public TestDb Create() => new(_options ?? throw new InvalidOperationException("The database is not started."));

        /// <summary>Another database in the same container, for a model of its own.</summary>
        public DbContextOptions<TDb> Options<TDb>(string databaseName) where TDb : DbContext =>
            new DbContextOptionsBuilder<TDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName }.ConnectionString)
                .UseTypeKit()
                .Options;

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
