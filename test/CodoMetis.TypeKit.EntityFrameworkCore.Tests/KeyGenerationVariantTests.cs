using System.Text.RegularExpressions;
using CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// Key generation across the variant catalogue of where a key sits (<see cref="KeyShapes"/>): every shape
/// keyed by every kind of <see cref="WrappedKinds.Keys"/>, on SQLite, Npgsql and SQL Server, each
/// against the same shape keyed by the wrapped type.
/// </summary>
/// <remarks>
/// The oracle is the README's "Keys" section, written as a differential: a value-object key is generated
/// as a key of the type it wraps is (whether, from what default, by which provider strategy), and on
/// SQLite the database fills in the same keys. Where it departs from parity, the case follows the README
/// (<see cref="Expected"/>): a key over <c>char</c> or <c>Guid</c> is the application's to assign unless
/// configured, and a TPC root's key is generated only from a sequence. A model the wrapped type cannot
/// have is no case.
/// </remarks>
public sealed partial class KeyGenerationVariantTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public static TheoryData<string, KeyShape, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, KeyShape, string>();
            foreach (var (provider, shape, kind) in KeyShapes.All()) cases.Add(provider, shape, kind.Name);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_value_object_key_is_generated_as_a_key_of_the_wrapped_type_is(string provider, KeyShape shape, string kind)
    {
        var wrapped = WrappedKinds.Named(kind);
        _connection.Open();

        var plain = Facts(provider, shape, wrapped.Wrapped, typeKit: false);
        var valueObject = Facts(provider, shape, wrapped.ValueObject, typeKit: true);

        if (plain.Refusal is not null)
        {
            Assert.Skip($"the wrapped type's model is refused: {plain.Refusal}");
        }

        var keys = $"{shape} keyed by {kind} on {provider}: value object [{string.Join("; ", valueObject.Keys)}], wrapped type [{string.Join("; ", plain.Keys)}]";
        valueObject.Refusal.ShouldBeNull($"the value object's model is refused where the wrapped type's is not");
        var expected = Expected(shape, wrapped, plain);
        valueObject.Keys.Select(key => key.Generated).ShouldBe(expected, keys);
        if (expected.SequenceEqual(plain.Keys.Select(key => key.Generated)))
            valueObject.Keys.ShouldBe(plain.Keys, keys);

        // The keys the database fills in; a key the client generates (a Guid, a string) differs on every run.
        if (provider == "sqlite" && IsInteger(wrapped.Wrapped) && expected is [ValueGenerated.OnAdd] && plain.Keys is [{ Generated: ValueGenerated.OnAdd }])
            Inserted(shape, wrapped.ValueObject, typeKit: true).ShouldBe(Inserted(shape, wrapped.Wrapped, typeKit: false), $"the keys SQLite filled in for {shape} keyed by {kind}");
    }

    public static TheoryData<string, string> ConfiguredOnAddCases
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach (var provider in KeyShapes.Providers)
            foreach (var kind in WrappedKinds.Keys)
                cases.Add(provider, kind.Name);
            return cases;
        }
    }

    /// <summary>
    /// A key configured <c>ValueGeneratedOnAdd()</c> is filled in at <c>Add</c> by the generator the provider
    /// gives a key of the wrapped type: on PostgreSQL a version 7 Guid where the wrapped key gets one, on SQL
    /// Server a Guid sequential in its order. The providers pick it by the property's CLR type, which gave a
    /// value object's key EF's random Guid instead. No database is opened: the client generates these.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConfiguredOnAddCases))]
    public void A_value_object_key_generated_on_add_gets_values_as_a_key_of_the_wrapped_type_does(string provider, string kind)
    {
        var wrapped = WrappedKinds.Named(kind);
        _connection.Open();

        var plain = Added(provider, wrapped.Wrapped, typeKit: false);
        if (plain is null) Assert.Skip("the wrapped type's key is not generated at Add");

        Added(provider, wrapped.ValueObject, typeKit: true).ShouldBe(plain, $"{kind} configured OnAdd on {provider}");
    }

    /// <summary>What the values generated at <c>Add</c> are, or null where <c>Add</c> generates none or refuses.</summary>
    private string? Added(string provider, Type key, bool typeKit)
    {
        using var db = KeyShapeDb.Create(provider, KeyShape.ConfiguredOnAdd, key, typeKit, _connection);
        var entities = Enumerable.Range(0, 16).Select(_ => Activator.CreateInstance(KeyShapes.Subject(KeyShape.ConfiguredOnAdd, key))!).ToArray();
        try
        {
            db.AddRange(entities);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or OverflowException)
        {
            return null;
        }

        var keys = entities.Select(entity => db.Entry(entity).Property("Id")).ToArray();
        return keys.All(property => property.IsTemporary) ? "temporary" : GeneratedShape([.. keys.Select(property => WrappedKind.Text(property.CurrentValue))]);
    }

    /// <summary>
    /// What tells the generators apart: whether each value is a Guid (or a Guid's text), its version, and whether
    /// the values ascend in SQL Server's order, as its sequential generator's do and random ones do not.
    /// </summary>
    private static string GeneratedShape(string[] values)
    {
        if (!values.All(value => System.Guid.TryParse(value, out _))) return $"not Guids: {string.Join(", ", values.Take(2))}";

        var guids = values.Select(System.Guid.Parse).ToArray();
        var versions = string.Join(",", guids.Select(guid => guid.Version).Distinct());
        var ascending = guids.Zip(guids.Skip(1)).All(pair => new System.Data.SqlTypes.SqlGuid(pair.First).CompareTo(new System.Data.SqlTypes.SqlGuid(pair.Second)) < 0);
        return $"version {versions}, {(ascending ? "sequential" : "unordered")} in SQL Server's order";
    }

    /// <summary>A guard against a vacuous run: the catalogue's size, and the kinds and shapes it crosses.</summary>
    [Fact]
    public void The_catalogue_crosses_every_shape_with_every_key_kind_on_three_providers() =>
        KeyShapes.All().Count().ShouldBe(Enum.GetValues<KeyShape>().Length * WrappedKinds.Keys.Length * 3);

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// The README's rule, per key: as a key of the wrapped type, with its two exceptions, a key over
    /// <c>char</c> or <c>Guid</c> "is yours to assign" unless configured, and its rule for TPC: "the key is
    /// generated only where the provider numbers the whole hierarchy from a sequence". EF leaves a plain key
    /// declared generated under TPC where the provider has no sequence for its type (a <c>ulong</c>, a
    /// <c>numeric(20,0)</c> on PostgreSQL), and nothing fills it in.
    /// </summary>
    private static ValueGenerated[] Expected(KeyShape shape, WrappedKind kind, Model plain) =>
    [
        .. plain.Keys.Select(key =>
            (kind.Wrapped == typeof(char) || kind.Wrapped == typeof(Guid)) && shape is not (KeyShape.ConfiguredOnAdd or KeyShape.ConfiguredNever) ? ValueGenerated.Never
          : shape is KeyShape.TpcRoot or KeyShape.AbstractTpcRoot && key is { Generated: ValueGenerated.OnAdd, DefaultSql: null }       ? ValueGenerated.Never
          : key.Generated),
    ];

    private static bool IsInteger(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte) || type == typeof(byte)
     || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort);

    private Model Facts(string provider, KeyShape shape, Type key, bool typeKit)
    {
        using var db = KeyShapeDb.Create(provider, shape, key, typeKit, _connection);
        try
        {
            return new Model([.. KeyShapes.KeyOf(db, shape, key).Select(Describe)], null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return new Model([], $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static Key Describe(IProperty property) => new(
        property.ValueGenerated,
        Normalized(property.GetDefaultValueSql()),
        string.Join(", ", property.GetAnnotations().Where(annotation => annotation.Name.EndsWith("ValueGenerationStrategy", StringComparison.Ordinal))
                                  .Select(annotation => $"{annotation.Name}={annotation.Value}")));

    /// <summary>A name EF derived from the closed entity type, the same for either key: <c>Root&lt;int&gt;Sequence</c> as <c>Root&lt;K&gt;Sequence</c>.</summary>
    private static string? Normalized(string? sql) => sql is null ? null : TypeArguments().Replace(sql, "<K>");

    /// <summary>Two entities of the shape added without a key, saved, and the keys SQLite gave them.</summary>
    private string[] Inserted(KeyShape shape, Type key, bool typeKit)
    {
        using var db = KeyShapeDb.Create("sqlite", shape, key, typeKit, _connection);
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        Type[] types = shape switch
        {
            KeyShape.TphRoot or KeyShape.TptRoot                 => [typeof(Root<>).MakeGenericType(key), typeof(Leaf<>).MakeGenericType(key)],
            KeyShape.AbstractTphRoot or KeyShape.AbstractTptRoot => [typeof(LeafA<>).MakeGenericType(key), typeof(LeafB<>).MakeGenericType(key)],
            _                                                    => [KeyShapes.Subject(shape, key), KeyShapes.Subject(shape, key)],
        };
        var entities = types.Select(type => Activator.CreateInstance(type)!).ToArray();
        db.AddRange(entities);
        db.SaveChanges();

        return [.. entities.Select(entity => WrappedKind.Text(entity.GetType().GetProperty("Id")!.GetValue(entity)))];
    }

    [GeneratedRegex("<[^<>]*>")]
    private static partial Regex TypeArguments();

    private sealed record Model(Key[] Keys, string? Refusal);

    private sealed record Key(ValueGenerated Generated, string? DefaultSql, string Strategy);
}
