using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    /// EF generates a key of every integer type but <c>byte</c>, unsigned ones included
    /// (<c>ValueGenerationConvention</c>), and a key over one follows it. Those over <c>uint</c>,
    /// <c>ulong</c>, <c>ushort</c> and <c>sbyte</c> were never generated. <c>char</c>, which EF counts
    /// as an integer too, is the application's to assign (<see cref="A_key_over_char_is_the_application_s_to_assign"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(IntegerKeys))]
    public void A_key_over_an_integer_type_is_generated_as_a_key_of_that_type_is(string provider, Type keyedByValueObject, Type keyedByWrapped)
    {
        using var db = new IntegerKeysDb(Options<IntegerKeysDb>(provider));

        Key(db, keyedByValueObject).ValueGenerated.ShouldBe(Key(db, keyedByWrapped).ValueGenerated, $"{keyedByValueObject.Name} on {provider}");
    }

    public static TheoryData<string> AllProviders => [.. Providers];

    /// <summary>
    /// No provider generates a <c>char</c> key: PostgreSQL refuses an identity on <c>character(1)</c>, and
    /// a plain one on SQL Server or SQLite reaches the insert without a value. Marked generated, the value
    /// object's key on SQL Server got a character of a GUID string at <c>Add</c>, which was then stored
    /// (EF picks the generator by the column's type, <c>nvarchar</c>). So it is not generated: the key the
    /// application set is the key added, a default one included, as in 1.5.1.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public void A_key_over_char_is_the_application_s_to_assign(string provider)
    {
        using var db = new CharAndULongKeysDb(Options<CharAndULongKeysDb>(provider));

        var unassigned = new KeyedByChar();
        var assigned = new KeyedByChar { Id = CharKey.From('B') };
        db.AddRange(unassigned, assigned);

        // The entry's value, which EF inserts; a temporary one is kept there, not on the entity.
        var added = db.Entry(unassigned).Property(e => e.Id);
        added.CurrentValue.Value.ShouldBe('\0', $"on {provider}, Add invented a key");
        added.IsTemporary.ShouldBeFalse($"on {provider}");
        unassigned.Id.Value.ShouldBe('\0', $"on {provider}, Add invented a key");
        assigned.Id.Value.ShouldBe('B');
        Key(db, typeof(KeyedByChar)).ValueGenerated.ShouldBe(ValueGenerated.Never, $"on {provider}");
    }

    /// <summary>The application's own configuration of a <c>char</c> key still wins: generated on add, or its own generator.</summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public void A_char_key_the_application_configures_keeps_its_configuration(string provider)
    {
        using var db = new CharAndULongKeysDb(Options<CharAndULongKeysDb>(provider));

        Key(db, typeof(GeneratedOnAddCharKey)).ValueGenerated.ShouldBe(ValueGenerated.OnAdd, $"on {provider}");

        var own = new OwnGeneratorCharKey();
        db.Add(own);
        own.Id.Value.ShouldBe('A', $"on {provider}");
    }

    /// <summary>
    /// A default key over <c>ulong</c> gets a temporary <c>ulong</c> at <c>Add</c>, as a plain key does, also where the
    /// provider stores it as a <c>decimal</c> (SQL Server, PostgreSQL). EF chooses a temporary number by the property's
    /// type mapping, which for a value object is its own, so it fell back to the column's type, and the negative
    /// temporary <c>decimal</c> threw <see cref="OverflowException"/> converted back (decision 63, now 114).
    /// Configured <c>ValueGeneratedNever()</c>, the key the application assigns is added.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public void A_default_key_over_ulong_gets_a_temporary_value_at_Add_as_a_plain_one_does(string provider)
    {
        using var db = new CharAndULongKeysDb(Options<CharAndULongKeysDb>(provider));

        KeyedByUInt64[] keyed = [new(), new()];
        KeyedByUlong[] plain  = [new(), new()];
        db.AddRange([.. keyed, .. plain]);

        keyed.Select(e => db.Entry(e).Property(p => p.Id).IsTemporary)
             .ShouldBe(plain.Select(e => db.Entry(e).Property(p => p.Id).IsTemporary), $"on {provider}");
        keyed.Select(e => db.Entry(e).Property(p => p.Id).CurrentValue.Value)
             .ShouldBe(plain.Select(e => db.Entry(e).Property(p => p.Id).CurrentValue), $"on {provider}");

        var assigned = new AssignedUInt64Key { Id = UInt64Key.From(42) };
        db.Add(assigned);

        assigned.Id.Value.ShouldBe(42ul);
        db.Entry(assigned).Property(e => e.Id).IsTemporary.ShouldBeFalse();
        Key(db, typeof(AssignedUInt64Key)).ValueGenerated.ShouldBe(ValueGenerated.Never);
    }

    public static TheoryData<string> DecimalColumnProviders => ["sqlserver", "npgsql"];

    /// <summary>
    /// SQL Server's HiLo still comes before a temporary value, for a <c>ulong</c> key as for a plain one, and hands out
    /// values that are not temporary. A generator configured on the property would have come before HiLo, so the
    /// temporary value is the type mapping's (decision 114). No database is opened: this is the generator EF chooses,
    /// not its values. PostgreSQL's provider refuses HiLo on a <c>ulong</c> key, plain or value object, when it validates the model.
    /// </summary>
    [Fact]
    public void SQL_Server_s_HiLo_still_comes_first_for_a_key_over_ulong()
    {
        using var db = new ULongHiLoKeysDb(Options<ULongHiLoKeysDb>("sqlserver"));
        var selector = db.GetService<Microsoft.EntityFrameworkCore.ValueGeneration.IValueGeneratorSelector>();

        bool Temporary(Type entity)
        {
            var entityType = db.Model.FindEntityType(entity)!;
            selector.TrySelect(entityType.FindPrimaryKey()!.Properties.Single(), entityType, out var generator).ShouldBeTrue(entity.Name);
            return generator!.GeneratesTemporaryValues;
        }

        Temporary(typeof(HiLoUlongKey)).ShouldBeFalse();
        Temporary(typeof(HiLoUInt64Key)).ShouldBeFalse("HiLo was not chosen");
    }

    /// <summary>A generator the application configures on a <c>ulong</c> key still comes before a temporary value.</summary>
    [Theory]
    [MemberData(nameof(DecimalColumnProviders))]
    public void The_application_s_own_generator_still_comes_first_for_a_key_over_ulong(string provider)
    {
        using var db = new ULongHiLoKeysDb(Options<ULongHiLoKeysDb>(provider));

        var own = new OwnGeneratorUInt64Key();
        db.Add(own);

        own.Id.Value.ShouldBe(7ul, $"on {provider}");
    }

    /// <summary>
    /// A compiled model gives a default <c>ulong</c> key the same temporary value: <c>dbcontext optimize</c> leaves the
    /// type mappings to be found when the model is first used, where <c>UseTypeKit()</c> composes them.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecimalColumnProviders))]
    public void In_a_compiled_model_a_default_key_over_ulong_gets_a_temporary_value(string provider)
    {
        using var db = Compiled(provider, nativeAot: false);

        KeyedByUInt64[] keyed = [new(), new()];
        KeyedByUlong[] plain  = [new(), new()];
        db.AddRange([.. keyed, .. plain]);

        keyed.Select(e => db.Entry(e).Property(p => p.Id).IsTemporary).ShouldBe([true, true], $"on {provider}");
        keyed.Select(e => db.Entry(e).Property(p => p.Id).CurrentValue.Value)
             .ShouldBe(plain.Select(e => db.Entry(e).Property(p => p.Id).CurrentValue), $"on {provider}");
    }

    /// <summary>
    /// Pinned as documented, not as wanted: <c>--nativeaot</c> writes each type mapping out as code, and the factory a
    /// value object's mapping carries is a delegate it cannot write, so a default <c>ulong</c> key still throws at
    /// <c>Add</c> where the column is a <c>decimal</c> (decision 114). <c>ValueGeneratedNever()</c> is the workaround.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecimalColumnProviders))]
    public void In_a_Native_AOT_compiled_model_a_default_key_over_ulong_still_throws_at_Add(string provider)
    {
        using var db = Compiled(provider, nativeAot: true);

        Should.Throw<OverflowException>(() => db.Add(new KeyedByUInt64()));
    }

    /// <summary>
    /// Pinned as documented, not as wanted: a key configured with <see cref="ValueObjectConverter{TValueObject,T}"/> by hand
    /// is mapped on that converter, which carries no hints, so a default <c>ulong</c> key still throws at <c>Add</c> where
    /// the column is a <c>decimal</c> (decision 114).
    /// </summary>
    [Theory]
    [MemberData(nameof(DecimalColumnProviders))]
    public void A_default_key_over_ulong_with_the_converter_named_by_hand_still_throws_at_Add(string provider)
    {
        using var db = new NamedConverterULongKeyDb(Options<NamedConverterULongKeyDb>(provider));

        Should.Throw<OverflowException>(() => db.Add(new KeyedByUInt64()));
    }

    /// <summary>
    /// A context on the compiled model, with an internal service provider of its own: EF caches a property's generator
    /// by the model's id, which a compiled model keeps from the model it was written from.
    /// </summary>
    private CompiledULongKeysDb Compiled(string provider, bool nativeAot)
    {
        using var design = new CompiledULongKeysDb(Options<CompiledULongKeysDb>(provider));
        var compiled = DesignTime.CompiledModel(design, nativeAot);
        var db = new CompiledULongKeysDb(
            new DbContextOptionsBuilder<CompiledULongKeysDb>(Options<CompiledULongKeysDb>(provider)).UseModel(compiled).EnableServiceProviderCaching(false).Options);

        db.Model.ShouldBeSameAs(compiled);
        return db;
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

    /// <summary>
    /// A compiled model (<c>dotnet ef dbcontext optimize</c>) can name only a value generator factory's type,
    /// never a delegate: the factory a Guid key generated on add gets is written out, and makes the provider's
    /// version 7 Guids from it. No database is opened: Npgsql generates these at <c>Add</c>.
    /// </summary>
    [Fact]
    public void In_a_compiled_model_a_Guid_key_generated_on_add_gets_the_provider_s_generator()
    {
        DbContextOptionsBuilder<CompiledGeneratedKeysDb> Options() => new DbContextOptionsBuilder<CompiledGeneratedKeysDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit();

        using var design = new CompiledGeneratedKeysDb(Options().Options);
        var compiled = DesignTime.CompiledModel(design);
        using var db = new CompiledGeneratedKeysDb(Options().UseModel(compiled).Options);

        db.Model.ShouldBeSameAs(compiled);
        compiled.FindEntityType(typeof(GeneratedByWrapsGuid))!.FindProperty("Id")!.GetValueGeneratorFactory().ShouldNotBeNull();
        GeneratedByWrapsGuid[] guids = [new(), new()];
        db.AddRange(guids);

        guids.Select(e => e.Id.Value.Version).ShouldBe([7, 7]);
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

/// <summary>
/// A key over each integer type, beside a key of that type itself, for the model only. Not <c>char</c>,
/// which no provider generates and a value object's key leaves to the application (<see cref="CharAndULongKeysDb"/>).
/// </summary>
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

/// <summary>A <c>char</c> key configured to be generated on add.</summary>
public class GeneratedOnAddCharKey { public CharKey Id { get; set; } }

/// <summary>A <c>char</c> key given a value generator of the application's own.</summary>
public class OwnGeneratorCharKey { public CharKey Id { get; set; } }

/// <summary>A <c>ulong</c> key the application assigns, as the README says to configure it.</summary>
public class AssignedUInt64Key { public UInt64Key Id { get; set; } }

/// <summary>Hands out the letter A, as an application's own generator would hand out its keys.</summary>
public sealed class LetterKeyGenerator : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator<CharKey>
{
    public override bool GeneratesTemporaryValues => false;

    public override CharKey Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) => CharKey.From('A');
}

/// <summary>Keys over <c>char</c> and <c>ulong</c>, by convention and as an application configures them.</summary>
public sealed class CharAndULongKeysDb(DbContextOptions<CharAndULongKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyedByChar>();
        modelBuilder.Entity<GeneratedOnAddCharKey>().Property(e => e.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<OwnGeneratorCharKey>().Property(e => e.Id).HasValueGenerator<LetterKeyGenerator>();
        modelBuilder.Entity<KeyedByUInt64>();
        modelBuilder.Entity<KeyedByUlong>();
        modelBuilder.Entity<AssignedUInt64Key>().Property(e => e.Id).ValueGeneratedNever();
    }
}

public class HiLoUInt64Key { public UInt64Key Id { get; set; } }

public class HiLoUlongKey { public ulong Id { get; set; } }

public class OwnGeneratorUInt64Key { public UInt64Key Id { get; set; } }

public sealed class SevenGenerator : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator<UInt64Key>
{
    public override bool GeneratesTemporaryValues => false;

    public override UInt64Key Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) => UInt64Key.From(7);
}

/// <summary>A key over <c>ulong</c> configured with the public converter by hand.</summary>
public sealed class NamedConverterULongKeyDb(DbContextOptions<NamedConverterULongKeyDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<KeyedByUInt64>().Property(e => e.Id).HasConversion<ValueObjectConverter<UInt64Key, ulong>>();
}

/// <summary>A default key over <c>ulong</c> and a plain one, for a compiled model, which refuses a generator type.</summary>
public sealed class CompiledULongKeysDb(DbContextOptions<CompiledULongKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyedByUInt64>();
        modelBuilder.Entity<KeyedByUlong>();
    }
}

/// <summary>Keys over <c>ulong</c> numbered by SQL Server's HiLo, and one numbered by the application's own generator.</summary>
public sealed class ULongHiLoKeysDb(DbContextOptions<ULongHiLoKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Npgsql refuses HiLo on a ulong key, plain or value object: "Sequences can only be used with integer properties".
        if (Database.IsSqlServer())
        {
            SqlServerPropertyBuilderExtensions.UseHiLo(modelBuilder.Entity<HiLoUInt64Key>().Property(e => e.Id));
            SqlServerPropertyBuilderExtensions.UseHiLo(modelBuilder.Entity<HiLoUlongKey>().Property(e => e.Id));
        }

        modelBuilder.Entity<OwnGeneratorUInt64Key>().Property(e => e.Id).HasValueGenerator<SevenGenerator>();
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

/// <summary>The keys over the integer types PostgreSQL has an identity column for, beyond <c>int</c> and <c>long</c>.</summary>
public sealed class PostgreSqlIntegerKeysDb(DbContextOptions<PostgreSqlIntegerKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyedByInt16>();
        modelBuilder.Entity<KeyedBySByte>();
        modelBuilder.Entity<KeyedByUInt16>();
        modelBuilder.Entity<KeyedByUInt32>();
    }
}

/// <summary>Keys over a Guid and a string configured generated on add, each beside the plain type's.</summary>
public sealed class PostgreSqlGeneratedKeysDb(DbContextOptions<PostgreSqlGeneratedKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GeneratedByGuid>().Property(e => e.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<GeneratedByWrapsGuid>().Property(e => e.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<GeneratedByString>().Property(e => e.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<GeneratedByWrapsString>().Property(e => e.Id).ValueGeneratedOnAdd();
    }
}

/// <summary>The Guid half of <see cref="PostgreSqlGeneratedKeysDb"/>, for a compiled model.</summary>
public sealed class CompiledGeneratedKeysDb(DbContextOptions<CompiledGeneratedKeysDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<GeneratedByWrapsGuid>().Property(e => e.Id).ValueGeneratedOnAdd();
}

public class GeneratedByGuid { public Guid Id { get; set; } }

public class GeneratedByWrapsGuid { public Variants.WrapsGuid Id { get; set; } }

public class GeneratedByString { public string Id { get; set; } = null!; }

public class GeneratedByWrapsString { public Variants.WrapsString Id { get; set; } }

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
