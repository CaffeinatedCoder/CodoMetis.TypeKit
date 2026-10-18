using CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A precision or a column type configured on a value-object property reaches the column, on PostgreSQL,
/// for every kind of the catalogue over <c>decimal</c>, <c>string</c> and <c>DateTime</c>, configured on the
/// property and through <c>ConfigureConventions</c>, each against the same configuration of the wrapped type.
/// </summary>
/// <remarks>
/// The oracle is the README: "A facet you configure on the property, such as a maximum length or a column
/// type, is kept", and "Configure the value object instead: <c>configurationBuilder.Properties&lt;Amount&gt;().HavePrecision(18, 2)</c>".
/// The column type annotation says what was configured, not what the mapping made of it, so the test reads
/// the mapping's store type and the create script.
/// </remarks>
public sealed class ColumnFacetVariantTests
{
    /// <summary>Each wrapped type's facet and the column type PostgreSQL is to get for it.</summary>
    private static readonly Dictionary<Type, (Facet Facet, string StoreType)> Facets = new()
    {
        [typeof(decimal)]  = (Facet.Precision18And2, "numeric(18,2)"),
        [typeof(string)]   = (Facet.Citext, "citext"),
        [typeof(DateTime)] = (Facet.TimestampWithoutTimeZone, "timestamp without time zone"),
    };

    private static IEnumerable<WrappedKind> Kinds() =>
        WrappedKinds.Keys.Concat(WrappedKinds.Queries).Distinct().Where(kind => Facets.ContainsKey(kind.Wrapped));

    public static TheoryData<string, FacetSite> Cases
    {
        get
        {
            var cases = new TheoryData<string, FacetSite>();
            foreach (var kind in Kinds())
            foreach (var site in Enum.GetValues<FacetSite>())
                cases.Add(kind.Name, site);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_configured_facet_reaches_the_column_as_for_the_wrapped_type(string kind, FacetSite site)
    {
        var wrapped = WrappedKinds.Named(kind);
        var (facet, storeType) = Facets[wrapped.Wrapped];

        var plain = Column(wrapped.Wrapped, facet, site, typeKit: false);
        var valueObject = Column(wrapped.ValueObject, facet, site, typeKit: true);

        plain.ShouldBe((storeType, storeType), $"the wrapped type's own column, {facet} {site}: the oracle");
        valueObject.ShouldBe(plain, $"{kind} with {facet} configured {site}");
    }

    /// <summary>A guard against a vacuous run: the catalogue's kinds over each wrapped type, string as a struct and as record classes.</summary>
    [Fact]
    public void The_catalogue_has_kinds_over_each_wrapped_type() =>
        Kinds().Select(kind => kind.Name).ShouldBe(["string", "decimal", "DateTime", "string, record class", "string, record class, own CompareTo"], ignoreOrder: true);

    /// <summary>The mapping's store type, and the column's type in the create script.</summary>
    private static (string Mapping, string Script) Column(Type value, Facet facet, FacetSite site, bool typeKit)
    {
        using var db = FacetDb.Create(value, facet, site, typeKit);
        var property = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Faceted<>).MakeGenericType(value))!.FindProperty("Value")!;

        var script = db.Database.GenerateCreateScript();
        var line = script.Split('\n').Single(l => l.TrimStart().StartsWith("\"Value\" ", StringComparison.Ordinal)).Trim();
        var scripted = line["\"Value\" ".Length..].Split(" NOT NULL")[0].TrimEnd(',');

        return (property.GetRelationalTypeMapping().StoreType, scripted);
    }
}

public enum Facet
{
    Precision18And2,
    Citext,
    TimestampWithoutTimeZone,
}

/// <summary>Where the application configures a facet: on the entity's property, or for every property of the type.</summary>
public enum FacetSite
{
    Property,
    Conventions,
}

public class Faceted<TValue>
{
    public int Id { get; set; }

    public TValue Value { get; set; } = default!;
}

/// <summary>A model of one property over <see cref="Value"/> with one facet, cached per type, facet and site.</summary>
public sealed class FacetDb(DbContextOptions<FacetDb> options, Type value, Facet facet, FacetSite site) : DbContext(options)
{
    public Type Value => value;

    public Facet Facet => facet;

    public FacetSite Site => site;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (site is not FacetSite.Conventions) return;

        var properties = configurationBuilder.Properties(value);
        _ = facet switch
        {
            Facet.Precision18And2          => properties.HavePrecision(18, 2),
            Facet.Citext                   => properties.HaveColumnType("citext"),
            Facet.TimestampWithoutTimeZone => properties.HaveColumnType("timestamp without time zone"),
            _                              => throw new ArgumentOutOfRangeException(nameof(facet), facet, null),
        };
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var property = modelBuilder.Entity(typeof(Faceted<>).MakeGenericType(value)).Property("Value");
        if (site is not FacetSite.Property) return;

        _ = facet switch
        {
            Facet.Precision18And2          => property.HasPrecision(18, 2),
            Facet.Citext                   => property.HasColumnType("citext"),
            Facet.TimestampWithoutTimeZone => property.HasColumnType("timestamp without time zone"),
            _                              => throw new ArgumentOutOfRangeException(nameof(facet), facet, null),
        };
    }

    /// <summary>On Npgsql without a server, as <see cref="TestDb.NpgsqlWithoutServer"/>: the model and the script never open the connection.</summary>
    public static FacetDb Create(Type value, Facet facet, FacetSite site, bool typeKit)
    {
        var builder = new DbContextOptionsBuilder<FacetDb>().ReplaceService<IModelCacheKeyFactory, FacetModelCacheKey>().UseNpgsql("Host=localhost;Database=none");
        if (typeKit) builder = builder.UseTypeKit();

        return new FacetDb(builder.Options, value, facet, site);
    }
}

public sealed class FacetModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is FacetDb db ? (db.GetType(), db.Value, db.Facet, db.Site, designTime) : (context.GetType(), designTime);
}
