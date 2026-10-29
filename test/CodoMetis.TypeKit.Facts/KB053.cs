using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-053: EF's SQL Server provider translates <c>EF.Functions.Greatest</c> and <c>Least</c> to
/// <c>GREATEST</c> and <c>LEAST</c> only at compatibility level 160 or later (<c>UseCompatibilityLevel(160)</c>), which
/// <c>UseSqlServer</c> does not assume. Below it a filter that calls one is refused ("could not be translated"), and a
/// final projection reads the arguments' columns and calls the function in memory, where it throws when the query runs.
/// No server is opened; <c>ToQueryString</c> translates without one.
/// </summary>
public sealed class KB053
{
    public sealed class Pair
    {
        public int Id { get; set; }

        public int Left { get; set; }

        public int Right { get; set; }
    }

    private sealed class Db(DbContextOptions<Db> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Pair>();
    }

    private static Db Create(int? level) =>
        new(new DbContextOptionsBuilder<Db>().UseSqlServer("Server=localhost;Database=none", sql =>
        {
            if (level is { } compatibility) sql.UseCompatibilityLevel(compatibility);
        }).Options);

    /// <summary>EF's default, and 150, the level below.</summary>
    public static TheoryData<int> Below160 => [0, 150];

    [Theory, MemberData(nameof(Below160))]
    public void Below_compatibility_level_160_Greatest_is_not_translated(int level)
    {
        using var db = Create(level == 0 ? null : level);

        Should.Throw<InvalidOperationException>(() => db.Set<Pair>().Where(p => EF.Functions.Greatest(p.Left, p.Right) > 2).ToQueryString()).Message.ShouldContain("could not be translated");
        db.Set<Pair>().Select(p => EF.Functions.Least(p.Left, p.Right)).ToQueryString().ShouldBe("SELECT [p].[Left], [p].[Right]" + Environment.NewLine + "FROM [Pair] AS [p]");
        Should.Throw<InvalidOperationException>(() => EF.Functions.Least(1, 2)).Message.ShouldContain("switched to client-evaluation");
    }

    [Fact]
    public void At_compatibility_level_160_Greatest_and_Least_are_translated()
    {
        using var db = Create(160);

        db.Set<Pair>().Where(p => EF.Functions.Greatest(p.Left, p.Right) > 2).ToQueryString().ShouldContain("WHERE GREATEST([p].[Left], [p].[Right]) > 2");
        db.Set<Pair>().Select(p => EF.Functions.Least(p.Left, p.Right)).ToQueryString().ShouldContain("SELECT LEAST([p].[Left], [p].[Right])");
    }
}
