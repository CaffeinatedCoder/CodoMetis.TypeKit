using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// <c>FirstOrNone</c>, <c>FirstOrError</c> and <c>FirstOrNoneAsync</c> on a query ask the database
/// for one row and let it run the condition. On the <c>IEnumerable</c> overloads, which were the only
/// ones, <c>db.Shipments.FirstOrNone(s =&gt; …)</c> read the whole table and ran the condition in
/// memory, with no warning.
/// </summary>
public sealed class QueryOptionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly List<string> _commands = [];

    public QueryOptionTests()
    {
        _connection.Open();
        using var db = Db();
        db.Database.EnsureCreated();
        db.Shipments.AddRange(new Shipment { Carrier = "alpha" }, new Shipment { Carrier = "beta" }, new Shipment { Carrier = "gamma" });
        db.SaveChanges();
        _commands.Clear();
    }

    public void Dispose() => _connection.Dispose();

    private TestDb Db() =>
        new(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).UseTypeKit()
                                                 .LogTo(_commands.Add, [RelationalEventId.CommandExecuted]).Options);

    /// <summary>EF passes the limit as a parameter, <c>LIMIT @p</c>, with 1 for its value.</summary>
    [Fact]
    public void FirstOrNone_runs_the_condition_in_the_query_and_reads_one_row()
    {
        using var db = Db();

        db.Shipments.FirstOrNone(s => s.Carrier == "beta").TryGetValue(out var found).ShouldBeTrue();

        found.Carrier.ShouldBe("beta");
        var sql = _commands.ShouldHaveSingleItem();
        sql.ShouldContain("WHERE \"s\".\"Carrier\" = 'beta'");
        sql.ShouldContain("LIMIT @p");
    }

    [Fact]
    public void FirstOrNone_without_a_condition_reads_one_row_and_None_reads_none()
    {
        using var db = Db();

        db.Shipments.OrderBy(s => s.Carrier).FirstOrNone().TryGetValue(out var first).ShouldBeTrue();
        first.Carrier.ShouldBe("alpha");
        _commands.ShouldHaveSingleItem().ShouldContain("LIMIT @p");

        db.Shipments.FirstOrNone(s => s.Carrier == "delta").TryGetValue(out _).ShouldBeFalse();
    }

    /// <summary>A condition only the database can run, which in memory throws: proof that it is not run there.</summary>
    [Fact]
    public async Task A_condition_only_the_database_can_run_is_translated()
    {
        await using var db = Db();

        db.Shipments.FirstOrNone(s => EF.Functions.Like(s.Carrier, "g%")).TryGetValue(out var sync).ShouldBeTrue();
        sync.Carrier.ShouldBe("gamma");

        (await db.Shipments.FirstOrNoneAsync(s => EF.Functions.Like(s.Carrier, "b%"), TestContext.Current.CancellationToken)).TryGetValue(out var async).ShouldBeTrue();
        async.Carrier.ShouldBe("beta");

        db.Shipments.FirstOrError(s => EF.Functions.Like(s.Carrier, "a%"), "none").TryGetValue(out var result, out _).ShouldBeTrue();
        result.Carrier.ShouldBe("alpha");
    }

    /// <summary>The database runs the condition and is asked for two rows: enough to tell one from many.</summary>
    [Fact]
    public async Task SingleOrNone_asks_for_two_rows_and_refuses_more_than_one()
    {
        await using var db = Db();

        db.Shipments.SingleOrNone(s => s.Carrier == "beta").TryGetValue(out var found).ShouldBeTrue();
        found.Carrier.ShouldBe("beta");
        var sql = _commands.ShouldHaveSingleItem();
        sql.ShouldContain("WHERE \"s\".\"Carrier\" = 'beta'");
        sql.ShouldContain("LIMIT @p");

        Should.Throw<InvalidOperationException>(() => db.Shipments.SingleOrNone());
        db.Shipments.SingleOrNone(s => s.Carrier == "delta").TryGetValue(out _).ShouldBeFalse();

        (await db.Shipments.SingleOrNoneAsync(s => EF.Functions.Like(s.Carrier, "g%"), TestContext.Current.CancellationToken)).TryGetValue(out var async).ShouldBeTrue();
        async.Carrier.ShouldBe("gamma");
        await Should.ThrowAsync<InvalidOperationException>(() => db.Shipments.SingleOrNoneAsync(TestContext.Current.CancellationToken));
        (await db.Shipments.Where(s => s.Carrier == "delta").SingleOrNoneAsync(TestContext.Current.CancellationToken)).TryGetValue(out _).ShouldBeFalse();
    }

    [Fact]
    public void ElementAtOrNone_skips_to_the_row_in_the_query()
    {
        using var db = Db();

        db.Shipments.OrderBy(s => s.Carrier).ElementAtOrNone(1).TryGetValue(out var second).ShouldBeTrue();
        second.Carrier.ShouldBe("beta");
        var sql = _commands.ShouldHaveSingleItem();
        sql.ShouldContain("LIMIT @p");
        sql.ShouldContain("OFFSET @p");

        db.Shipments.ElementAtOrNone(3).TryGetValue(out _).ShouldBeFalse();
        _commands.Clear();
        db.Shipments.ElementAtOrNone(-1).TryGetValue(out _).ShouldBeFalse();
        _commands.ShouldBeEmpty("a negative index asks nothing of the database");
    }

    [Fact]
    public async Task FirstOrNoneAsync_reads_one_row_and_answers_None_for_nothing_found()
    {
        await using var db = Db();

        (await db.Shipments.FirstOrNoneAsync(s => s.Carrier == "gamma", TestContext.Current.CancellationToken)).TryGetValue(out var found).ShouldBeTrue();
        found.Carrier.ShouldBe("gamma");
        _commands.ShouldHaveSingleItem().ShouldContain("LIMIT @p");

        (await db.Shipments.Where(s => s.Carrier == "delta").FirstOrNoneAsync(TestContext.Current.CancellationToken)).TryGetValue(out _).ShouldBeFalse();
        db.Shipments.FirstOrError(s => s.Carrier == "delta", "none").TryGetValue(out _, out var error).ShouldBeFalse();
        error.ShouldBe("none");
    }
}
