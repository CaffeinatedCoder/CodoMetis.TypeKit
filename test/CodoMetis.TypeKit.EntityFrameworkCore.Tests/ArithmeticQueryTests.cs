using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// The arithmetic operators a value object declares, in a query: between two value objects EF translates them as
/// it translates the wrapped type's, and the database computes them, so a filter keeps the rows memory keeps.
/// </summary>
/// <remarks>
/// EF reads the operator as SQL arithmetic and never runs it (<c>ef_add("p"."Price", "p"."Tax")</c> on SQLite,
/// measured 2026-10-30, as for two <see cref="decimal"/> columns). With an operand or a result of another type
/// (<c>Price * 2m</c>, <c>Tax / Price</c> as a <see cref="decimal"/>) it applies the value object's converter to the
/// other side, and the query throws: <c>.Value * 2m</c> is the form that translates.
/// </remarks>
public sealed class ArithmeticQueryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public void An_operator_between_value_objects_runs_in_the_database()
    {
        using var db = Seeded();

        db.Priced.Where(p => p.Price + p.Tax > ProbeMoney.From(3m)).Select(p => p.Id).ToList().ShouldBe([2]);
        db.Priced.Where(p => p.Price - p.Tax < ProbeMoney.From(2m)).Select(p => p.Id).ToList().ShouldBe([1]);
        db.Priced.Where(p => -p.Price < ProbeMoney.From(-5m)).Select(p => p.Id).ToList().ShouldBe([2]);
        db.Priced.OrderBy(p => p.Id).Select(p => (p.Price + p.Tax).Value).ToList().ShouldBe([2.0m, 12.0m]);
        db.Priced.Where(p => p.Price + p.Tax > ProbeMoney.From(3m)).ToQueryString().ShouldContain("ef_add(");
    }

    [Fact]
    public void An_operator_with_another_operand_throws_and_Value_translates()
    {
        using var db = Seeded();

        Should.Throw<InvalidCastException>(() => db.Priced.Where(p => p.Price * 2m > ProbeMoney.From(10m)).ToList());
        Should.Throw<InvalidOperationException>(() => db.Priced.Select(p => p.Tax / p.Price).ToList());
        db.Priced.Where(p => p.Price.Value * 2m > 10m).Select(p => p.Id).ToList().ShouldBe([2]);
    }

    public void Dispose() => _connection.Dispose();

    private PricedDb Seeded()
    {
        _connection.Open();
        var db = new PricedDb(new DbContextOptionsBuilder<PricedDb>().UseSqlite(_connection).UseTypeKit().Options);
        db.Database.EnsureCreated();
        db.Priced.AddRange(new Priced { Id = 1, Price = ProbeMoney.From(1.5m), Tax = ProbeMoney.From(0.5m) },
                           new Priced { Id = 2, Price = ProbeMoney.From(10m), Tax = ProbeMoney.From(2m) });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    public sealed class Priced
    {
        public int Id { get; set; }

        public ProbeMoney Price { get; set; }

        public ProbeMoney Tax { get; set; }
    }

    public sealed class PricedDb(DbContextOptions<PricedDb> options) : DbContext(options)
    {
        public DbSet<Priced> Priced => Set<Priced>();
    }
}
