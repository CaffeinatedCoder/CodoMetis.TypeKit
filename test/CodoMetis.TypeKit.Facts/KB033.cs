using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-033: on PostgreSQL <c>StartsWith</c> is case-sensitive (<c>LIKE 'b%'</c>), and ordering
/// follows the database's collation, <c>en_US.utf8</c> in the <c>postgres:17</c> image, not ordinal order, which
/// the <c>C</c> collation gives.
/// </summary>
public sealed class KB033
{
    public sealed class Named
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    [Fact]
    public async Task StartsWith_is_case_sensitive_and_order_follows_the_collation()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await Pg.ContextAsync(model => model.Entity<Named>());
        db.AddRange(new Named { Name = "alpha" }, new Named { Name = "Bravo" }, new Named { Name = "charlie" }, new Named { Name = "Äpfel" });
        await db.SaveChangesAsync(cancellation);

        (await db.Set<Named>().Where(n => n.Name.StartsWith("b")).ToListAsync(cancellation)).ShouldBeEmpty();
        (await db.Set<Named>().OrderBy(n => n.Name).Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["alpha", "Äpfel", "Bravo", "charlie"]);
        (await db.Set<Named>().OrderBy(n => EF.Functions.Collate(n.Name, "C")).Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["Bravo", "alpha", "charlie", "Äpfel"]);
    }
}
