using System.Globalization;
using CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// The key-generation catalogue (<see cref="KeyShapes"/>) inserted into a real SQL Server and read back: every shape
/// keyed by every kind of <see cref="WrappedKinds.Keys"/>, under <c>UseTypeKit()</c> and keyed by the wrapped type
/// under plain EF, each in a schema of its own.
/// </summary>
/// <remarks>
/// <see cref="KeyGenerationVariantTests"/> holds the model: a value-object key is generated as a key of the wrapped
/// type is. Here the database answers. A key the model generates is left unset, one it does not is assigned, and
/// every key reads back as it was after <c>SaveChanges</c>. Where the database numbers the keys (an identity column,
/// TPC's sequence), the value object gets the numbers the wrapped type gets.
/// </remarks>
[Collection(SqlServerCollection.Name)]
[Trait("Needs", "Docker")]
public sealed class SqlServerKeyVariantTests(SqlServer server)
{
    public static TheoryData<KeyShape, string> Cases
    {
        get
        {
            var cases = new TheoryData<KeyShape, string>();
            foreach (var (_, shape, kind) in KeyShapes.All().Where(@case => @case.Provider == "sqlserver")) cases.Add(shape, kind.Name);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_value_object_key_is_stored_and_read_back_as_a_key_of_the_wrapped_type_is(KeyShape shape, string kind)
    {
        var wrapped = WrappedKinds.Named(kind);
        var database = await server.DatabaseAsync("typekit_keys");

        var plain = Inserted(shape, wrapped, typeKit: false, database);
        var valueObject = Inserted(shape, wrapped, typeKit: true, database);
        var keys = $"{shape} keyed by {kind}: value object [{string.Join(", ", valueObject.Saved)}], wrapped type [{string.Join(", ", plain.Saved)}]";

        if (plain.Refusal is not null)
        {
            if (valueObject.Refusal is not null) Assert.Skip($"refused for the wrapped type and the value object alike: {plain.Refusal}");

            // A key over char, which the application assigns (the README's exception), where EF generates the plain one as NULL.
            wrapped.Wrapped.ShouldBe(typeof(char), $"stored where the wrapped type's keys are refused ({plain.Refusal}); {keys}");
            ReadBack(valueObject, wrapped).ShouldBe(Saved(valueObject, wrapped), ignoreOrder: true, $"the keys read back; {keys}");
            return;
        }

        valueObject.Refusal.ShouldBeNull(keys);
        ReadBack(valueObject, wrapped).ShouldBe(Saved(valueObject, wrapped), ignoreOrder: true, $"the keys read back; {keys}");
        ReadBack(plain, wrapped).ShouldBe(Saved(plain, wrapped), ignoreOrder: true, $"the wrapped type's keys read back; {keys}");

        // The README's two exceptions: a key over char or Guid is the application's to assign unless configured.
        var assigned = (wrapped.Wrapped == typeof(char) || wrapped.Wrapped == typeof(Guid)) && shape is not (KeyShape.ConfiguredOnAdd or KeyShape.ConfiguredNever);
        valueObject.Generated.ShouldBe(plain.Generated && !assigned, keys);
        valueObject.Numbered.ShouldBe(plain.Numbered && !assigned, keys);
        if (plain.Numbered && IsInteger(wrapped.Wrapped))
            valueObject.Saved.ShouldBe(plain.Saved, $"the keys SQL Server numbered; {keys}");
    }

    /// <summary>A guard against a vacuous run: every shape crossed with every key kind, and the database numbering some.</summary>
    [Fact]
    public void The_catalogue_crosses_every_shape_with_every_key_kind() =>
        Cases.Count.ShouldBe(Enum.GetValues<KeyShape>().Length * WrappedKinds.Keys.Length);

    /// <summary>
    /// A decimal reads back at its column's scale, <c>1.50</c> for <c>1.5</c> in SQL Server's default <c>decimal(18,2)</c>
    /// (KB-050), and is the same number: decimal keys are compared as numbers.
    /// </summary>
    private static string[] ReadBack(Keys keys, WrappedKind kind) => kind.Wrapped == typeof(decimal) ? [.. keys.Read.Select(Number)] : keys.Read;

    private static string[] Saved(Keys keys, WrappedKind kind) => kind.Wrapped == typeof(decimal) ? [.. keys.Saved.Select(Number)] : keys.Saved;

    private static string Number(string key) =>
        string.Join("/", key.Split('/').Select(part => decimal.Parse(part, CultureInfo.InvariantCulture).ToString("0.############", CultureInfo.InvariantCulture)));

    private static int _schemas;

    /// <summary>The keys saved and read back, whether the subject's key is generated, and whether the database left any key it saved to the database.</summary>
    private sealed record Keys(string[] Saved, string[] Read, bool Generated, bool Numbered, string? Refusal);

    /// <summary>Two entities of the shape saved, the keys they have after <c>SaveChanges</c>, and the keys a fresh context reads.</summary>
    private static Keys Inserted(KeyShape shape, WrappedKind kind, bool typeKit, string database)
    {
        var key = typeKit ? kind.ValueObject : kind.Wrapped;
        var schema = $"k{Interlocked.Increment(ref _schemas)}";
        try
        {
            using (var create = KeyShapeDb.OnSqlServer(shape, key, typeKit, database, schema))
                create.GetService<IRelationalDatabaseCreator>().CreateTables();

            string[] saved;
            bool generated, numbered;
            using (var db = KeyShapeDb.OnSqlServer(shape, key, typeKit, database, schema))
            {
                generated = KeyShapes.KeyOf(db, shape, key) is [{ ValueGenerated: ValueGenerated.OnAdd }];
                object Value(int index) => typeKit ? kind.Wrap(kind.Values[index]) : kind.Values[index];
                (saved, numbered) = Save(db, shape, key, Value);
            }

            using var read = KeyShapeDb.OnSqlServer(shape, key, typeKit, database, schema);
            return new Keys(saved, Read(read, shape, key), generated, numbered, null);
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            return new Keys([], [], false, false, $"{exception.GetType().Name}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>The shape's entities saved, each key left to the model's generation or assigned, and whether any was left.</summary>
    private static (string[] Saved, bool Numbered) Save(KeyShapeDb db, KeyShape shape, Type key, Func<int, object> value)
    {
        var numbered = false;
        Type Closed(Type definition) => definition.MakeGenericType(key);
        object New(Type type, int index)
        {
            var entity = Activator.CreateInstance(type)!;
            if (db.Model.FindEntityType(type)!.FindPrimaryKey()!.Properties is [{ ValueGenerated: ValueGenerated.OnAdd }]) numbered = true;
            else type.GetProperty("Id")!.SetValue(entity, value(index));
            return entity;
        }

        object[] entities = shape switch
        {
            KeyShape.TphRoot or KeyShape.TptRoot or KeyShape.TpcRoot                         => [New(Closed(typeof(Root<>)), 0), New(Closed(typeof(Leaf<>)), 1)],
            KeyShape.AbstractTphRoot or KeyShape.AbstractTptRoot or KeyShape.AbstractTpcRoot => [New(Closed(typeof(LeafA<>)), 0), New(Closed(typeof(LeafB<>)), 1)],
            KeyShape.OwnedDependent                                                          => [New(Closed(typeof(Owner<>)), 0), New(Closed(typeof(Owner<>)), 1)],
            KeyShape.SharedKeyDependent or KeyShape.TableSplitDependent                      => [New(Closed(typeof(Principal<>)), 0), New(Closed(typeof(Principal<>)), 1)],
            KeyShape.Composite                                                               => [Composite(key, value(0), value(1)), Composite(key, value(1), value(0))],
            _                                                                                => [New(Closed(typeof(Alone<>)), 0), New(Closed(typeof(Alone<>)), 1)],
        };
        db.AddRange(entities);
        db.SaveChanges();

        // A dependent's key is its principal's, so the dependents follow the principals they share it with.
        if (shape is KeyShape.SharedKeyDependent or KeyShape.TableSplitDependent)
        {
            object[] dependents = [.. entities.Select(principal =>
            {
                var dependent = Activator.CreateInstance(Closed(typeof(Dependent<>)))!;
                dependent.GetType().GetProperty("Id")!.SetValue(dependent, principal.GetType().GetProperty("Id")!.GetValue(principal));
                return dependent;
            })];
            db.AddRange(dependents);
            db.SaveChanges();
            entities = dependents;
        }

        return ([.. entities.Select(Text)], numbered);
    }

    private static object Composite(Type key, object id, object line)
    {
        var entity = Activator.CreateInstance(typeof(Composite<>).MakeGenericType(key))!;
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);
        entity.GetType().GetProperty("Line")!.SetValue(entity, line);
        return entity;
    }

    /// <summary>The keys of the entities the shape is about, as a fresh context reads them.</summary>
    private static string[] Read(KeyShapeDb db, KeyShape shape, Type key)
    {
        var type = shape switch
        {
            KeyShape.OwnedDependent => typeof(Owner<>).MakeGenericType(key),
            _                       => KeyShapes.Subject(shape, key),
        };
        var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type).Invoke(db, null)!;
        return [.. set.Cast<object>().AsEnumerable().Select(Text)];
    }

    /// <summary>An entity's key as text, both properties of a composite one.</summary>
    private static string Text(object entity) =>
        entity.GetType().GetProperty("Line") is { } line
            ? $"{WrappedKind.Text(entity.GetType().GetProperty("Id")!.GetValue(entity))}/{WrappedKind.Text(line.GetValue(entity))}"
            : WrappedKind.Text(entity.GetType().GetProperty("Id")!.GetValue(entity));

    private static bool IsInteger(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte) || type == typeof(byte)
     || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort);
}
