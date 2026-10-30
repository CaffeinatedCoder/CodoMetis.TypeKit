using System.Numerics;

using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// An operator a value object declares by hand cannot run in the database, where EF translates it as the database's
/// arithmetic and never calls it: the query is refused, naming the value object and <c>.Value</c> (#187).
/// </summary>
/// <remarks>
/// Measured on SQLite with the refusal taken out, with <see cref="RoundedMoney"/> holding 1.005 and 0.001: its
/// <c>+</c> rounds the sum to 1.01, the database added the columns to 1.006, and <c>Price + Tax &gt; 1.006</c> kept
/// no row where memory keeps the one; projected, the sum read back as 1.006.
/// </remarks>
public sealed class HandWrittenArithmeticTests : IDisposable
{
    private static readonly RoundedMoney Limit = RoundedMoney.From(1.006m);

    private static readonly Dictionary<string, Func<LedgerDb, object>> Refused = new()
    {
        ["+ between two"]              = db => db.Lines.Where(l => l.Price + l.Tax > Limit).ToList(),
        ["+ in a projection"]          = db => db.Lines.Select(l => l.Price + l.Tax).ToList(),
        ["+ in a checked context"]     = db => db.Lines.Where(l => checked(l.Price + l.Tax) > Limit).ToList(),
        ["+ of an optional one"]       = db => db.Lines.Where(l => l.Optional + l.Tax > Limit).ToList(),
        ["* without an interface"]     = db => db.Lines.Where(l => l.Price * 2m > Limit).ToList(),
        ["unary -"]                    = db => db.Lines.Select(l => -l.Price).ToList(),
        ["| of a bitwise one"]         = db => db.Lines.Where(l => (l.Access | Permissions.From(1)) == Permissions.From(3)).ToList(),
        ["~ of a bitwise one"]         = db => db.Lines.Select(l => ~l.Access).ToList(),
        ["+ of a validated one"]       = db => db.Lines.Where(l => l.Allowance + l.Allowance == Allowance.FromKnownGood(4)).ToList(),
        ["+ over a navigation"]        = db => db.Ledgers.Where(g => g.Lines.Any(l => l.Price + l.Tax > Limit)).ToList(),
        ["+ in OrderBy"]               = db => db.Lines.OrderBy(l => l.Price + l.Tax).ToList(),
    };

    public static TheoryData<string> RefusedCases => [.. Refused.Keys];

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public void A_hand_written_operator_in_a_query_is_refused_naming_the_value_object_and_Value(string name)
    {
        using var db = Seeded();

        var refused = Should.Throw<InvalidOperationException>(() => Refused[name](db));

        refused.Message.ShouldMatch(@"^(RoundedMoney|Permissions|Allowance)'s operator \S+ is used in a query, but it is written by hand");
        refused.Message.ShouldContain(".Value");
    }

    /// <summary>The generated <c>-</c> beside a hand-written <c>+</c>, the wrapped type's arithmetic, and an operator over captured values alone, which EF runs in memory before the query.</summary>
    [Fact]
    public void What_the_database_can_compute_as_written_stays_allowed()
    {
        using var db = Seeded();

        db.Lines.Where(l => l.Price - l.Tax > RoundedMoney.From(1.004m)).Select(l => l.Id).ToList().ShouldBe([2]);
        db.Lines.Where(l => l.Price.Value + l.Tax.Value > 1.006m).Select(l => l.Id).ToList().ShouldBe([2]);

        // Rounded in memory to 1.01; added in the database it would be 1.006, and no row.
        db.Lines.Where(l => l.Price == RoundedMoney.From(1.005m) + RoundedMoney.From(0.001m)).Select(l => l.Id).ToList().ShouldBe([2]);
    }

    [Fact]
    public void A_query_filter_with_a_hand_written_operator_is_refused_when_the_model_is_built()
    {
        _connection.Open();
        using var db = new FilteredDb(new DbContextOptionsBuilder<FilteredDb>().UseSqlite(_connection).UseTypeKit().Options);

        var refused = Should.Throw<InvalidOperationException>(() => db.Model);

        refused.Message.ShouldStartWith("RoundedMoney's operator + is used in the query filter of Line, but it is written by hand");
    }

    [Fact]
    public void A_named_query_filter_with_a_hand_written_operator_is_refused_naming_it()
    {
        _connection.Open();
        using var db = new NamedFilteredDb(new DbContextOptionsBuilder<NamedFilteredDb>().UseSqlite(_connection).UseTypeKit().Options);

        var refused = Should.Throw<InvalidOperationException>(() => db.Model);

        refused.Message.ShouldStartWith("RoundedMoney's operator + is used in the query filter \"Rounded\" of Line, but it is written by hand");
    }

    /// <summary>
    /// A filter's operator over the context's own values runs in memory, as written, when the query runs: it rounds
    /// 1.005 + 0.001 to 1.01, where the database's sum would keep the other row.
    /// </summary>
    [Fact]
    public void A_query_filter_with_a_hand_written_operator_over_captured_values_stays_allowed()
    {
        _connection.Open();
        using var db = new CapturedFilterDb(new DbContextOptionsBuilder<CapturedFilterDb>().UseSqlite(_connection).UseTypeKit().Options);
        db.Database.EnsureCreated();
        db.Ledgers.Add(new Ledger { Id = 1, Lines = [NewLine(1, 1.01m, 0m), NewLine(2, 1.006m, 0m)] });
        db.SaveChanges();

        db.Lines.Select(l => l.Id).ToList().ShouldBe([1]);
    }

    public void Dispose() => _connection.Dispose();

    private LedgerDb Seeded()
    {
        _connection.Open();
        var db = new LedgerDb(new DbContextOptionsBuilder<LedgerDb>().UseSqlite(_connection).UseTypeKit().Options);
        db.Database.EnsureCreated();
        db.Ledgers.Add(new Ledger { Id = 1, Lines = [NewLine(1, 1.005m, 0.001m), NewLine(2, 1.01m, 0m)] });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static Line NewLine(int id, decimal price, decimal tax) => new()
    {
        Id = id, LedgerId = 1, Price = RoundedMoney.From(price), Tax = RoundedMoney.From(tax),
        Access = Permissions.From(2), Allowance = Allowance.FromKnownGood(2),
    };

    public sealed class Line
    {
        public int Id { get; set; }

        public int LedgerId { get; set; }

        public RoundedMoney Price { get; set; }

        public RoundedMoney Tax { get; set; }

        public RoundedMoney? Optional { get; set; }

        public Permissions Access { get; set; }

        public Allowance Allowance { get; set; }
    }

    public sealed class Ledger
    {
        public int Id { get; set; }

        public List<Line> Lines { get; set; } = [];
    }

    public class LedgerDb(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Line> Lines => Set<Line>();

        public DbSet<Ledger> Ledgers => Set<Ledger>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Line>().Property(l => l.Id).ValueGeneratedNever();
            modelBuilder.Entity<Ledger>().Property(l => l.Id).ValueGeneratedNever();
        }
    }

    private sealed class FilteredDb(DbContextOptions<FilteredDb> options) : LedgerDb(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Line>().HasQueryFilter(l => l.Price + l.Tax > RoundedMoney.From(1m));
        }
    }

    private sealed class NamedFilteredDb(DbContextOptions<NamedFilteredDb> options) : LedgerDb(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Line>().HasQueryFilter("Plain", l => l.Price.Value > 0m).HasQueryFilter("Rounded", l => l.Price + l.Tax > RoundedMoney.From(1m));
        }
    }

    private sealed class CapturedFilterDb(DbContextOptions<CapturedFilterDb> options) : LedgerDb(options)
    {
        private readonly RoundedMoney _base = RoundedMoney.From(1.005m);

        private readonly RoundedMoney _step = RoundedMoney.From(0.001m);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Line>().HasQueryFilter(l => l.Price == _base + _step);
        }
    }
}

/// <summary>
/// Rounds its sum to cents by hand, as 2.3 wrote an operator, beside a generated <c>-</c>; scales and negates by
/// hand without declaring an interface.
/// </summary>
public readonly partial record struct RoundedMoney : IValue<decimal>,
    IAdditionOperators<RoundedMoney, RoundedMoney, RoundedMoney>,
    ISubtractionOperators<RoundedMoney, RoundedMoney, RoundedMoney>
{
    public static RoundedMoney operator +(RoundedMoney left, RoundedMoney right) => From(Math.Round(left.Value + right.Value, 2));

    public static RoundedMoney operator *(RoundedMoney money, decimal factor) => From(Math.Round(money.Value * factor, 2));

    public static RoundedMoney operator -(RoundedMoney money) => From(-money.Value);
}

/// <summary>Bitwise operators by hand, which TypeKit never generates: <c>~</c> keeps three bits.</summary>
public readonly partial record struct Permissions : IValue<int>
{
    public static Permissions operator |(Permissions left, Permissions right) => From(left.Value | right.Value);

    public static Permissions operator ~(Permissions permissions) => From(~permissions.Value & 7);
}

public enum AllowanceFault { Negative }

/// <summary>A validated value object, which gets no generated operator: its <c>+</c> is always its own.</summary>
public readonly partial record struct Allowance : IValidatedValue<Allowance, int, AllowanceFault>
{
    public static Result<Allowance, AllowanceFault> Create(int value) => value >= 0 ? new Allowance(value) : Result.Error(AllowanceFault.Negative);

    public static Allowance operator +(Allowance left, Allowance right) => FromKnownGood(left.Value + right.Value);
}
