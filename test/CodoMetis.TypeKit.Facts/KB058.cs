using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-058: EF Core evaluates a LINQ chain over a collection the query captures in memory when the chain
/// takes no lambda, <c>values.Max()</c>, and sends the result as one parameter; with a lambda,
/// <c>values.OrderByDescending(v =&gt; v).First()</c>, it sends the collection's elements and orders them in SQL, and
/// <c>values.Max(v =&gt; v)</c> throws inside EF. A query filter over the context's own values does the same.
/// </summary>
public sealed class KB058
{
    public sealed class Row
    {
        public int Id { get; set; }

        public int P { get; set; }
    }

    private sealed class Db(DbContextOptions options) : DbContext(options)
    {
        public int[] Values { get; } = [1, 2, 3];

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>();
    }

    private sealed class MaxFilterDb(DbContextOptions options) : DbContext(options)
    {
        public int[] Values { get; } = [1, 2, 3];

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>().HasQueryFilter(r => r.P == Values.Max());
    }

    private sealed class KeyedMaxFilterDb(DbContextOptions options) : DbContext(options)
    {
        public int[] Values { get; } = [1, 2, 3];

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>().HasQueryFilter(r => r.P == Values.OrderByDescending(v => v).First());
    }

    public static TheoryData<string, bool> Chains => new()
    {
        { "Max()", false },
        { "Min()", false },
        { "Order().First()", false },
        { "First()", false },
        { "OrderBy(v => v).First()", true },
        { "OrderByDescending(v => v).First()", true },
    };

    [Theory, MemberData(nameof(Chains))]
    public void A_chain_over_captured_values_is_ordered_in_SQL_only_with_a_lambda(string chain, bool inSql)
    {
        int[] values = [1, 2, 3];
        Expression<Func<Row, bool>> filter = chain switch
        {
            "Max()"                             => r => r.P == values.Max(),
            "Min()"                             => r => r.P == values.Min(),
            "Order().First()"                   => r => r.P == values.Order().First(),
            "First()"                           => r => r.P == values.First(),
            "OrderBy(v => v).First()"           => r => r.P == values.OrderBy(v => v).First(),
            _                                   => r => r.P == values.OrderByDescending(v => v).First(),
        };
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = new Db(new DbContextOptionsBuilder().UseSqlite(connection).Options);

        HoldsTheCollection(db.Set<Row>().Where(filter).ToQueryString()).ShouldBe(inSql);
    }

    [Fact]
    public void A_keyed_Max_over_captured_values_throws_inside_EF()
    {
        int[] values = [1, 2, 3];
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = new Db(new DbContextOptionsBuilder().UseSqlite(connection).Options);

        Should.Throw<NullReferenceException>(() => db.Set<Row>().Where(r => r.P == values.Max(v => v)).ToQueryString());
    }

    [Fact]
    public void A_query_filter_over_the_contexts_values_is_ordered_in_SQL_only_with_a_lambda()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        using (var db = new MaxFilterDb(new DbContextOptionsBuilder().UseSqlite(connection).Options))
            HoldsTheCollection(db.Set<Row>().ToQueryString()).ShouldBeFalse();

        using (var db = new KeyedMaxFilterDb(new DbContextOptionsBuilder().UseSqlite(connection).Options))
            HoldsTheCollection(db.Set<Row>().ToQueryString()).ShouldBeTrue();
    }

    /// <summary>The collection's elements in the SQL: one parameter each in a <c>VALUES</c> list (EF 10's default), or as JSON.</summary>
    private static bool HoldsTheCollection(string sql) => sql.Contains("VALUES") || sql.Contains("json_each");
}
