using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-039: EF Core's SQLite provider translates no comparison and no <c>ORDER BY</c> of a
/// <see cref="DateTimeOffset"/>, a <see cref="TimeSpan"/> or a <see cref="ulong"/> column: <c>&gt;=</c> throws "could not be
/// translated", and an ordering throws <see cref="NotSupportedException"/>. Equality is translated. A <see cref="decimal"/>,
/// which EF's page lists beside them, is compared and ordered.
/// </summary>
public sealed class KB039
{
    public sealed class Row<T>
    {
        public int Id { get; set; }

        public T P { get; set; } = default!;
    }

    private sealed class Db<T>(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row<T>>().ToTable("t");
    }

    private sealed class PerType : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), designTime);
    }

    private static async Task<(SqliteConnection Connection, Db<T> Db)> Open<T>(T low, T high)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var db = new Db<T>(new DbContextOptionsBuilder().UseSqlite(connection).ReplaceService<IModelCacheKeyFactory, PerType>().Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.AddRange(new Row<T> { P = high }, new Row<T> { P = low });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return (connection, db);
    }

    public static TheoryData<string> Refused => ["DateTimeOffset", "TimeSpan", "ulong"];

    [Theory, MemberData(nameof(Refused))]
    public Task A_comparison_is_not_translated(string type) => type switch
    {
        "DateTimeOffset" => NotTranslated(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), r => r.P >= DateTimeOffset.UnixEpoch.AddDays(1)),
        "TimeSpan" => NotTranslated(TimeSpan.FromHours(1), TimeSpan.FromHours(2), r => r.P >= TimeSpan.FromHours(2)),
        _ => NotTranslated(1UL, 2UL, r => r.P >= 2UL),
    };

    [Theory, MemberData(nameof(Refused))]
    public Task An_ordering_is_not_supported(string type) => type switch
    {
        "DateTimeOffset" => NotOrdered(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), "DateTimeOffset"),
        "TimeSpan" => NotOrdered(TimeSpan.FromHours(1), TimeSpan.FromHours(2), "TimeSpan"),
        _ => NotOrdered(1UL, 2UL, "ulong"),
    };

    [Theory, MemberData(nameof(Refused))]
    public Task Equality_is_translated(string type) => type switch
    {
        "DateTimeOffset" => Equal(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), r => r.P == DateTimeOffset.UnixEpoch),
        "TimeSpan" => Equal(TimeSpan.FromHours(1), TimeSpan.FromHours(2), r => r.P == TimeSpan.FromHours(1)),
        _ => Equal(1UL, 2UL, r => r.P == 1UL),
    };

    [Fact]
    public async Task A_decimal_is_compared_and_ordered()
    {
        var (connection, db) = await Open(1.5m, 2.5m);
        await using var _ = connection;
        await using var __ = db;
        (await db.Set<Row<decimal>>().Where(r => r.P >= 2m).Select(r => r.P).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe([2.5m]);
        (await db.Set<Row<decimal>>().OrderBy(r => r.P).Select(r => r.P).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe([1.5m, 2.5m]);
    }

    private static async Task NotTranslated<T>(T low, T high, Expression<Func<Row<T>, bool>> atLeastHigh)
    {
        var (connection, db) = await Open(low, high);
        await using var _ = connection;
        await using var __ = db;
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => db.Set<Row<T>>().Where(atLeastHigh).ToListAsync(TestContext.Current.CancellationToken));
        refused.Message.ShouldContain("could not be translated");
    }

    private static async Task NotOrdered<T>(T low, T high, string name)
    {
        var (connection, db) = await Open(low, high);
        await using var _ = connection;
        await using var __ = db;
        var refused = await Should.ThrowAsync<NotSupportedException>(() => db.Set<Row<T>>().OrderBy(r => r.P).ToListAsync(TestContext.Current.CancellationToken));
        refused.Message.ShouldContain($"SQLite does not support expressions of type '{name}' in ORDER BY clauses");
    }

    private static async Task Equal<T>(T low, T high, Expression<Func<Row<T>, bool>> isLow)
    {
        var (connection, db) = await Open(low, high);
        await using var _ = connection;
        await using var __ = db;
        (await db.Set<Row<T>>().Where(isLow).Select(r => r.P).ToListAsync(TestContext.Current.CancellationToken)).ShouldBe([low]);
    }
}
