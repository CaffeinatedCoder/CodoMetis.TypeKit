using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;

/// <summary>Where a key sits in a model: the shapes EF decides a key's generation again for.</summary>
public enum KeyShape
{
    /// <summary>An entity type of its own.</summary>
    Alone,

    /// <summary>The root of a hierarchy with a derived type, under each mapping strategy; TPH is EF's default.</summary>
    TphRoot,
    TptRoot,
    TpcRoot,

    /// <summary>An abstract root with two derived types, mapped to no table of its own under TPC.</summary>
    AbstractTphRoot,
    AbstractTptRoot,
    AbstractTpcRoot,

    /// <summary>A one-to-one dependent whose key is the foreign key to its principal's.</summary>
    SharedKeyDependent,

    /// <summary>A one-to-one dependent sharing its principal's table.</summary>
    TableSplitDependent,

    /// <summary>An owned type, whose key is its owner's.</summary>
    OwnedDependent,

    /// <summary>A key of two properties of the kind.</summary>
    Composite,

    /// <summary>The application's own configuration, which wins over every default.</summary>
    ConfiguredNever,
    ConfiguredOnAdd,
}

/// <summary>
/// The key-generation catalogue (chain H of design/fix-history.md): every <see cref="KeyShape"/> keyed by
/// every kind of <see cref="WrappedKinds.Keys"/>, on SQLite, Npgsql and SQL Server. Each is built twice,
/// keyed by the value object under <c>UseTypeKit()</c> and keyed by the wrapped type under plain EF.
/// </summary>
public static class KeyShapes
{
    public static readonly string[] Providers = ["sqlite", "npgsql", "sqlserver"];

    public static IEnumerable<(string Provider, KeyShape Shape, WrappedKind Kind)> All() =>
        from provider in Providers
        from shape in Enum.GetValues<KeyShape>()
        from kind in WrappedKinds.Keys
        select (provider, shape, kind);

    /// <summary>The entity types whose key the shape is about, closed over <paramref name="key"/>.</summary>
    public static Type Subject(KeyShape shape, Type key) => shape switch
    {
        KeyShape.Alone or KeyShape.ConfiguredNever or KeyShape.ConfiguredOnAdd               => typeof(Alone<>).MakeGenericType(key),
        KeyShape.TphRoot or KeyShape.TptRoot or KeyShape.TpcRoot                             => typeof(Root<>).MakeGenericType(key),
        KeyShape.AbstractTphRoot or KeyShape.AbstractTptRoot or KeyShape.AbstractTpcRoot     => typeof(AbstractRoot<>).MakeGenericType(key),
        KeyShape.SharedKeyDependent or KeyShape.TableSplitDependent                          => typeof(Dependent<>).MakeGenericType(key),
        KeyShape.OwnedDependent                                                              => typeof(OwnedNote),
        KeyShape.Composite                                                                   => typeof(Composite<>).MakeGenericType(key),
        _                                                                                    => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    /// <summary>The shape's model, keyed by <paramref name="key"/>.</summary>
    public static void Configure(ModelBuilder model, KeyShape shape, Type key)
    {
        Type Closed(Type definition) => definition.MakeGenericType(key);

        switch (shape)
        {
            case KeyShape.Alone:
                model.Entity(Closed(typeof(Alone<>)));
                break;
            case KeyShape.ConfiguredNever:
                model.Entity(Closed(typeof(Alone<>))).Property("Id").ValueGeneratedNever();
                break;
            case KeyShape.ConfiguredOnAdd:
                model.Entity(Closed(typeof(Alone<>))).Property("Id").ValueGeneratedOnAdd();
                break;
            case KeyShape.TphRoot or KeyShape.TptRoot or KeyShape.TpcRoot:
                Strategy(model.Entity(Closed(typeof(Root<>))), shape);
                model.Entity(Closed(typeof(Leaf<>)));
                break;
            case KeyShape.AbstractTphRoot or KeyShape.AbstractTptRoot or KeyShape.AbstractTpcRoot:
                Strategy(model.Entity(Closed(typeof(AbstractRoot<>))), shape);
                model.Entity(Closed(typeof(LeafA<>)));
                model.Entity(Closed(typeof(LeafB<>)));
                break;
            case KeyShape.SharedKeyDependent:
                model.Entity(Closed(typeof(Principal<>))).HasOne(Closed(typeof(Dependent<>))).WithOne().HasForeignKey(Closed(typeof(Dependent<>)), "Id");
                break;
            case KeyShape.TableSplitDependent:
                model.Entity(Closed(typeof(Principal<>))).ToTable("Split").HasOne(Closed(typeof(Dependent<>))).WithOne().HasForeignKey(Closed(typeof(Dependent<>)), "Id");
                model.Entity(Closed(typeof(Dependent<>))).ToTable("Split");
                break;
            case KeyShape.OwnedDependent:
                model.Entity(Closed(typeof(Owner<>))).OwnsOne(typeof(OwnedNote), "Part");
                break;
            case KeyShape.Composite:
                model.Entity(Closed(typeof(Composite<>))).HasKey("Id", "Line");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    private static void Strategy(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder root, KeyShape shape)
    {
        if (shape is KeyShape.TptRoot or KeyShape.AbstractTptRoot) root.UseTptMappingStrategy();
        if (shape is KeyShape.TpcRoot or KeyShape.AbstractTpcRoot) root.UseTpcMappingStrategy();
    }

    /// <summary>The finished model's key properties of the shape's subject.</summary>
    public static IReadOnlyList<IProperty> KeyOf(DbContext db, KeyShape shape, Type key) =>
        db.GetService<IDesignTimeModel>().Model.FindEntityType(Subject(shape, key))!.FindPrimaryKey()!.Properties;
}

/// <summary>A model of one key shape, cached per shape, key type and side.</summary>
public sealed class KeyShapeDb(DbContextOptions<KeyShapeDb> options, KeyShape shape, Type key) : DbContext(options)
{
    public KeyShape Shape => shape;

    public Type Key => key;

    protected override void OnModelCreating(ModelBuilder modelBuilder) => KeyShapes.Configure(modelBuilder, shape, key);

    public static KeyShapeDb Create(string provider, KeyShape shape, Type key, bool typeKit, Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        var builder = new DbContextOptionsBuilder<KeyShapeDb>().ReplaceService<IModelCacheKeyFactory, KeyShapeModelCacheKey>();
        builder = provider switch
        {
            "sqlite"    => builder.UseSqlite(connection),
            "npgsql"    => builder.UseNpgsql("Host=localhost;Database=none"),
            "sqlserver" => builder.UseSqlServer("Server=localhost;Database=none"),
            _           => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
        if (typeKit) builder = builder.UseTypeKit();

        return new KeyShapeDb(builder.Options, shape, key);
    }
}

public sealed class KeyShapeModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is KeyShapeDb db ? (db.GetType(), db.Shape, db.Key, designTime) : (context.GetType(), designTime);
}

public class Alone<TKey>
{
    public TKey Id { get; set; } = default!;

    public string Name { get; set; } = "";
}

public class Root<TKey>
{
    public TKey Id { get; set; } = default!;

    public string Name { get; set; } = "";
}

public class Leaf<TKey> : Root<TKey>
{
    public string Extra { get; set; } = "";
}

public abstract class AbstractRoot<TKey>
{
    public TKey Id { get; set; } = default!;

    public string Name { get; set; } = "";
}

public class LeafA<TKey> : AbstractRoot<TKey>
{
    public int Doors { get; set; }
}

public class LeafB<TKey> : AbstractRoot<TKey>
{
    public int Gears { get; set; }
}

public class Principal<TKey>
{
    public TKey Id { get; set; } = default!;

    public string Name { get; set; } = "";
}

public class Dependent<TKey>
{
    public TKey Id { get; set; } = default!;

    public string Note { get; set; } = "";
}

public class Owner<TKey>
{
    public TKey Id { get; set; } = default!;

    public OwnedNote Part { get; set; } = new();
}

public class OwnedNote
{
    public string Note { get; set; } = "";
}

public class Composite<TKey>
{
    public TKey Id { get; set; } = default!;

    public TKey Line { get; set; } = default!;
}
