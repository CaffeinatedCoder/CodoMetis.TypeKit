using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>A closed set as a column: the member's name, read back as the member itself, with nothing registered per set.</summary>
public sealed class ClosedSetMappingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ClosedSetMappingTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private DbContextOptions<SetDb> Options => new DbContextOptionsBuilder<SetDb>().UseSqlite(_connection).UseTypeKit().Options;

    [Fact]
    public void A_closed_set_property_is_a_text_column_of_the_member_s_name()
    {
        using var db = new SetDb(Options);

        var status = db.Model.FindEntityType(typeof(Ticket))!.FindProperty(nameof(Ticket.Status)).ShouldNotBeNull();
        status.GetTypeMapping().Converter.ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(string));
        status.GetColumnType().ShouldBe("TEXT");
    }

    [Fact]
    public void A_member_round_trips_and_a_query_compares_by_name()
    {
        using (var db = new SetDb(Options))
        {
            db.Database.EnsureCreated();
            db.Tickets.AddRange(new Ticket { Id = 1, Status = ProbeStatus.Open, Tier = ProbeTier.Premium }, new Ticket { Id = 2, Status = ProbeStatus.Shipped, Tier = ProbeTier.Basic });
            db.SaveChanges();
        }

        using (var db = new SetDb(Options))
        {
            var shipped = db.Tickets.Single(ticket => ticket.Status == ProbeStatus.Shipped);
            ReferenceEquals(shipped.Status, ProbeStatus.Shipped).ShouldBeTrue();
            ReferenceEquals(shipped.Tier, ProbeTier.Basic).ShouldBeTrue();

            db.Tickets.Where(ticket => ticket.Tier == ProbeTier.Premium).ToQueryString().ShouldContain("'Premium'");
        }
    }

    /// <summary>
    /// An instance the set's own code made beside its members has no name. Before, a nullable column
    /// stored its null name and read it back as no value at all.
    /// </summary>
    [Fact]
    public void An_instance_that_is_not_a_member_is_never_stored()
    {
        using var db = new SetDb(Options);
        db.Database.EnsureCreated();
        db.Tickets.Add(new Ticket { Id = 3, Level = ProbeLevel.Unlisted() });

        var thrown = Should.Throw<Exception>(() => db.SaveChanges());
        var cause = thrown as InvalidOperationException ?? thrown.InnerException.ShouldBeOfType<InvalidOperationException>();
        cause.Message.ShouldEndWith("#an-instance-that-is-not-a-member-of-its-set");
    }

    public sealed class Ticket
    {
        public int Id { get; set; }

        public ProbeStatus Status { get; set; } = ProbeStatus.Open;

        public ProbeTier Tier { get; set; } = ProbeTier.Basic;

        public ProbeLevel? Level { get; set; }
    }

    public sealed class SetDb(DbContextOptions<SetDb> options) : DbContext(options)
    {
        public DbSet<Ticket> Tickets => Set<Ticket>();
    }
}
