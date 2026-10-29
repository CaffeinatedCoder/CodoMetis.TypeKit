using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-050: a <see cref="decimal"/> property without a configured precision is a <c>decimal(18,2)</c>
/// column on SQL Server. Every value reads back at scale 2 (<c>10m</c> as <c>10.00m</c>, equal but printed
/// <c>"10.00"</c>), and a value with more fractional digits is rounded when it is written.
/// </summary>
[Trait("Needs", "Docker")]
public sealed class KB050
{
    public sealed class Money
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }

    [Fact]
    public async Task A_decimal_reads_back_at_scale_2_rounded()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Money>());
        db.Model.FindEntityType(typeof(Money))!.FindProperty(nameof(Money.Amount))!.GetColumnType().ShouldBe("decimal(18,2)");
        db.AddRange(new Money { Amount = 10m }, new Money { Amount = 1.005m }, new Money { Amount = -0.004m });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        var read = await db.Set<Money>().OrderBy(m => m.Id).Select(m => m.Amount).ToListAsync(cancellation);
        read.Select(amount => amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(["10.00", "1.01", "0.00"]);
        read[0].ShouldBe(10m);
    }
}
