using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// The arithmetic operators a value object declares, in a query, on every site a query reads one: each query
/// answers as the same query over the objects in memory, which runs the generated operators.
/// </summary>
/// <remarks>
/// <para>
/// EF translates an operator between two value objects as the database's arithmetic over the columns
/// (<c>ef_add("p"."Price", "p"."Tax")</c> on SQLite). With an operand or a result of another type
/// (<c>Price * 2m</c>, <c>Tax / Price</c> as a <see cref="decimal"/>) EF applied the value object's converter to
/// the other side, and the query threw: "Invalid cast from 'System.Decimal' to 'ProbeMoney'" and "No coercion operator
/// is defined between types 'ProbeMoney' and 'Nullable&lt;Decimal&gt;'" (measured with EF Core 10.0.12 on SQLite,
/// #186). Unary <c>+</c> was "could not be translated". <see cref="ValueObjectArithmetic"/> rewrites every
/// generated operator to the wrapped type's arithmetic.
/// </para>
/// <para>
/// The values divide exactly, so a database's decimal scale cannot tell its answer from memory's. SQLite orders and
/// aggregates no <see cref="decimal"/>, <c>.Value</c> neither, so those cases run on the servers alone.
/// </para>
/// </remarks>
public static class ArithmeticQueries
{
    private static readonly ProbeMoney Ten  = ProbeMoney.From(10m);
    private static readonly ProbeMoney Four = ProbeMoney.From(4m);

    /// <summary>One query, run against a database and against the same rows in memory; ordered, so the lists compare.</summary>
    public sealed record Case(Func<IQueryable<Priced>, IQueryable<Basket>, IEnumerable<object?>> Query, bool OrdersDecimals = false);

    public static readonly Dictionary<string, Case> Cases = new()
    {
        // Between two value objects into one, which EF translated right before, over TypeKit's converter.
        ["Price + Tax, filtered"]            = Ids(q => q.Where(p => p.Price + p.Tax > Four)),
        ["Price - Tax, filtered"]            = Ids(q => q.Where(p => p.Price - p.Tax < Four)),
        ["-Price, filtered"]                 = Ids(q => q.Where(p => -p.Price < -Four)),
        ["Price + Tax, projected"]           = Values(q => q.Select(p => p.Price + p.Tax)),

        // Another operand, or another result, which EF could not translate.
        ["Price * 2m, filtered"]             = Ids(q => q.Where(p => p.Price * 2m > Ten)),
        ["Price / 2m, filtered"]             = Ids(q => q.Where(p => p.Price / 2m >= ProbeMoney.From(2.5m))),
        ["Price % 2m, filtered"]             = Ids(q => q.Where(p => p.Price % 2m == ProbeMoney.From(1m))),
        ["Tax / Price, filtered"]            = Ids(q => q.Where(p => p.Tax / p.Price < 0.3m)),
        ["checked Price * 2m, filtered"]     = Ids(q => q.Where(p => checked(p.Price * 2m) > Ten)),
        ["+Price, filtered"]                 = Ids(q => q.Where(p => +p.Price > Four)),
        ["Price * a column, filtered"]       = Ids(q => q.Where(p => p.Price * p.Rate > Ten)),
        ["Price * a captured factor"]        = Ids(q => q.Where(p => p.Price * Factor() > Ten)),
        ["(Price + Tax) * 2m, filtered"]     = Ids(q => q.Where(p => (p.Price + p.Tax) * 2m > ProbeMoney.From(20m))),
        ["Price * 2m + Tax, filtered"]       = Ids(q => q.Where(p => p.Price * 2m + p.Tax > ProbeMoney.From(12m))),
        ["(Price * 2m).Value, filtered"]     = Ids(q => q.Where(p => (p.Price * 2m).Value > 10m)),

        // Past the column's scale, SQL Server's default decimal(18,2): cast to the column's store type, 5 / 32 = 0.15625 read
        // 0.16 there, and the filter lost the row (measured on Azure SQL Edge before the re-typing).
        ["Price / 32m past the scale"]       = Ids(q => q.Where(p => p.Price / 32m < ProbeMoney.From(0.15626m))),
        ["(Price / 32m).Value past the scale"] = Ids(q => q.Where(p => (p.Price / 32m).Value < 0.15626m)),
        // Composed past the scale: a CASE (?:) or a COALESCE (??) of the database's arithmetic was cast whole, and read
        // 0.09 for 3 / 32 = 0.09375 on SQL Server; arithmetic over one, and .ValueOrNull() of an optional one's null
        // check, the same (measured on Azure SQL Edge, an outside review of #190).
        ["?: of two quotients, .Value"]      = Values(q => q.Select(p => (p.Rate > 1m ? p.Price / 32m : p.Tax / 32m).Value)),
        ["?: of two quotients * 2m"]         = Values(q => q.Select(p => (p.Rate > 1m ? p.Price / 32m : p.Tax / 32m) * 2m)),
        ["?? of two quotients, .Value"]      = Values(q => q.Select(p => (p.Optional / 32m ?? p.Price / 32m).Value)),
        ["?? of two quotients, filtered"]    = Ids(q => q.Where(p => (p.Optional / 32m ?? p.Price / 32m).Value < 0.048m)),
        ["?? of two quotients * 2m, filtered"] = Ids(q => q.Where(p => (p.Optional / 32m ?? p.Price / 32m) * 2m < ProbeMoney.From(0.1m))),
        ["?? of ?: and a quotient, .Value"]  = Values(q => q.Select(p => ((p.Rate > 1m ? p.Optional / 32m : null) ?? p.Tax / 32m).Value)),
        ["an optional quotient, ValueOrNull"] = Values(q => q.Select(p => (p.Optional / 32m).ValueOrNull())),
        ["an optional quotient, filtered"]   = Ids(q => q.Where(p => (p.Optional / 32m).ValueOrNull() < 0.048m)),

        ["an optional one * 2m, filtered"]   = Ids(q => q.Where(p => p.Optional * 2m > Four)),
        ["an optional one / Price"]          = Ids(q => q.Where(p => p.Optional / p.Price > 1m)),
        ["a JSON property * 2m, filtered"]   = Ids(q => q.Where(p => p.Doc.Price * 2m > Ten)),
        ["over a navigation"]                = new((_, b) => b.Where(x => x.Items.Any(i => i.Price * 2m > Ten)).OrderBy(x => x.Id).Select(x => (object?)x.Id)),

        ["Price * 2m, projected"]            = Values(q => q.Select(p => p.Price * 2m)),
        ["Price / 2m, projected"]            = Values(q => q.Select(p => p.Price / 2m)),
        ["Price % 2m, projected"]            = Values(q => q.Select(p => p.Price % 2m)),
        ["Tax / Price, projected"]           = Values(q => q.Select(p => p.Tax / p.Price)),
        ["+Price, projected"]                = Values(q => q.Select(p => +p.Price)),
        ["(Price * 2m).Value, projected"]    = Values(q => q.Select(p => (p.Price * 2m).Value)),
        ["(Price / 32m).Value, projected"]   = Values(q => q.Select(p => (p.Price / 32m).Value)),
        ["an optional one * 2m, projected"]  = Values(q => q.Select(p => p.Optional * 2m)),
        ["an optional one * 2m + Tax"]       = Values(q => q.Select(p => p.Optional * 2m + p.Tax)),
        ["-an optional one, projected"]      = Values(q => q.Select(p => -p.Optional)),
        ["Price * an optional factor"]       = Values(q => q.Select(p => p.Price * p.Share)),
        ["Price * an optional factor, filtered"] = Ids(q => q.Where(p => p.Price * p.Share > Four)),

        ["ordered by Tax / Price"]           = Ids(q => q.OrderBy(p => p.Tax / p.Price).ThenBy(p => p.Id), ordersDecimals: true),
        ["ordered by Price * a column"]      = Ids(q => q.OrderBy(p => p.Price * p.Rate).ThenBy(p => p.Id), ordersDecimals: true),
        ["the maximum of Tax / Price"]       = new((q, _) => [q.Max(p => p.Tax / p.Price)], OrdersDecimals: true),
        ["a basket's largest quotient, .Value"] = new((_, b) => b.OrderBy(x => x.Id).Select(x => (object?)x.Items.Max(i => i.Price / 32m).Value), OrdersDecimals: true),
    };

    public static TheoryData<string> Names => [.. Cases.Keys];

    /// <summary>A factor the query captures, so it reaches the database as a parameter.</summary>
    private static decimal Factor() => 2m;

    private static Case Ids(Func<IQueryable<Priced>, IQueryable<Priced>> query, bool ordersDecimals = false) =>
        new((q, _) => ordersDecimals ? query(q).Select(p => (object?)p.Id) : query(q).OrderBy(p => p.Id).Select(p => (object?)p.Id), ordersDecimals);

    private static Case Values<T>(Func<IQueryable<Priced>, IQueryable<T>> query) =>
        new((q, _) => query(q.OrderBy(p => p.Id)).AsEnumerable().Select(value => (object?)value));

    /// <summary>The rows, and their baskets, as both the database and memory hold them.</summary>
    public static (List<Priced> Items, List<Basket> Baskets) Rows()
    {
        Priced Row(int id, decimal price, decimal tax, decimal? optional, decimal rate, decimal? share, int basket) => new()
        {
            Id = id, Price = ProbeMoney.From(price), Tax = ProbeMoney.From(tax), Rate = rate, Share = share, BasketId = basket,
            Optional = optional is { } value ? ProbeMoney.From(value) : null,
            Doc = new PricedDoc { Price = ProbeMoney.From(price) },
        };

        List<Priced> items = [Row(1, 2m, 0.5m, 3m, 3m, 0.5m, 1), Row(2, 10m, 2m, null, 0.5m, null, 1), Row(3, 5m, 2m, 1.5m, 2.5m, 2m, 2)];
        List<Basket> baskets = [new() { Id = 1, Items = [items[0], items[1]] }, new() { Id = 2, Items = [items[2]] }];

        return (items, baskets);
    }

    public static async Task Seed(PricedDb db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Baskets.AddRange(Rows().Baskets);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>The case's answer from the database, as memory gives it.</summary>
    public static void AnswersAsMemory(string name, PricedDb db)
    {
        var @case = Cases[name];
        var (items, baskets) = Rows();

        var memory = @case.Query(items.AsQueryable(), baskets.AsQueryable()).ToList();

        @case.Query(db.Priced, db.Baskets).ToList().ShouldBe(memory, name);
    }

    public sealed class Priced
    {
        public int Id { get; set; }

        public ProbeMoney Price { get; set; }

        public ProbeMoney Tax { get; set; }

        public ProbeMoney? Optional { get; set; }

        public decimal Rate { get; set; }

        public decimal? Share { get; set; }

        public PricedDoc Doc { get; set; } = new();

        public int BasketId { get; set; }
    }

    public sealed class PricedDoc
    {
        public ProbeMoney Price { get; set; }
    }

    public sealed class SmallRow
    {
        public int Id { get; set; }

        public SmallLevel Level { get; set; }
    }

    public sealed class Basket
    {
        public int Id { get; set; }

        public List<Priced> Items { get; set; } = [];
    }

    public class PricedDb(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Priced> Priced => Set<Priced>();

        public DbSet<Basket> Baskets => Set<Basket>();

        public DbSet<SmallRow> Small => Set<SmallRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Priced>().Property(p => p.Id).ValueGeneratedNever();
            modelBuilder.Entity<Priced>().OwnsOne(p => p.Doc, doc => doc.ToJson());
            modelBuilder.Entity<Basket>().Property(b => b.Id).ValueGeneratedNever();
        }
    }
}

/// <summary><see cref="ArithmeticQueries"/> on SQLite.</summary>
public sealed class ArithmeticQueryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Theory]
    [MemberData(nameof(ArithmeticQueries.Names), MemberType = typeof(ArithmeticQueries))]
    public async Task On_SQLite_an_operator_answers_as_memory(string name)
    {
        if (ArithmeticQueries.Cases[name].OrdersDecimals) Assert.Skip("SQLite orders and aggregates no decimal, .Value neither");

        await using var db = await Seeded();

        ArithmeticQueries.AnswersAsMemory(name, db);
    }

    /// <summary>The SQL is the wrapped type's: the value object's column re-typed, the result never cast.</summary>
    [Fact]
    public async Task The_SQL_is_the_wrapped_types_arithmetic_over_the_columns()
    {
        await using var db = await Seeded();

        db.Priced.Where(p => p.Price + p.Tax > ProbeMoney.From(3m)).ToQueryString().ShouldContain("ef_add(\"p\".\"Price\", \"p\".\"Tax\")");
        db.Priced.Where(p => p.Price * 2m > ProbeMoney.From(3m)).ToQueryString().ShouldContain("ef_multiply(\"p\".\"Price\", '2.0')");
        db.Priced.Select(p => p.Tax / p.Price).ToQueryString().ShouldContain("ef_divide(\"p\".\"Tax\", \"p\".\"Price\")");
        db.Priced.Where(p => +p.Price > ProbeMoney.From(3m)).ToQueryString().ShouldContain("ef_compare(\"p\".\"Price\", ");
        db.Priced.Where(p => (p.Price * 2m).Value > 3m).ToQueryString().ShouldNotContain("CAST");

        // An optional operand: the arithmetic in one CASE that checks it, which a ?? or .Value re-types, never casts.
        db.Priced.Select(p => p.Optional * 2m + p.Tax).ToQueryString()
                 .ShouldContain("WHEN \"p\".\"Optional\" IS NOT NULL THEN ef_add(ef_multiply(\"p\".\"Optional\", '2.0'), \"p\".\"Tax\")");
        db.Priced.Select(p => (p.Optional / 32m ?? p.Price / 32m).Value).ToQueryString().ShouldNotContain("CAST");
        db.Priced.Select(p => (p.Rate > 1m ? p.Price / 32m : p.Tax / 32m) * 2m).ToQueryString().ShouldNotContain("CAST");
    }

    /// <summary>
    /// <c>byte</c>, <c>short</c> and <c>char</c> have no arithmetic of their own in an expression tree, where C#
    /// computes it as an <see cref="int"/>'s: such an operator is refused, naming the value object, between two value
    /// objects too.
    /// </summary>
    [Fact]
    public async Task An_operator_the_wrapped_type_has_no_expression_for_is_refused_naming_the_value_object()
    {
        await using var db = await Seeded();

        var scaled = Should.Throw<InvalidOperationException>(() => db.Small.Where(s => s.Level * (byte)2 > SmallLevel.From(4)).ToList());
        var added  = Should.Throw<InvalidOperationException>(() => db.Small.Where(s => s.Level + s.Level > SmallLevel.From(4)).ToList());

        scaled.Message.ShouldStartWith("SmallLevel * Byte is used in a query");
        added.Message.ShouldStartWith("SmallLevel + SmallLevel is used in a query");
        scaled.Message.ShouldContain(".Value");
    }

    public void Dispose() => _connection.Dispose();

    private async Task<ArithmeticQueries.PricedDb> Seeded()
    {
        _connection.Open();
        var db = new ArithmeticQueries.PricedDb(new DbContextOptionsBuilder<ArithmeticQueries.PricedDb>().UseSqlite(_connection).UseTypeKit().Options);
        await ArithmeticQueries.Seed(db);
        return db;
    }
}

/// <summary>A query filter that scales a value object, and one with a ratio: rewritten where the model is built.</summary>
public sealed class ArithmeticQueryFilterTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public async Task Named_query_filters_with_an_operator_answer_as_memory()
    {
        _connection.Open();
        await using var db = new FilteredDb(new DbContextOptionsBuilder<FilteredDb>().UseSqlite(_connection).UseTypeKit().Options);
        await ArithmeticQueries.Seed(db);

        var memory = ArithmeticQueries.Rows().Items.Where(p => p.Price * 2m > ProbeMoney.From(5m) && p.Tax / p.Price < 0.3m).Select(p => p.Id);

        db.Priced.OrderBy(p => p.Id).Select(p => p.Id).ToList().ShouldBe(memory);
    }

    [Fact]
    public async Task An_anonymous_query_filter_with_an_operator_answers_as_memory()
    {
        _connection.Open();
        await using var db = new AnonymousFilteredDb(new DbContextOptionsBuilder<AnonymousFilteredDb>().UseSqlite(_connection).UseTypeKit().Options);
        await ArithmeticQueries.Seed(db);

        var memory = ArithmeticQueries.Rows().Items.Where(p => p.Optional * 2m > ProbeMoney.From(4m)).Select(p => p.Id);

        db.Priced.OrderBy(p => p.Id).Select(p => p.Id).ToList().ShouldBe(memory);
    }

    public void Dispose() => _connection.Dispose();

    private sealed class AnonymousFilteredDb(DbContextOptions<AnonymousFilteredDb> options) : ArithmeticQueries.PricedDb(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ArithmeticQueries.Priced>().HasQueryFilter(p => p.Optional * 2m > ProbeMoney.From(4m));
        }
    }

    private sealed class FilteredDb(DbContextOptions<FilteredDb> options) : ArithmeticQueries.PricedDb(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ArithmeticQueries.Priced>()
                        .HasQueryFilter("Scaled", p => p.Price * 2m > ProbeMoney.From(5m))
                        .HasQueryFilter("Ratio", p => p.Tax / p.Price < 0.3m);
        }
    }
}

/// <summary><see cref="ArithmeticQueries"/> on PostgreSQL.</summary>
[Trait("Needs", "Docker")]
public sealed class PostgreSqlArithmeticQueryTests(PostgreSqlArithmeticQueryTests.Database database) : IClassFixture<PostgreSqlArithmeticQueryTests.Database>
{
    [Theory]
    [MemberData(nameof(ArithmeticQueries.Names), MemberType = typeof(ArithmeticQueries))]
    public async Task On_PostgreSQL_an_operator_answers_as_memory(string name)
    {
        await using var db = database.Create();

        ArithmeticQueries.AnswersAsMemory(name, db);
    }

    public sealed class Database : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        private DbContextOptions<ArithmeticQueries.PricedDb>? _options;

        public ArithmeticQueries.PricedDb Create() => new(_options ?? throw new InvalidOperationException("The database is not started."));

        public async ValueTask InitializeAsync()
        {
            await _container.StartAsync();

            var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "typekit_arithmetic" }.ConnectionString;
            _options = new DbContextOptionsBuilder<ArithmeticQueries.PricedDb>().UseNpgsql(connectionString).UseTypeKit().Options;

            await using var db = Create();
            await ArithmeticQueries.Seed(db);
        }

        public async ValueTask DisposeAsync() => await _container.DisposeAsync();
    }
}

/// <summary><see cref="ArithmeticQueries"/> on SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Needs", "Docker")]
public sealed class SqlServerArithmeticQueryTests(SqlServer server)
{
    private static readonly SemaphoreSlim Seeding = new(1, 1);
    private static bool _seeded;

    [Theory]
    [MemberData(nameof(ArithmeticQueries.Names), MemberType = typeof(ArithmeticQueries))]
    public async Task On_SQL_Server_an_operator_answers_as_memory(string name)
    {
        await using var db = await CreateAsync();

        ArithmeticQueries.AnswersAsMemory(name, db);
    }

    private async Task<ArithmeticQueries.PricedDb> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<ArithmeticQueries.PricedDb>().UseSqlServer(await server.DatabaseAsync("typekit_arithmetic")).UseTypeKit()
                                                                              .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                                                                              .Options;
        var db = new ArithmeticQueries.PricedDb(options);

        await Seeding.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (!_seeded) await ArithmeticQueries.Seed(db);
            _seeded = true;
        }
        finally
        {
            Seeding.Release();
        }

        return db;
    }
}

/// <summary>Added and scaled by a <see cref="byte"/>, which an expression tree cannot do: C# computes two as <see cref="int"/>s.</summary>
public readonly partial record struct SmallLevel : IValue<byte>,
    System.Numerics.IAdditionOperators<SmallLevel, SmallLevel, SmallLevel>,
    System.Numerics.IMultiplyOperators<SmallLevel, byte, SmallLevel>;

/// <summary>
/// A value object the application stores with a converter of its own, here as <c>"M:" + value</c>: its column does
/// not hold the value, so an operator over it is not translated, as <c>.Value</c> is not (decision 56).
/// </summary>
/// <remarks>
/// Measured on SQLite with EF's own translation of an operator between two value objects: <c>Price + Tax &gt; 4</c>
/// became <c>"p"."Price" + "p"."Tax" &gt; 'M:4'</c> and kept no row where memory keeps two, and <c>-Price &lt; -4</c>
/// kept all three where memory keeps two.
/// </remarks>
public sealed class ForeignConvertedArithmeticTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public async Task An_operator_over_a_column_another_converter_stores_is_not_translated()
    {
        _connection.Open();
        await using var db = new ForeignDb(new DbContextOptionsBuilder<ForeignDb>().UseSqlite(_connection).UseTypeKit().Options);
        await ArithmeticQueries.Seed(db);

        foreach (var query in new Func<object>[]
                 {
                     () => db.Priced.Where(p => p.Price + p.Tax > ProbeMoney.From(4m)).ToList(),
                     () => db.Priced.Where(p => -p.Price < ProbeMoney.From(-4m)).ToList(),
                     () => db.Priced.Where(p => p.Price * 2m > ProbeMoney.From(4m)).ToList(),
                     () => db.Priced.Where(p => p.Tax / p.Price < 0.3m).ToList(),
                 })
            Should.Throw<InvalidOperationException>(query).Message.ShouldContain("could not be translated");

        // As the query's last step EF computes it in memory, after the application's converter read the value objects.
        db.Priced.OrderBy(p => p.Id).Select(p => p.Tax / p.Price).ToList().ShouldBe([0.25m, 0.2m, 0.4m]);
        db.Priced.Count(p => p.Price == ProbeMoney.From(10m)).ShouldBe(1);
    }

    /// <summary>
    /// Computed in memory, an optional operand's null is the operator's: unwrapped by a conversion alone, the null row
    /// threw "Nullable object must have a value" (an outside review of #190).
    /// </summary>
    [Fact]
    public async Task Computed_in_memory_an_operator_over_an_optional_one_answers_null_as_memory_does()
    {
        _connection.Open();
        await using var db = new ForeignDb(new DbContextOptionsBuilder<ForeignDb>().UseSqlite(_connection).UseTypeKit().Options);
        await ArithmeticQueries.Seed(db);
        var items = ArithmeticQueries.Rows().Items.AsQueryable();

        void AnswersAsMemory<T>(Func<IQueryable<ArithmeticQueries.Priced>, IQueryable<T>> query) =>
            query(db.Priced.OrderBy(p => p.Id)).ToList().ShouldBe(query(items.OrderBy(p => p.Id)).ToList());

        AnswersAsMemory(q => q.Select(p => p.Optional * 2m));
        AnswersAsMemory(q => q.Select(p => p.Optional / p.Price));
        AnswersAsMemory(q => q.Select(p => -p.Optional + p.Tax));
        AnswersAsMemory(q => q.Select(p => p.Optional * 2m * 3m));
        AnswersAsMemory(q => q.Select(p => p.Price * p.Share));
        AnswersAsMemory(q => q.Select(p => (p.Optional / 32m).ValueOrNull()));
    }

    public void Dispose() => _connection.Dispose();

    private sealed class ForeignDb(DbContextOptions<ForeignDb> options) : ArithmeticQueries.PricedDb(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var entity = modelBuilder.Entity<ArithmeticQueries.Priced>();
            foreach (var property in new[] { entity.Property(p => p.Price), entity.Property(p => p.Tax) })
                property.HasConversion(money => "M:" + money.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                       text => ProbeMoney.From(decimal.Parse(text.Substring(2), System.Globalization.CultureInfo.InvariantCulture)));

            entity.Property(p => p.Optional)
                  .HasConversion(money => "M:" + money!.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                 text => ProbeMoney.From(decimal.Parse(text.Substring(2), System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
