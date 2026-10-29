using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-054: EF maps a <see cref="DateTime"/> to SQL Server's <c>datetime2</c>, of precision 7 unless
/// configured, which keeps every tick: nothing is rounded. A <see cref="DateTime"/> written as UTC reads back with
/// <see cref="DateTime.Kind"/> <see cref="DateTimeKind.Unspecified"/>, and equal, since equality ignores the kind. A
/// <see cref="DateTimeOffset"/> keeps its offset.
/// </summary>
[Trait("Needs", "Docker")]
public sealed class KB054
{
    public sealed class Stamped
    {
        public int Id { get; set; }

        public DateTime At { get; set; }

        public DateTimeOffset Offset { get; set; }
    }

    [Fact]
    public async Task Datetime2_keeps_every_tick_and_reads_back_unspecified()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var at = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var offset = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(2)).AddTicks(7654321);
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Stamped>());
        db.Model.FindEntityType(typeof(Stamped))!.FindProperty(nameof(Stamped.At))!.GetColumnType().ShouldBe("datetime2");
        db.Add(new Stamped { At = at, Offset = offset });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        var read = await db.Set<Stamped>().SingleAsync(cancellation);
        read.At.Ticks.ShouldBe(at.Ticks);
        read.At.Kind.ShouldBe(DateTimeKind.Unspecified);
        read.Offset.ShouldBe(offset);
        read.Offset.Offset.ShouldBe(TimeSpan.FromHours(2));
    }
}
