using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A value-object key over an integer is generated as a key of that integer is, in every mapping of an
/// inheritance hierarchy, compared with the same hierarchy keyed by a plain <c>int</c>.
/// </summary>
/// <remarks>
/// EF decides a hierarchy root's key generation again when it sets the root's mapping strategy, in a
/// convention that runs while the model is finalized, and SQLite's provider decides from the CLR type:
/// the root of a TPH hierarchy keyed by a value object went back to never generated, and the second
/// entity added threw for a duplicate key, while PostgreSQL kept its identity column.
/// </remarks>
public sealed class KeyGenerationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public static TheoryData<string, Type, Type> Hierarchies => new()
    {
        { "sqlite", typeof(TphByValueObject), typeof(TphByInt) },
        { "sqlite", typeof(TptByValueObject), typeof(TptByInt) },
        { "sqlite", typeof(TpcByValueObject), typeof(TpcByInt) },
        { "npgsql", typeof(TphByValueObject), typeof(TphByInt) },
        { "npgsql", typeof(TptByValueObject), typeof(TptByInt) },
        { "npgsql", typeof(TpcByValueObject), typeof(TpcByInt) },
    };

    /// <summary>
    /// The same decision as for <c>int</c>: generated under TPH and TPT on both providers, and under TPC
    /// only on PostgreSQL, from a sequence, since SQLite has none and each table would number its own rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(Hierarchies))]
    public void A_hierarchy_root_s_key_is_generated_as_an_int_key_is(string provider, Type keyedByValueObject, Type keyedByInt)
    {
        using var db = Create(provider);

        var valueObjectKey = Key(db, keyedByValueObject);
        var intKey = Key(db, keyedByInt);

        valueObjectKey.ValueGenerated.ShouldBe(intKey.ValueGenerated, $"{keyedByValueObject.Name} on {provider}");
        valueObjectKey.GetDefaultValueSql()?.Replace(keyedByValueObject.Name, "Root", StringComparison.Ordinal)
                      .ShouldBe(intKey.GetDefaultValueSql()?.Replace(keyedByInt.Name, "Root", StringComparison.Ordinal), $"{keyedByValueObject.Name} on {provider}");
    }

    /// <summary>What the review reproduced, on a database: a base entity and a derived one, each given its own key.</summary>
    [Fact]
    public void On_SQLite_a_TPH_root_s_key_is_generated_by_the_database()
    {
        _connection.Open();
        using var db = Create("sqlite");
        db.Database.EnsureCreated();

        var root    = new TphByValueObject { Name = "a" };
        var derived = new TphByValueObjectDerived { Name = "b", Extra = "x" };
        db.AddRange(root, derived);
        db.SaveChanges();

        root.Id.Value.ShouldBeGreaterThan(0);
        derived.Id.Value.ShouldBeGreaterThan(0);
        derived.Id.ShouldNotBe(root.Id);
    }

    [Fact]
    public void On_SQLite_a_TPT_root_s_key_is_generated_by_the_database()
    {
        _connection.Open();
        using var db = Create("sqlite");
        db.Database.EnsureCreated();

        var root    = new TptByValueObject { Name = "a" };
        var derived = new TptByValueObjectDerived { Name = "b", Extra = "x" };
        db.AddRange(root, derived);
        db.SaveChanges();

        root.Id.Value.ShouldBeGreaterThan(0);
        derived.Id.ShouldNotBe(root.Id);
    }

    public void Dispose() => _connection.Dispose();

    private HierarchyKeysDb Create(string provider) => new(provider switch
    {
        "sqlite" => new DbContextOptionsBuilder<HierarchyKeysDb>().UseSqlite(_connection).UseTypeKit().Options,
        _        => new DbContextOptionsBuilder<HierarchyKeysDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options,
    });

    private static IProperty Key(DbContext db, Type root) => db.Model.FindEntityType(root)!.FindPrimaryKey()!.Properties.Single();
}

public class TphByValueObject
{
    public ShipmentNo Id { get; set; }

    public string Name { get; set; } = "";
}

public class TphByValueObjectDerived : TphByValueObject
{
    public string Extra { get; set; } = "";
}

public class TphByInt
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public class TphByIntDerived : TphByInt
{
    public string Extra { get; set; } = "";
}

public class TptByValueObject
{
    public ShipmentNo Id { get; set; }

    public string Name { get; set; } = "";
}

public class TptByValueObjectDerived : TptByValueObject
{
    public string Extra { get; set; } = "";
}

public class TptByInt
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public class TptByIntDerived : TptByInt
{
    public string Extra { get; set; } = "";
}

public class TpcByValueObject
{
    public ShipmentNo Id { get; set; }

    public string Name { get; set; } = "";
}

public class TpcByValueObjectDerived : TpcByValueObject
{
    public string Extra { get; set; } = "";
}

public class TpcByInt
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public class TpcByIntDerived : TpcByInt
{
    public string Extra { get; set; } = "";
}

/// <summary>Each mapping strategy twice, keyed by a value object over <c>int</c> and by <c>int</c>; TPH is EF's default.</summary>
public sealed class HierarchyKeysDb(DbContextOptions<HierarchyKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TphByValueObject>();
        modelBuilder.Entity<TphByValueObjectDerived>();
        modelBuilder.Entity<TphByInt>();
        modelBuilder.Entity<TphByIntDerived>();
        modelBuilder.Entity<TptByValueObject>().UseTptMappingStrategy();
        modelBuilder.Entity<TptByValueObjectDerived>();
        modelBuilder.Entity<TptByInt>().UseTptMappingStrategy();
        modelBuilder.Entity<TptByIntDerived>();
        modelBuilder.Entity<TpcByValueObject>().UseTpcMappingStrategy();
        modelBuilder.Entity<TpcByValueObjectDerived>();
        modelBuilder.Entity<TpcByInt>().UseTpcMappingStrategy();
        modelBuilder.Entity<TpcByIntDerived>();
    }
}
