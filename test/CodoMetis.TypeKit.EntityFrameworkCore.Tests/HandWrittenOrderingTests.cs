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
        refusal.ShouldContain(query == "a record class" ? "ProbeReversedLabel" : "ProbeDescending");
        refusal.ShouldContain("Compare or order by .Value explicitly");
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
    }

    /// <summary>Two value objects captured from the query's closure compare in memory, before EF sees the query.</summary>
    [Fact]
    public void A_comparison_EF_evaluates_in_memory_is_not_refused()
    {
        using var db = Seeded();
        var one = ProbeDescending.From(1);

        db.Ranked.Count(r => one > Two).ShouldBe(3);
    }

    public void Dispose() => _connection.Dispose();

    private RankedDb Seeded()
    {
        _connection.Open();
        var db = new RankedDb(new DbContextOptionsBuilder<RankedDb>().UseSqlite(_connection).UseTypeKit().Options);
        db.Database.EnsureCreated();

        foreach (var value in new[] { 1, 2, 3 })
            db.Add(new Ranked { Rank = ProbeDescending.From(value), Count = ProbeCount.From(value), Label = ProbeReversedLabel.From($"{value}") });

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

public sealed class RankedDb(DbContextOptions<RankedDb> options) : DbContext(options)
{
    public DbSet<Ranked> Ranked => Set<Ranked>();
}
