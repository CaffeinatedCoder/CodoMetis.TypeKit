using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-051: EF maps a <see cref="TimeSpan"/> to SQL Server's <c>time</c>, which holds less than a day.
/// In a column, <c>SaveChanges</c> refuses a value of a day or more. In a JSON column the value is written as its text,
/// <c>"1.02:00:00"</c>, and a query that reads the property converts that text to <c>time</c>: it answers
/// <c>01:02:00</c>, without an error, where the entity materialized whole reads <c>1.02:00:00</c>.
/// </summary>
[Trait("Needs", "Docker")]
public sealed class KB051
{
    public sealed class Span
    {
        public int Id { get; set; }

        public TimeSpan Length { get; set; }
    }

    public sealed class Holder
    {
        public int Id { get; set; }

        public Spans Doc { get; set; } = new();
    }

    public sealed class Spans
    {
        public TimeSpan Length { get; set; }
    }

    [Fact]
    public async Task A_time_column_refuses_a_day_or_more()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Span>());
        db.Model.FindEntityType(typeof(Span))!.FindProperty(nameof(Span.Length))!.GetColumnType().ShouldBe("time");

        db.Add(new Span { Length = TimeSpan.FromHours(26) });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellation));
    }

    [Fact]
    public async Task A_day_or_more_in_a_JSON_column_is_misread_by_a_query()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Holder>().OwnsOne(h => h.Doc, doc => doc.ToJson()));
        db.Add(new Holder { Doc = new Spans { Length = TimeSpan.FromHours(26) } });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        (await db.Set<Holder>().Select(h => h.Doc.Length).SingleAsync(cancellation)).ShouldBe(new TimeSpan(1, 2, 0));
        (await db.Set<Holder>().SingleAsync(cancellation)).Doc.Length.ShouldBe(TimeSpan.FromHours(26));
    }
}
