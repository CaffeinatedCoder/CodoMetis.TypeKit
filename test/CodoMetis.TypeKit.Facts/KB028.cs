using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-028: PostgreSQL treats NaN as greater than every number, so <c>&gt; 5</c> keeps a NaN row
/// and ascending order puts NaN after Infinity. In .NET every comparison with NaN is false.
/// </summary>
public sealed class KB028
{
    public sealed class Ratio
    {
        public int Id { get; set; }

        public double R { get; set; }
    }

    [Fact]
    public async Task NaN_is_greater_than_every_number_and_sorts_after_Infinity()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Ratio>());
        db.AddRange(new Ratio { R = 1.5 }, new Ratio { R = double.NaN }, new Ratio { R = 10 }, new Ratio { R = double.PositiveInfinity });
        await db.SaveChangesAsync(cancellation);

        (await db.Set<Ratio>().Where(r => r.R > 5).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync(cancellation)).ShouldBe([2, 3, 4]);
        (await db.Set<Ratio>().OrderBy(r => r.R).Select(r => r.R).ToListAsync(cancellation)).ShouldBe([1.5, 10, double.PositiveInfinity, double.NaN]);
        (double.NaN > 5).ShouldBeFalse("in memory");
    }
}
