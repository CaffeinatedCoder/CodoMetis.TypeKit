using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-052: EF translates <c>Max</c> and <c>Min</c> of a <see cref="bool"/> column to <c>MAX</c> and
/// <c>MIN</c> of a <c>bit</c>, which SQL Server refuses: "Operand data type bit is invalid for max operator".
/// </summary>
[Trait("Needs", "Docker")]
public sealed class KB052
{
    public sealed class Flagged
    {
        public int Id { get; set; }

        public bool Flag { get; set; }
    }

    [Fact]
    public async Task Max_and_min_of_a_bit_are_refused()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var db = await SqlServer.ContextAsync(model => model.Entity<Flagged>());
        db.AddRange(new Flagged { Flag = false }, new Flagged { Flag = true });
        await db.SaveChangesAsync(cancellation);

        (await Should.ThrowAsync<SqlException>(() => db.Set<Flagged>().MaxAsync(f => f.Flag, cancellation))).Message.ShouldContain("Operand data type bit is invalid for max operator");
        (await Should.ThrowAsync<SqlException>(() => db.Set<Flagged>().MinAsync(f => f.Flag, cancellation))).Message.ShouldContain("Operand data type bit is invalid for min operator");
    }
}
