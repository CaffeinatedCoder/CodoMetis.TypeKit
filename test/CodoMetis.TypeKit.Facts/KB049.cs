using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-049: SQL Server orders a <c>uniqueidentifier</c> by its last group first (bytes 10 to 15), then
/// the fourth, third, second and first, where <see cref="Guid.CompareTo(Guid)"/> starts at the first group. So
/// <c>ORDER BY</c>, <c>&lt;</c> and <c>MIN</c> over Guids answer in another order than memory does.
/// </summary>
[Trait("Needs", "Docker")]
public sealed class KB049
{
    public sealed class Keyed
    {
        public int Id { get; set; }

        public Guid Key { get; set; }
    }

    [Fact]
    public async Task A_uniqueidentifier_orders_by_its_last_group_first()
    {
        var cancellation = TestContext.Current.CancellationToken;
        Guid first = Guid.Parse("00000001-0000-0000-0000-000000000003"), second = Guid.Parse("00000002-0000-0000-0000-000000000001"), third = Guid.Parse("00000003-0000-0000-0000-000000000002");
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Keyed>());
        db.AddRange(new Keyed { Key = first }, new Keyed { Key = second }, new Keyed { Key = third });
        await db.SaveChangesAsync(cancellation);

        new[] { third, first, second }.Order().ShouldBe([first, second, third]);
        (await db.Set<Keyed>().OrderBy(k => k.Key).Select(k => k.Key).ToListAsync(cancellation)).ShouldBe([second, third, first]);
        (await db.Set<Keyed>().MinAsync(k => k.Key, cancellation)).ShouldBe(second);
        (await db.Set<Keyed>().Where(k => k.Key < first).Select(k => k.Key).OrderBy(k => k).ToListAsync(cancellation)).ShouldBe([second, third]);
    }
}
