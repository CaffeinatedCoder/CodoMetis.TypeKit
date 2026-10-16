using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A value object that declares its own <c>CompareTo</c> cannot be compared or ordered in SQL, where the
/// column orders by the wrapped value: the query is refused, naming the value object and <c>.Value</c>.
/// </summary>
/// <remarks>
/// Measured on SQLite before the refusal, with <see cref="ProbeDescending"/> holding 1, 2 and 3 (its
/// <c>CompareTo</c> orders them 3, 2, 1): <c>Rank &gt; 2</c> answered 3 where memory answers 1,
/// <c>CompareTo(2) &gt; 0</c> the same, <c>OrderBy(Rank)</c> 1, 2, 3 where memory sorts 3, 2, 1, and
/// <c>Max(Rank)</c> 3 where memory answers 1.
/// </remarks>
public sealed class HandWrittenOrderingTests : IDisposable
{
    private static readonly ProbeDescending Two = ProbeDescending.From(2);

    private static readonly Dictionary<string, Func<RankedDb, object>> Refused = new()
    {
        ["greater than"]           = db => db.Ranked.Where(r => r.Rank > Two).ToList(),
        ["less than or equal"]     = db => db.Ranked.Where(r => r.Rank <= Two).ToList(),
        ["an optional one"]        = db => db.Ranked.Where(r => r.Optional < Two).ToList(),
        ["CompareTo"]              = db => db.Ranked.Where(r => r.Rank.CompareTo(Two) > 0).ToList(),
        ["in a projection"]        = db => db.Ranked.Select(r => r.Rank >= Two).ToList(),
        ["OrderBy"]                = db => db.Ranked.OrderBy(r => r.Rank).ToList(),
        ["OrderByDescending"]      = db => db.Ranked.OrderByDescending(r => r.Rank).ToList(),
        ["ThenBy"]                 = db => db.Ranked.OrderBy(r => r.Id).ThenBy(r => r.Rank).ToList(),
        ["ThenByDescending"]       = db => db.Ranked.OrderBy(r => r.Id).ThenByDescending(r => r.Optional).ToList(),
        ["Order"]                  = db => db.Ranked.Select(r => r.Rank).Order().ToList(),
        ["Min"]                    = db => db.Ranked.Min(r => r.Rank),
        ["Max"]                    = db => db.Ranked.Select(r => r.Rank).Max(),
        ["a record class"]         = db => db.Ranked.OrderBy(r => r.Label).ToList(),

        // Over a navigation the operators are Enumerable's: the first answered 1 where memory answers 3,
        // the second 3 where memory answers 1.
        ["OrderBy over a navigation"] = db => db.Parents.Select(p => p.Children.OrderBy(c => c.Rank).Select(c => c.Rank.Value).First()).ToList(),
        ["Max over a navigation"]     = db => db.Parents.Select(p => p.Children.Max(c => c.Rank)).ToList(),

        // Boxed, the key still orders by CompareTo in memory, 3, 2, 1; EF dropped the cast and answered 1, 2, 3.
        ["OrderBy a key cast to object"] = db => db.Ranked.OrderBy(r => (object)r.Rank).ToList(),
        ["ThenBy a key cast to object"]  = db => db.Ranked.OrderBy(r => r.Id % 1).ThenBy(r => (object)r.Rank).ToList(),

        // The database's own GREATEST and LEAST (SQLite's max and min) order by the column: the first answered
        // 2, 2, 3 where memory's order answers 1, 1, 2 (measured on PostgreSQL 17 before the refusal).
        ["Greatest"]               = db => db.Ranked.Select(r => EF.Functions.Greatest(r.Rank, Two)).ToList(),
        ["Least"]                  = db => db.Ranked.Where(r => EF.Functions.Least(r.Optional, Two) == r.Optional).ToList(),
        ["Least of a record class"] = db => db.Ranked.Select(r => EF.Functions.Least(r.Label, ProbeReversedLabel.From("2"))).ToList(),
    };

    /// <summary>
    /// Npgsql's row-value comparisons, <c>(a, b) &gt; (x, y)</c>, which compare each element by the column's order:
    /// <c>GreaterThan((Rank, Id), (2, 0))</c> kept every row from 2 up where memory's order keeps only 1. Translated
    /// without a server, since the refusal comes before any SQL.
    /// </summary>
    private static readonly Dictionary<string, Func<RankedDb, IQueryable>> RefusedOnPostgreSql = new()
    {
        ["GreaterThan"]          = db => db.Ranked.Where(r => EF.Functions.GreaterThan(ValueTuple.Create(r.Rank, r.Id), ValueTuple.Create(Two, 0))),
        ["LessThan"]             = db => db.Ranked.Where(r => EF.Functions.LessThan(ValueTuple.Create(r.Id, r.Optional), ValueTuple.Create(0, (ProbeDescending?)Two))),
        ["GreaterThanOrEqual"]   = db => db.Ranked.Where(r => EF.Functions.GreaterThanOrEqual(new ValueTuple<int, ProbeDescending>(r.Id, r.Rank), new ValueTuple<int, ProbeDescending>(0, Two))),
        ["LessThanOrEqual"]      = db => db.Ranked.Where(r => EF.Functions.LessThanOrEqual(ValueTuple.Create(r.Label, r.Id), ValueTuple.Create(ProbeReversedLabel.From("2"), 0))),
        ["Greatest"]             = db => db.Ranked.Select(r => EF.Functions.Greatest(r.Rank, Two)),
    };

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public static TheoryData<string> RefusedQueries => [.. Refused.Keys];

    [Theory]
    [MemberData(nameof(RefusedQueries))]
    public void A_query_that_orders_a_value_object_by_its_own_CompareTo_is_refused(string query)
    {
        using var db = Seeded();

        var refusal = Should.Throw<InvalidOperationException>(() => Refused[query](db)).Message;

        refusal.ShouldContain("its ordering is its own CompareTo, which the database cannot run");
        refusal.ShouldContain(query is "a record class" or "Least of a record class" ? "ProbeReversedLabel" : "ProbeDescending");
        refusal.ShouldContain("Compare or order by .Value explicitly");
    }

    public static TheoryData<string> RefusedPostgreSqlQueries => [.. RefusedOnPostgreSql.Keys];

    [Theory]
    [MemberData(nameof(RefusedPostgreSqlQueries))]
    public void A_row_value_comparison_by_its_own_CompareTo_is_refused_on_PostgreSQL(string query)
    {
        using var db = new RankedDb(new DbContextOptionsBuilder<RankedDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options);

        var refusal = Should.Throw<InvalidOperationException>(() => RefusedOnPostgreSql[query](db).ToQueryString()).Message;

        refusal.ShouldContain("its ordering is its own CompareTo, which the database cannot run");
        refusal.ShouldContain(query == "LessThanOrEqual" ? "ProbeReversedLabel is compared" : "ProbeDescending is compared");
        refusal.ShouldContain($"by {query} in a query");
    }

    /// <summary>The same functions over <c>.Value</c>, or over a value object ordered by what it wraps, still translate.</summary>
    [Fact]
    public void Greatest_Least_and_row_values_by_Value_still_translate()
    {
        using (var db = Seeded())
        {
            db.Ranked.Select(r => EF.Functions.Greatest(r.Rank.Value, 2)).ToList().Order().ShouldBe([2, 2, 3]);
            db.Ranked.Select(r => EF.Functions.Least(r.Count, ProbeCount.From(2))).ToList().Order().ShouldBe([ProbeCount.From(1), ProbeCount.From(2), ProbeCount.From(2)]);
        }

        using var postgreSql = new RankedDb(new DbContextOptionsBuilder<RankedDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options);
        postgreSql.Ranked.Where(r => EF.Functions.GreaterThan(ValueTuple.Create(r.Rank.Value, r.Id), ValueTuple.Create(2, 0))).ToQueryString()
                  .ShouldContain("(r.\"Rank\", r.\"Id\") > (");
        postgreSql.Ranked.Where(r => EF.Functions.LessThan(ValueTuple.Create(r.Count, r.Id), ValueTuple.Create(ProbeCount.From(2), 0))).ToQueryString()
                  .ShouldContain("(r.\"Count\", r.\"Id\") < (");
    }

    /// <summary>
    /// <c>.Value</c> says the wrapped value's order is meant, equality is no ordering, and a value object
    /// ordered by what it wraps orders in SQL as in memory.
    /// </summary>
    [Fact]
    public void Ordering_by_Value_equality_and_a_generated_ordering_still_translate()
    {
        using var db = Seeded();

        db.Ranked.Where(r => r.Rank.Value > 2).Select(r => r.Rank.Value).ToList().ShouldBe([3]);
        db.Ranked.OrderByDescending(r => r.Rank.Value).Select(r => r.Rank.Value).ToList().ShouldBe([3, 2, 1]);
        db.Ranked.Select(r => r.Rank).OrderBy(rank => rank.Value).ToList().ShouldBe([ProbeDescending.From(1), Two, ProbeDescending.From(3)]);
        db.Ranked.Max(r => r.Rank.Value).ShouldBe(3);
        db.Ranked.Count(r => r.Rank == Two).ShouldBe(1);
        db.Ranked.Count(r => r.Rank != Two).ShouldBe(2);

        db.Ranked.Where(r => r.Count > ProbeCount.From(1)).OrderBy(r => r.Count).Select(r => r.Count.Value).ToList().ShouldBe([2, 3]);
        db.Ranked.Max(r => r.Count).ShouldBe(ProbeCount.From(3));

        db.Parents.Select(p => p.Children.OrderByDescending(c => c.Rank.Value).Select(c => c.Rank.Value).First()).ToList().ShouldBe([3]);
        db.Parents.Select(p => p.Children.Max(c => c.Rank.Value)).ToList().ShouldBe([3]);
    }

    /// <summary>Two value objects captured from the query's closure compare in memory, before EF sees the query.</summary>
    [Fact]
    public void A_comparison_EF_evaluates_in_memory_is_not_refused()
    {
        using var db = Seeded();
        var one = ProbeDescending.From(1);

        db.Ranked.Count(r => one > Two).ShouldBe(3);
    }

    /// <summary>
    /// EF adds a query filter to the query after the interceptor has seen it, so a filter that compares
    /// such a value object answered by the column's order: <c>Rank &gt; From(2)</c> kept 3 where memory
    /// keeps 1. The filter is refused when the model is built, named or not, naming the entity type.
    /// </summary>
    [Theory]
    [InlineData(typeof(FilteredByRankDb), "compared with >", "the query filter of FilteredRank")]
    [InlineData(typeof(NamedFilterByRankDb), "compared with >", "the query filter \"AboveTwo\" of FilteredRank")]
    [InlineData(typeof(FilteredByChildRankDb), "compared with >", "the query filter of RankParent")]
    [InlineData(typeof(FilteredByGreatestDb), "compared by Greatest", "the query filter of FilteredRank")]
    public void A_query_filter_that_orders_by_its_own_CompareTo_is_refused(Type context, string how, string filter)
    {
        _connection.Open();
        using var db = (DbContext)Activator.CreateInstance(context, new DbContextOptionsBuilder().UseSqlite(_connection).UseTypeKit().Options)!;

        var refusal = Should.Throw<InvalidOperationException>(() =>
        {
            db.Database.EnsureCreated();
            return db.Set<FilteredRank>().Select(r => r.Rank.Value).ToList();
        }).Message;

        refusal.ShouldContain($"ProbeDescending is {how} in {filter}");
        refusal.ShouldContain("its ordering is its own CompareTo, which the database cannot run");
        refusal.ShouldContain("Compare or order by .Value explicitly");
    }

    /// <summary>A row-value comparison in a filter is refused when the model is built, on the provider that has one.</summary>
    [Fact]
    public void A_query_filter_with_a_row_value_comparison_by_its_own_CompareTo_is_refused_on_PostgreSQL()
    {
        using var db = new FilteredByRowValueDb(new DbContextOptionsBuilder().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options);

        var refusal = Should.Throw<InvalidOperationException>(() => db.Model).Message;

        refusal.ShouldContain("ProbeDescending is compared in a row value by GreaterThan in the query filter of FilteredRank");
        refusal.ShouldContain("its ordering is its own CompareTo, which the database cannot run");
    }

    /// <summary>
    /// A filter by <c>.Value</c> is the column's order, and a comparison that reads nothing of the entity
    /// is evaluated in memory, where <c>CompareTo</c> runs: <c>Floor &gt; From(2)</c> is true for the
    /// descending order, so the rows above 1 are kept, where SQL's <c>1 &gt; 2</c> would have kept none.
    /// </summary>
    [Fact]
    public void A_query_filter_by_Value_or_in_memory_is_not_refused()
    {
        _connection.Open();
        using var db = new FilteredByValueDb(new DbContextOptionsBuilder<FilteredByValueDb>().UseSqlite(_connection).UseTypeKit().Options);
        db.Database.EnsureCreated();
        foreach (var value in new[] { 1, 2, 3 }) db.Add(new FilteredRank { Rank = ProbeDescending.From(value) });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        db.Set<FilteredRank>().Select(r => r.Rank.Value).Order().ToList().ShouldBe([2, 3]);
    }

    public void Dispose() => _connection.Dispose();

    private RankedDb Seeded()
    {
        _connection.Open();
        var db = new RankedDb(new DbContextOptionsBuilder<RankedDb>().UseSqlite(_connection).UseTypeKit().Options);
        db.Database.EnsureCreated();

        foreach (var value in new[] { 1, 2, 3 })
            db.Add(new Ranked { Rank = ProbeDescending.From(value), Count = ProbeCount.From(value), Label = ProbeReversedLabel.From($"{value}") });

        db.Add(new RankParent { Children = [new RankChild { Rank = ProbeDescending.From(1) }, new RankChild { Rank = ProbeDescending.From(3) }] });

        db.SaveChanges();
        db.ChangeTracker.Clear();

        return db;
    }
}

public sealed class Ranked
{
    public int Id { get; set; }

    /// <summary>Ordered by its own <c>CompareTo</c>, descending.</summary>
    public ProbeDescending Rank { get; set; }

    public ProbeDescending? Optional { get; set; }

    /// <summary>A record class ordered by its own <c>CompareTo</c>.</summary>
    public ProbeReversedLabel Label { get; set; } = null!;

    /// <summary>Ordered by what it wraps.</summary>
    public ProbeCount Count { get; set; }
}

public sealed class RankParent
{
    public int Id { get; set; }

    public List<RankChild> Children { get; set; } = [];
}

public sealed class RankChild
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public ProbeDescending Rank { get; set; }
}

public sealed class RankedDb(DbContextOptions<RankedDb> options) : DbContext(options)
{
    public DbSet<Ranked> Ranked => Set<Ranked>();

    public DbSet<RankParent> Parents => Set<RankParent>();
}

public sealed class FilteredRank
{
    public int Id { get; set; }

    public ProbeDescending Rank { get; set; }
}

/// <summary>A query filter that compares by the hand-written ordering.</summary>
public sealed class FilteredByRankDb(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FilteredRank>().HasQueryFilter(r => r.Rank > ProbeDescending.From(2));
}

/// <summary>The same as a named filter, beside one that is allowed.</summary>
public sealed class NamedFilterByRankDb(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FilteredRank>()
                    .HasQueryFilter("Positive", r => r.Rank.Value > 0)
                    .HasQueryFilter("AboveTwo", r => r.Rank > ProbeDescending.From(2));
}

/// <summary>A filter on another entity type, over its navigation.</summary>
public sealed class FilteredByChildRankDb(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilteredRank>();
        modelBuilder.Entity<RankParent>().HasQueryFilter(p => p.Children.Max(c => c.Rank) > ProbeDescending.From(2));
    }
}

/// <summary>A query filter that keeps the rows whose rank is the greater of it and 2, by the database's GREATEST.</summary>
public sealed class FilteredByGreatestDb(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FilteredRank>().HasQueryFilter(r => EF.Functions.Greatest(r.Rank, ProbeDescending.From(2)) == r.Rank);
}

/// <summary>A query filter by Npgsql's row-value comparison.</summary>
public sealed class FilteredByRowValueDb(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FilteredRank>().HasQueryFilter(r => EF.Functions.GreaterThan(ValueTuple.Create(r.Rank, r.Id), ValueTuple.Create(ProbeDescending.From(2), 0)));
}

/// <summary>Filters that are allowed: by <c>.Value</c>, and a comparison of the context's own value, evaluated in memory.</summary>
public sealed class FilteredByValueDb(DbContextOptions<FilteredByValueDb> options) : DbContext(options)
{
    public ProbeDescending Floor { get; set; } = ProbeDescending.From(1);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FilteredRank>().HasQueryFilter(r => Floor > ProbeDescending.From(2) && r.Rank.Value > 1);
}
