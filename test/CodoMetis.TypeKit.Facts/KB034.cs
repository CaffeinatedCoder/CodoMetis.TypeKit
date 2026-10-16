using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-034: a <c>numeric(18,2)</c> column rounds a value with more fractional digits when it is
/// written, half away from zero.
/// </summary>
public sealed class KB034
{
    public sealed class Money
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }

    [Fact]
    public async Task Numeric_p_s_rounds_more_fractional_digits_on_write()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Money>().Property(m => m.Amount).HasPrecision(18, 2));
        db.AddRange(new Money { Amount = 1.005m }, new Money { Amount = -0.004m });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        (await db.Set<Money>().OrderBy(m => m.Id).Select(m => m.Amount).ToListAsync(cancellation)).ShouldBe([1.01m, 0.00m]);
    }
}
