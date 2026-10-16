using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-029: Npgsql truncates a value's ticks below a microsecond, for <see cref="DateTime"/>,
/// <see cref="DateTimeOffset"/>, <see cref="TimeOnly"/> and <see cref="TimeSpan"/> alike, and does not round them.
/// </summary>
public sealed class KB029
{
    public sealed class Ticks
    {
        public int Id { get; set; }

        public DateTime At { get; set; }

        public DateTimeOffset On { get; set; }

        public TimeOnly T { get; set; }

        public TimeSpan S { get; set; }
    }

    [Fact]
    public async Task Ticks_below_a_microsecond_are_truncated_on_write()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Ticks>());
        var at = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1234567);
        db.Add(new Ticks
        {
            At = at,
            On = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(9),
            T  = new TimeOnly(2, 0).Add(TimeSpan.FromTicks(5)),
            S  = TimeSpan.FromHours(26).Add(TimeSpan.FromTicks(3)),
        });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        var back = await db.Set<Ticks>().SingleAsync(cancellation);

        back.At.ToString("o").ShouldBe("2026-03-04T05:06:07.1234560Z", "truncated, where rounding would give .1234570");
        back.At.Kind.ShouldBe(DateTimeKind.Utc);
        back.On.Ticks.ShouldBe(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).Ticks);
        back.T.Ticks.ShouldBe(72_000_000_000);
        back.S.Ticks.ShouldBe(936_000_000_000);
    }
}
