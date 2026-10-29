using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-047: EF Core gives a key generated on add whose type is no number, converted to a <see cref="decimal"/>
/// column, a negative temporary <see cref="decimal"/>, which a conversion to <see cref="ulong"/> refuses. A value generator
/// factory in the converter's mapping hints is consulted instead, after a generator configured on the property and after
/// Npgsql's HiLo. No database is opened.
/// </summary>
public sealed class KB047
{
    public readonly record struct Code(ulong Value);

    public readonly record struct Number(long Value);

    public sealed class ByCode
    {
        public Code Id { get; set; }
    }

    public sealed class ByNumber
    {
        public Number Id { get; set; }
    }

    private static readonly Code Hinted = new(7);

    /// <summary>A <see cref="Code"/> stored as a <see cref="decimal"/>, as SQL Server and PostgreSQL store a <see cref="ulong"/>.</summary>
    private static ValueConverter<Code, decimal> CodeConverter(bool hinted) =>
        new(code => (decimal)code.Value, value => new Code((ulong)value), hinted ? new ConverterMappingHints(valueGeneratorFactory: (_, _) => new Fixed<Code>(Hinted)) : null);

    private sealed class Fixed<T>(T value) : ValueGenerator<T>
    {
        public override bool GeneratesTemporaryValues => true;

        public override T Next(EntityEntry entry) => value;
    }

    private sealed class Own : ValueGenerator<Code>
    {
        public override bool GeneratesTemporaryValues => false;

        public override Code Next(EntityEntry entry) => new(42);
    }

    private sealed class Db(DbContextOptions options, Action<ModelBuilder> configure) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    }

    private sealed class Uncached : IModelCacheKeyFactory
    {
        private static int _next;

        public object Create(DbContext context, bool designTime) => Interlocked.Increment(ref _next);
    }

    private static Db Create(Action<ModelBuilder> configure) =>
        new(new DbContextOptionsBuilder().UseNpgsql("Host=localhost;Database=none").ReplaceService<IModelCacheKeyFactory, Uncached>().Options, configure);

    [Fact]
    public void Without_hints_the_temporary_decimal_is_refused_converted_back()
    {
        using var db = Create(model => model.Entity<ByCode>().Property(e => e.Id).HasConversion(CodeConverter(hinted: false)).ValueGeneratedOnAdd());

        Should.Throw<OverflowException>(() => db.Add(new ByCode()));
    }

    [Fact]
    public void The_hints_factory_fills_the_key()
    {
        using var db = Create(model => model.Entity<ByCode>().Property(e => e.Id).HasConversion(CodeConverter(hinted: true)).ValueGeneratedOnAdd());

        var entity = new ByCode();
        db.Add(entity);

        db.Entry(entity).Property(e => e.Id).CurrentValue.ShouldBe(Hinted);
    }

    [Fact]
    public void A_generator_on_the_property_comes_before_the_hints_factory()
    {
        using var db = Create(model => model.Entity<ByCode>().Property(e => e.Id).HasConversion(CodeConverter(hinted: true)).HasValueGenerator<Own>());

        var entity = new ByCode();
        db.Add(entity);

        entity.Id.ShouldBe(new Code(42));
    }

    [Fact]
    public void Npgsql_s_HiLo_comes_before_the_hints_factory()
    {
        var converter = new ValueConverter<Number, long>(
            number => number.Value, value => new Number(value), new ConverterMappingHints(valueGeneratorFactory: (_, _) => new Fixed<Number>(new Number(7))));
        using var db = Create(model => model.Entity<ByNumber>().Property(e => e.Id).HasConversion(converter).UseHiLo());

        var entityType = db.Model.FindEntityType(typeof(ByNumber))!;
        db.GetService<IValueGeneratorSelector>().TrySelect(entityType.FindPrimaryKey()!.Properties[0], entityType, out var generator).ShouldBeTrue();

        generator.ShouldNotBeOfType<Fixed<Number>>();
        generator!.GeneratesTemporaryValues.ShouldBeFalse();
    }
}
