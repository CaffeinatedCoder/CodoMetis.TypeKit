using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-048: under SQL Server's default collation, <c>SQL_Latin1_General_CP1_CI_AS</c>, text compares
/// ignoring case and trailing spaces: <c>==</c>, <c>Contains</c> over a list (<c>IN</c>) and <c>StartsWith</c>
/// (<c>LIKE</c>) match rows that differ in case, and <c>ORDER BY</c> sorts ignoring case. A binary collation
/// (<c>Latin1_General_BIN2</c>) gives the ordinal order.
/// </summary>
[Trait("Needs", "Docker")]
public sealed class KB048
{
    public sealed class Named
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    [Fact]
    public async Task Text_compares_and_orders_ignoring_case()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Named>());
        db.AddRange(new Named { Name = "alpha" }, new Named { Name = "Bravo" }, new Named { Name = "charlie" });
        await db.SaveChangesAsync(cancellation);
        List<string> upper = ["ALPHA"];

        (await db.Set<Named>().Where(n => n.Name == "BRAVO").Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["Bravo"]);
        (await db.Set<Named>().Where(n => n.Name == "alpha  ").Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["alpha"]);
        (await db.Set<Named>().Where(n => upper.Contains(n.Name)).Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["alpha"]);
        (await db.Set<Named>().Where(n => n.Name.StartsWith("b")).Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["Bravo"]);
        (await db.Set<Named>().OrderBy(n => n.Name).Select(n => n.Name).ToListAsync(cancellation)).ShouldBe(["alpha", "Bravo", "charlie"]);
        (await db.Set<Named>().OrderBy(n => EF.Functions.Collate(n.Name, "Latin1_General_BIN2")).Select(n => n.Name).ToListAsync(cancellation))
            .ShouldBe(["Bravo", "alpha", "charlie"]);
    }
}
