using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-035: Npgsql refuses a <see cref="DateTime"/> that is not UTC and a <see cref="DateTimeOffset"/>
/// whose offset is not zero for a <c>timestamptz</c>, written or sent as a parameter.
/// </summary>
public sealed class KB035
{
    public sealed class Stamped
    {
        public int Id { get; set; }

        public DateTime At { get; set; }

        public DateTimeOffset On { get; set; }
    }

    [Fact]
    public async Task A_non_UTC_DateTime_and_a_non_zero_offset_are_refused_for_timestamptz()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Stamped>());

        db.Add(new Stamped { At = DateTime.UtcNow, On = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)) });
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellation))).InnerException.ShouldBeOfType<ArgumentException>()
            .Message.ShouldContain("only offset 0 (UTC) is supported");
        db.ChangeTracker.Clear();

        db.Add(new Stamped { At = new DateTime(2026, 1, 1), On = DateTimeOffset.UtcNow });
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellation))).InnerException!.Message.ShouldContain("Kind=Unspecified");
        db.ChangeTracker.Clear();

        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        (await Should.ThrowAsync<ArgumentException>(() => db.Set<Stamped>().Where(s => s.At > local).ToListAsync(cancellation))).Message.ShouldContain("Kind=Local");
    }
}
