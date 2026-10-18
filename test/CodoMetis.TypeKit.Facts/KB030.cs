using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-030: <see cref="DateTime.MaxValue"/> written to a <c>timestamptz</c> as UTC reads back as
/// <see cref="DateTime.MaxValue"/> with <see cref="DateTimeKind.Unspecified"/>: Npgsql stores it as infinity.
/// </summary>
public sealed class KB030
{
    public sealed class Stamped
    {
        public int Id { get; set; }

        public DateTime At { get; set; }

        public DateTimeOffset On { get; set; }
    }

    [Fact]
    public async Task DateTime_MaxValue_as_UTC_reads_back_Unspecified()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Stamped>());
        db.Add(new Stamped { At = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), On = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        var at = (await db.Set<Stamped>().SingleAsync(cancellation)).At;

        at.ShouldBe(DateTime.MaxValue);
        at.Kind.ShouldBe(DateTimeKind.Unspecified);
    }
}
