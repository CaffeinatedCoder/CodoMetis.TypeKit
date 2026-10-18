using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-032: PostgreSQL sorts NULL last in ascending order and first in descending order, and EF
/// adds no <c>NULLS FIRST</c> or <c>NULLS LAST</c>. LINQ to Objects puts null first ascending.
/// </summary>
public sealed class KB032
{
    public sealed class Ranked
    {
        public int Id { get; set; }

        public int? Rank { get; set; }
    }

    [Fact]
    public async Task Null_sorts_last_ascending_and_first_descending()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Ranked>());
        db.AddRange(new Ranked { Rank = null }, new Ranked { Rank = 1 }, new Ranked { Rank = 3 }, new Ranked { Rank = 2 });
        await db.SaveChangesAsync(cancellation);

        db.Set<Ranked>().OrderBy(r => r.Rank).ToQueryString().ShouldNotContain("NULLS");
        (await db.Set<Ranked>().OrderBy(r => r.Rank).Select(r => r.Rank).ToListAsync(cancellation)).ShouldBe([1, 2, 3, null]);
        (await db.Set<Ranked>().OrderByDescending(r => r.Rank).Select(r => r.Rank).ToListAsync(cancellation)).ShouldBe([null, 3, 2, 1]);
        new int?[] { null, 1, 3, 2 }.Order().ToList().ShouldBe([null, 1, 2, 3], "in memory");
    }
}
