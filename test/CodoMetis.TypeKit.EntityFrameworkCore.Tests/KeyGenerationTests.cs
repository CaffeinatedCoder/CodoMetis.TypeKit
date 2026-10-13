using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A value-object key over an integer is generated as a key of that integer is, in every mapping of an
/// inheritance hierarchy, compared with the same hierarchy keyed by a plain <c>int</c>, in the models
/// of SQLite, Npgsql and SQL Server.
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

    /// <summary>The providers a model is built for. SQL Server's is built without a server, for the model alone.</summary>
    private static readonly string[] Providers = ["sqlite", "npgsql", "sqlserver"];

    public static TheoryData<string, Type, Type> Hierarchies
    {
        get
        {
            var hierarchies = new TheoryData<string, Type, Type>();
            foreach (var provider in Providers)
            {
                hierarchies.Add(provider, typeof(TphByValueObject), typeof(TphByInt));
                hierarchies.Add(provider, typeof(TptByValueObject), typeof(TptByInt));
                hierarchies.Add(provider, typeof(TpcByValueObject), typeof(TpcByInt));
                hierarchies.Add(provider, typeof(AbstractTpcByValueObject), typeof(AbstractTpcByLong));
            }
            return hierarchies;
        }
    }

    /// <summary>
    /// The same decision as for the wrapped type: generated under TPH and TPT on every provider, and under
    /// TPC only on PostgreSQL and SQL Server, from a sequence, since SQLite has none and each table would
    /// number its own rows. An abstract TPC root included, whose key is mapped only to its derived types' tables.
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

    /// <summary>
    /// An abstract root has no table of its own, only its derived types' tables, and the provider's
    /// sequence is the key's default in each of them: the convention looked for the root's own table,
    /// found none, and took the generation away, so on PostgreSQL every entity was stored with 0.
    /// </summary>
    [Theory]
    [InlineData("npgsql", """nextval('"AbstractTpcByValueObjectSequence"')""")]
    [InlineData("sqlserver", "NEXT VALUE FOR [AbstractTpcByValueObjectSequence]")]
    public void An_abstract_TPC_root_s_key_is_generated_from_its_sequence(string provider, string sequenceDefault)
    {
        using var db = Create(provider);

        var key = Key(db, typeof(AbstractTpcByValueObject));

        key.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);
        key.GetDefaultValueSql().ShouldBe(sequenceDefault);
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

    public static TheoryData<string, Type, Type> IntegerKeys
    {
        get
        {
            var keys = new TheoryData<string, Type, Type>();
            foreach (var provider in Providers)
                foreach (var (byValueObject, byWrapped) in IntegerKeysDb.Pairs)
                    keys.Add(provider, byValueObject, byWrapped);
            return keys;
        }
    }

    /// <summary>
    /// EF generates a key of every integer type but <c>byte</c>, unsigned ones and <c>char</c> included
    /// (<c>ValueGenerationConvention</c>), and a key over one follows it. Those over <c>uint</c>,
    /// <c>ulong</c>, <c>ushort</c>, <c>sbyte</c> and <c>char</c> were never generated.
    /// </summary>
    [Theory]
    [MemberData(nameof(IntegerKeys))]
    public void A_key_over_an_integer_type_is_generated_as_a_key_of_that_type_is(string provider, Type keyedByValueObject, Type keyedByWrapped)
    {
        using var db = new IntegerKeysDb(Options<IntegerKeysDb>(provider));

        Key(db, keyedByValueObject).ValueGenerated.ShouldBe(Key(db, keyedByWrapped).ValueGenerated, $"{keyedByValueObject.Name} on {provider}");
    }

    /// <summary>
    /// Each integer type SQLite fills a key of, added twice. Not <c>char</c>: EF's own <c>char</c> key asks
    /// SQLite for a <c>TEXT</c> column with <c>AUTOINCREMENT</c>, which it refuses.
    /// </summary>
    [Fact]
    public void On_SQLite_a_key_over_each_integer_type_is_generated_by_the_database()
    {
        _connection.Open();
        using var db = new SqliteIntegerKeysDb(Options<SqliteIntegerKeysDb>("sqlite"));
        db.Database.EnsureCreated();

        KeyedByInt16[]  shorts  = [new(), new()];
        KeyedByInt64[]  longs   = [new(), new()];
        KeyedBySByte[]  sbytes  = [new(), new()];
        KeyedByUInt16[] ushorts = [new(), new()];
        KeyedByUInt32[] uints   = [new(), new()];
        KeyedByUInt64[] ulongs  = [new(), new()];
        db.AddRange([.. shorts, .. longs, .. sbytes, .. ushorts, .. uints, .. ulongs]);
        db.SaveChanges();

        shorts.Select(e => e.Id.Value).ShouldBe([(short)1, (short)2]);
        longs.Select(e => e.Id.Value).ShouldBe([1L, 2L]);
        sbytes.Select(e => e.Id.Value).ShouldBe([(sbyte)1, (sbyte)2]);
        ushorts.Select(e => e.Id.Value).ShouldBe([(ushort)1, (ushort)2]);
        uints.Select(e => e.Id.Value).ShouldBe([1u, 2u]);
        ulongs.Select(e => e.Id.Value).ShouldBe([1ul, 2ul]);
    }

    public static TheoryData<string, Type, Type> ForeignKeyKeys
    {
        get
        {
            var keys = new TheoryData<string, Type, Type>();
            foreach (var provider in Providers)
                foreach (var (byValueObject, byWrapped) in ForeignKeyKeysDb.Pairs)
                    keys.Add(provider, byValueObject, byWrapped);
            return keys;
        }
    }

    /// <summary>
    /// A key that is also a foreign key takes its value from the principal, so EF never generates it,
    /// and a key over an integer is no exception: the dependent of a one-to-one sharing its principal's
    /// key, one splitting its principal's table, and an owned type, whose key is its owner's.
    /// </summary>
    /// <remarks>
    /// Without the convention's foreign-key check each of them was generated on add on SQLite (measured);
    /// Npgsql and SQL Server reset it themselves when they choose the key's value-generation strategy.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ForeignKeyKeys))]
    public void A_key_that_is_also_a_foreign_key_is_never_generated(string provider, Type dependentByValueObject, Type dependentByWrapped)
    {
        using var db = new ForeignKeyKeysDb(Options<ForeignKeyKeysDb>(provider));

        var valueObjectKey = Key(db, dependentByValueObject);

        valueObjectKey.IsForeignKey().ShouldBeTrue();
        valueObjectKey.ValueGenerated.ShouldBe(Key(db, dependentByWrapped).ValueGenerated, $"{dependentByValueObject.Name} on {provider}");
        valueObjectKey.ValueGenerated.ShouldBe(ValueGenerated.Never, $"{dependentByValueObject.Name} on {provider}");
    }

    public void Dispose() => _connection.Dispose();

    private HierarchyKeysDb Create(string provider) => new(Options<HierarchyKeysDb>(provider));

    private DbContextOptions<TDb> Options<TDb>(string provider) where TDb : DbContext => provider switch
    {
        "sqlite"    => new DbContextOptionsBuilder<TDb>().UseSqlite(_connection).UseTypeKit().Options,
        "npgsql"    => new DbContextOptionsBuilder<TDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options,
        "sqlserver" => new DbContextOptionsBuilder<TDb>().UseSqlServer("Server=localhost;Database=none").UseTypeKit().Options,
        _           => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

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

/// <summary>A TPC root that is abstract, so mapped to no table of its own: the review's shape, keyed over <c>long</c>.</summary>
public abstract class AbstractTpcByValueObject
{
    public Int64Key Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class AbstractTpcByValueObjectCar : AbstractTpcByValueObject
{
    public int Doors { get; set; }
}

public sealed class AbstractTpcByValueObjectBike : AbstractTpcByValueObject
{
    public int Gears { get; set; }
}

public abstract class AbstractTpcByLong
{
    public long Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class AbstractTpcByLongCar : AbstractTpcByLong
{
    public int Doors { get; set; }
}

public sealed class AbstractTpcByLongBike : AbstractTpcByLong
{
    public int Gears { get; set; }
}

/// <summary>The abstract TPC root alone, for a database.</summary>
public sealed class AbstractTpcDb(DbContextOptions<AbstractTpcDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AbstractTpcByValueObject>().UseTpcMappingStrategy();
        modelBuilder.Entity<AbstractTpcByValueObjectCar>();
        modelBuilder.Entity<AbstractTpcByValueObjectBike>();
    }
}

/// <summary>
/// Each mapping strategy twice, keyed by a value object over <c>int</c> and by <c>int</c>; TPH is EF's
/// default. And an abstract TPC root twice, over <c>long</c>.
/// </summary>
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
        modelBuilder.Entity<AbstractTpcByValueObject>().UseTpcMappingStrategy();
        modelBuilder.Entity<AbstractTpcByValueObjectCar>();
        modelBuilder.Entity<AbstractTpcByValueObjectBike>();
        modelBuilder.Entity<AbstractTpcByLong>().UseTpcMappingStrategy();
        modelBuilder.Entity<AbstractTpcByLongCar>();
        modelBuilder.Entity<AbstractTpcByLongBike>();
    }
}

public readonly partial record struct Int16Key : IValue<short>;

public readonly partial record struct Int64Key : IValue<long>;

public readonly partial record struct SByteKey : IValue<sbyte>;

public readonly partial record struct UInt16Key : IValue<ushort>;

public readonly partial record struct UInt32Key : IValue<uint>;

public readonly partial record struct UInt64Key : IValue<ulong>;

public readonly partial record struct ByteKey : IValue<byte>;

public readonly partial record struct CharKey : IValue<char>;

public class KeyedByInt16 { public Int16Key Id { get; set; } }

public class KeyedByShort { public short Id { get; set; } }

public class KeyedByInt64 { public Int64Key Id { get; set; } }

public class KeyedByLong { public long Id { get; set; } }

public class KeyedBySByte { public SByteKey Id { get; set; } }

public class KeyedBySbyte { public sbyte Id { get; set; } }

public class KeyedByUInt16 { public UInt16Key Id { get; set; } }

public class KeyedByUshort { public ushort Id { get; set; } }

public class KeyedByUInt32 { public UInt32Key Id { get; set; } }

public class KeyedByUint { public uint Id { get; set; } }

public class KeyedByUInt64 { public UInt64Key Id { get; set; } }

public class KeyedByUlong { public ulong Id { get; set; } }

public class KeyedByByte { public ByteKey Id { get; set; } }

public class KeyedByPlainByte { public byte Id { get; set; } }

public class KeyedByChar { public CharKey Id { get; set; } }

public class KeyedByPlainChar { public char Id { get; set; } }

/// <summary>A key over each integer type, beside a key of that type itself. For the model only: SQLite refuses EF's own <c>char</c> key.</summary>
public sealed class IntegerKeysDb(DbContextOptions<IntegerKeysDb> options) : DbContext(options)
{
    public static readonly (Type ByValueObject, Type ByWrapped)[] Pairs =
    [
        (typeof(KeyedByInt16), typeof(KeyedByShort)),
        (typeof(KeyedByInt64), typeof(KeyedByLong)),
        (typeof(KeyedBySByte), typeof(KeyedBySbyte)),
        (typeof(KeyedByUInt16), typeof(KeyedByUshort)),
        (typeof(KeyedByUInt32), typeof(KeyedByUint)),
        (typeof(KeyedByUInt64), typeof(KeyedByUlong)),
        (typeof(KeyedByByte), typeof(KeyedByPlainByte)),
        (typeof(KeyedByChar), typeof(KeyedByPlainChar)),
    ];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var (byValueObject, byWrapped) in Pairs)
        {
            modelBuilder.Entity(byValueObject);
            modelBuilder.Entity(byWrapped);
        }
    }
}

/// <summary>The keys over the integer types SQLite fills in.</summary>
public sealed class SqliteIntegerKeysDb(DbContextOptions<SqliteIntegerKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyedByInt16>();
        modelBuilder.Entity<KeyedByInt64>();
        modelBuilder.Entity<KeyedBySByte>();
        modelBuilder.Entity<KeyedByUInt16>();
        modelBuilder.Entity<KeyedByUInt32>();
        modelBuilder.Entity<KeyedByUInt64>();
    }
}

public class SharedKeyPrincipal { public ShipmentNo Id { get; set; } }

public class SharedKeyDependent { public ShipmentNo Id { get; set; } }

public class SharedKeyPrincipalInt { public int Id { get; set; } }

public class SharedKeyDependentInt { public int Id { get; set; } }

public class SplitHead { public ShipmentNo Id { get; set; } }

public class SplitBody { public ShipmentNo Id { get; set; } }

public class SplitHeadInt { public int Id { get; set; } }

public class SplitBodyInt { public int Id { get; set; } }

public class Owner { public ShipmentNo Id { get; set; } public OwnedPart Part { get; set; } = new(); }

public class OwnedPart { public string Note { get; set; } = ""; }

public class OwnerInt { public int Id { get; set; } public OwnedPartInt Part { get; set; } = new(); }

public class OwnedPartInt { public string Note { get; set; } = ""; }

/// <summary>
/// Three dependents whose key is a foreign key to their principal's, each beside the same shape keyed
/// by <c>int</c>: a one-to-one sharing the key, a one-to-one splitting the table, and an owned type.
/// </summary>
public sealed class ForeignKeyKeysDb(DbContextOptions<ForeignKeyKeysDb> options) : DbContext(options)
{
    public static readonly (Type ByValueObject, Type ByWrapped)[] Pairs =
    [
        (typeof(SharedKeyDependent), typeof(SharedKeyDependentInt)),
        (typeof(SplitBody), typeof(SplitBodyInt)),
        (typeof(OwnedPart), typeof(OwnedPartInt)),
    ];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SharedKeyPrincipal>().HasOne<SharedKeyDependent>().WithOne().HasForeignKey<SharedKeyDependent>(d => d.Id);
        modelBuilder.Entity<SharedKeyPrincipalInt>().HasOne<SharedKeyDependentInt>().WithOne().HasForeignKey<SharedKeyDependentInt>(d => d.Id);

        modelBuilder.Entity<SplitHead>().ToTable("Split").HasOne<SplitBody>().WithOne().HasForeignKey<SplitBody>(b => b.Id);
        modelBuilder.Entity<SplitBody>().ToTable("Split");
        modelBuilder.Entity<SplitHeadInt>().ToTable("SplitInt").HasOne<SplitBodyInt>().WithOne().HasForeignKey<SplitBodyInt>(b => b.Id);
        modelBuilder.Entity<SplitBodyInt>().ToTable("SplitInt");

        modelBuilder.Entity<Owner>().OwnsOne(o => o.Part);
        modelBuilder.Entity<OwnerInt>().OwnsOne(o => o.Part);
    }
}
