using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-031: a <see cref="char"/> is a <c>character(1)</c> on PostgreSQL, and a space in it is
/// removed when SQL converts it to text, as in a concatenation; the column itself reads back as the space.
/// </summary>
public sealed class KB031
{
    public sealed class Letter
    {
        public int Id { get; set; }

        public char C { get; set; }
    }

    [Fact]
    public async Task A_space_in_character_1_is_removed_when_SQL_converts_it_to_text()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Letter>());
        db.Add(new Letter { C = ' ' });
        await db.SaveChangesAsync(cancellation);
        db.ChangeTracker.Clear();

        (await db.Set<Letter>().Select(l => l.C).SingleAsync(cancellation)).ShouldBe(' ');
        (await db.Set<Letter>().Select(l => "[" + l.C + "]").SingleAsync(cancellation)).ShouldBe("[]");
    }
}
