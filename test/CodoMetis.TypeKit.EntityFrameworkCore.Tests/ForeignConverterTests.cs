using System.Globalization;
using System.Linq.Expressions;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// <c>.Value</c> is the column only where the column holds <c>.Value</c>: where TypeKit's converter
/// stores the value object. A property converted by a converter of the application's own holds
/// something else, and the translation is refused rather than compared with what the column does not
/// hold.
/// </summary>
/// <remarks>
/// Measured on SQLite before the check: with <c>"C:" + c.Value</c> stored, <c>t.Code.Value == "abc"</c>
/// became <c>"t"."Code" = 'abc'</c> and found no row, where the row holds <c>abc</c> in memory; with a
/// quantity stored as text, <c>t.Quantity.Value &gt; 10</c> became <c>"t"."Quantity" &gt; 10</c> and found
/// the row holding 7.
/// </remarks>
public sealed class ForeignConverterTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public static TheoryData<ForeignConversion> Conversions => [ForeignConversion.Lambda, ForeignConversion.ConverterType, ForeignConversion.ModelConvention];

    [Theory]
    [MemberData(nameof(Conversions))]
    public void Value_on_a_column_another_converter_stores_is_refused(ForeignConversion conversion)
    {
        using var db = Seeded(conversion);

        Refusal(() => db.Coded.Count(c => c.Code.Value == "abc")).ShouldContain("could not be translated");
        Refusal(() => db.Coded.Count(c => c.Quantity.Value > 10)).ShouldContain("could not be translated");
        Refusal(() => db.Coded.Count(c => c.Quantity.GetValue() > 10)).ShouldContain("could not be translated");
    }

    /// <summary>The column still answers for the value object itself, through the application's converter.</summary>
    [Theory]
    [MemberData(nameof(Conversions))]
    public void The_value_object_itself_still_compares_through_the_application_s_converter(ForeignConversion conversion)
    {
        using var db = Seeded(conversion);

        db.Coded.Count(c => c.Code == CodeText.From("abc")).ShouldBe(1);
        db.Coded.Count(c => c.Quantity == ShipmentNo.From(7)).ShouldBe(1);
    }

    /// <summary>The control: TypeKit's converter named explicitly, as the README shows, still translates <c>.Value</c>.</summary>
    [Fact]
    public void Value_on_a_column_TypeKit_s_converter_stores_explicitly_translates()
    {
        using var db = Seeded(ForeignConversion.ExplicitTypeKit);

        db.Coded.Count(c => c.Code.Value == "abc").ShouldBe(1);
        db.Coded.Count(c => c.Quantity.Value > 10).ShouldBe(0);
        db.Coded.Where(c => c.Code.Value == "abc").ToQueryString().ShouldContain("WHERE \"c\".\"Code\" = 'abc'");
    }

    /// <summary>
    /// Beside a column the application converts, the columns TypeKit maps still translate: one over a
    /// string, and one over an enum, whose converter is TypeKit's composed onto the enum's own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Conversions))]
    public void Value_on_the_columns_TypeKit_maps_still_translates(ForeignConversion conversion)
    {
        using var db = Seeded(conversion);

        db.Model.FindEntityType(typeof(Coded))!.FindProperty(nameof(Coded.Day))!.GetTypeMapping().Converter.ShouldNotBeNull()
          .ConvertToProviderExpression.Body.ShouldNotBeAssignableTo<MethodCallExpression>("the enum's mapping is expected to carry a converter of its own");

        db.Coded.Count(c => c.Label.Value == "abc").ShouldBe(1);
        db.Coded.Count(c => c.Day.Value == DayOfWeek.Friday).ShouldBe(1);
        db.Coded.Count(c => c.Day.Value == DayOfWeek.Monday).ShouldBe(0);
    }

    /// <summary>
    /// A compiled model (<c>dotnet ef dbcontext optimize</c>) creates every converter again, from the
    /// expressions it wrote out as C#. TypeKit's are still told from the application's, the one composed
    /// onto an enum's included.
    /// </summary>
    [Fact]
    public void In_a_compiled_model_Value_translates_only_where_TypeKit_s_converter_stores_the_column()
    {
        using var design = Seeded(ForeignConversion.Lambda);
        var compiled = DesignTime.CompiledModel(design);
        using var db = new ForeignConvertersDb(
            new DbContextOptionsBuilder<ForeignConvertersDb>().UseSqlite(_connection).UseTypeKit().UseModel(compiled).Options, ForeignConversion.Lambda);

        db.Model.ShouldBeSameAs(compiled);
        db.Coded.Count(c => c.Label.Value == "abc").ShouldBe(1);
        db.Coded.Count(c => c.Day.Value == DayOfWeek.Friday).ShouldBe(1);
        Refusal(() => db.Coded.Count(c => c.Code.Value == "abc")).ShouldContain("could not be translated");
        Refusal(() => db.Coded.Count(c => c.Quantity.Value > 10)).ShouldContain("could not be translated");
    }

    public void Dispose() => _connection.Dispose();

    private ForeignConvertersDb Seeded(ForeignConversion conversion)
    {
        _connection.Open();
        var db = new ForeignConvertersDb(new DbContextOptionsBuilder<ForeignConvertersDb>().UseSqlite(_connection).UseTypeKit()
                                                                                  .ReplaceService<IModelCacheKeyFactory, ModelPerConversion>()
                                                                                  .Options, conversion);
        db.Database.EnsureCreated();
        db.Add(new Coded { Code = CodeText.From("abc"), Quantity = ShipmentNo.From(7), Label = LabelText.From("abc"), Day = ProbeWeekday.From(DayOfWeek.Friday) });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        return db;
    }

    private static string Refusal(Func<int> query) => Should.Throw<InvalidOperationException>(() => query()).Message;
}

/// <summary>EF caches a model per context type; these need one per conversion.</summary>
public sealed class ModelPerConversion : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), (context as ForeignConvertersDb)?.Conversion, designTime);
}

public enum ForeignConversion
{
    /// <summary><c>HasConversion(to, from)</c> with lambdas.</summary>
    Lambda,

    /// <summary><c>HasConversion&lt;TConverter&gt;()</c>.</summary>
    ConverterType,

    /// <summary><c>ConfigureConventions</c>: <c>Properties&lt;T&gt;().HaveConversion&lt;TConverter&gt;()</c>.</summary>
    ModelConvention,

    /// <summary><c>HasConversion&lt;ValueObjectConverter&lt;,&gt;&gt;()</c>: TypeKit's own, named.</summary>
    ExplicitTypeKit,
}

public sealed class Coded
{
    public int Id { get; set; }

    public CodeText Code { get; set; } = null!;

    public ShipmentNo Quantity { get; set; }

    /// <summary>Never configured: TypeKit maps it.</summary>
    public LabelText Label { get; set; } = null!;

    /// <summary>Never configured: TypeKit's converter composed onto the enum's.</summary>
    public ProbeWeekday Day { get; set; }
}

/// <summary>
/// Record classes: a struct over a string cannot be a property in a compiled model (the package README's
/// first EF limitation).
/// </summary>
public sealed partial record CodeText : IValue<string>;

public sealed partial record LabelText : IValue<string>;

/// <summary>Stores a code upper-cased: not what <c>.Value</c> answers.</summary>
public sealed class UpperCodeConverter() : ValueConverter<CodeText, string>(code => code.Value.ToUpperInvariant(), text => CodeText.From(text.ToLowerInvariant()));

/// <summary>Stores a number as text.</summary>
public sealed class TextQuantityConverter()
    : ValueConverter<ShipmentNo, string>(quantity => quantity.Value.ToString(CultureInfo.InvariantCulture), text => ShipmentNo.From(int.Parse(text, CultureInfo.InvariantCulture)));

public sealed class ForeignConvertersDb(DbContextOptions<ForeignConvertersDb> options, ForeignConversion conversion) : DbContext(options)
{
    public DbSet<Coded> Coded => Set<Coded>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (conversion != ForeignConversion.ModelConvention) return;

        configurationBuilder.Properties<CodeText>().HaveConversion<UpperCodeConverter>();
        configurationBuilder.Properties<ShipmentNo>().HaveConversion<TextQuantityConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var coded = modelBuilder.Entity<Coded>();

        switch (conversion)
        {
            case ForeignConversion.Lambda:
                coded.Property(c => c.Code).HasConversion(PrefixedCode, code => CodeText.From(code.Substring(2)));
                coded.Property(c => c.Quantity).HasConversion(quantity => quantity.Value.ToString(CultureInfo.InvariantCulture), text => ShipmentNo.From(int.Parse(text, CultureInfo.InvariantCulture)));
                break;
            case ForeignConversion.ConverterType:
                coded.Property(c => c.Code).HasConversion<UpperCodeConverter>();
                coded.Property(c => c.Quantity).HasConversion<TextQuantityConverter>();
                break;
            case ForeignConversion.ExplicitTypeKit:
                coded.Property(c => c.Code).HasConversion<ValueObjectConverter<CodeText, string>>();
                coded.Property(c => c.Quantity).HasConversion<ValueObjectConverter<ShipmentNo, int>>();
                break;
        }
    }

    public ForeignConversion Conversion => conversion;

    private static readonly Expression<Func<CodeText, string>> PrefixedCode = code => "C:" + code.Value;
}
