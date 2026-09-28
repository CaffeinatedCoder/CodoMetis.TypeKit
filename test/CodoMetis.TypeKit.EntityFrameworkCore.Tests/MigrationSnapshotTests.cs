using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A model with value-object keys over integers matches the migration snapshot EF writes for it, so the
/// next <c>migrations add</c> finds nothing and <c>Migrate()</c> does not refuse with
/// <c>PendingModelChangesWarning</c>.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot records a converted property by its provider type, <c>Property&lt;int&gt;("Id")</c>, and a
/// provider reads its defaults off that type when the snapshot is loaded. EF's SQLite provider decides
/// <c>AUTOINCREMENT</c> by the property's CLR type: an <c>int</c> in the snapshot, a value object in the
/// model. So the model never matched its snapshot, every migration repeated an <c>AlterColumn</c> (a table
/// rebuild on SQLite) and <c>Migrate()</c> threw, until the key is configured with <c>UseAutoincrement()</c>,
/// as the package README says. PostgreSQL decides its identity column by the provider type and needs nothing.
/// </para>
/// <para>
/// The snapshot is written by EF's own scaffolder, with the provider's design-time services, found as
/// <c>dotnet ef</c> finds them, and compiled back into a model, which is what <c>HasPendingModelChanges()</c>
/// compares with.
/// </para>
/// </remarks>
public sealed class MigrationSnapshotTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public void On_PostgreSQL_integer_keys_match_their_snapshot_as_they_are()
    {
        using var db = new PostgreSqlKeysDb(new DbContextOptionsBuilder<PostgreSqlKeysDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options);

        Snapshots.DifferencesFromItsSnapshot(db).ShouldBeEmpty();
    }

    /// <summary>The configuration the README documents for SQLite: <c>UseAutoincrement()</c> on each such key.</summary>
    [Fact]
    public void On_SQLite_integer_keys_with_UseAutoincrement_match_their_snapshot()
    {
        using var db = new SqliteAutoincrementKeysDb(new DbContextOptionsBuilder<SqliteAutoincrementKeysDb>().UseSqlite(_connection).UseTypeKit().Options);

        Snapshots.DifferencesFromItsSnapshot(db).ShouldBeEmpty();
        foreach (var key in db.Model.GetEntityTypes().Select(entity => entity.FindPrimaryKey()!.Properties.Single()))
            SqlitePropertyExtensions.GetValueGenerationStrategy(key).ShouldBe(SqliteValueGenerationStrategy.Autoincrement, key.DeclaringType.DisplayName());
    }

    /// <summary>
    /// Why the README asks for <c>UseAutoincrement()</c> on SQLite, and the positive control for the comparison
    /// above. Once EF's SQLite provider decides <c>AUTOINCREMENT</c> by the provider type, this fails, and the
    /// README's SQLite note can go.
    /// </summary>
    [Fact]
    public void On_SQLite_integer_keys_without_UseAutoincrement_differ_from_their_snapshot()
    {
        using var db = new SqliteKeysDb(new DbContextOptionsBuilder<SqliteKeysDb>().UseSqlite(_connection).UseTypeKit().Options);

        Snapshots.DifferencesFromItsSnapshot(db).ShouldBe(
            ["AlterColumn Parcels.Id", "AlterColumn Shipments.Id"], ignoreOrder: true,
            "EF's SQLite provider may now make a value-object key AUTOINCREMENT on its own; the README's SQLite note can go.");
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>Two value-object keys over integers, <c>int</c> and <c>long</c>, each generated on add.</summary>
public abstract class KeysDb(DbContextOptions options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<Parcel> Parcels => Set<Parcel>();
}

public sealed class PostgreSqlKeysDb(DbContextOptions<PostgreSqlKeysDb> options) : KeysDb(options);

public sealed class SqliteKeysDb(DbContextOptions<SqliteKeysDb> options) : KeysDb(options);

public sealed class SqliteAutoincrementKeysDb(DbContextOptions<SqliteAutoincrementKeysDb> options) : KeysDb(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>().Property(shipment => shipment.Id).UseAutoincrement();
        modelBuilder.Entity<Parcel>().Property(parcel => parcel.Id).UseAutoincrement();
    }
}

internal static class Snapshots
{
    /// <summary>
    /// What a migration added now would contain: the differences between the model and the snapshot EF
    /// writes for it, as <c>HasPendingModelChanges()</c> computes them.
    /// </summary>
    public static IReadOnlyList<string> DifferencesFromItsSnapshot(DbContext db)
    {
        var snapshot = Compile(Scaffold(db).SnapshotCode, db.GetType().Assembly);
        var snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true, validationLogger: null);
        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshotModel.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        return [.. differences.Select(Describe)];
    }

    /// <summary>An operation as a migration's <c>Up</c> reads: <c>AlterColumn Shipments.Id</c>.</summary>
    private static string Describe(MigrationOperation operation)
    {
        var kind = operation.GetType().Name.Replace("Operation", "", StringComparison.Ordinal);

        return operation is ColumnOperation column ? $"{kind} {column.Table}.{column.Name}" : kind;
    }

    /// <summary>The first migration, as <c>dotnet ef migrations add</c> writes it, with the provider's design-time services.</summary>
    private static ScaffoldedMigration Scaffold(DbContext db)
    {
        // The order and the lookup of EF's DesignTimeServicesBuilder: the context, the provider's services
        // (named by the provider assembly's attribute), then EF's own, which are only added where missing.
        var services = new ServiceCollection().AddDbContextDesignTimeServices(db);
        var provider = Assembly.Load(new AssemblyName(db.GetService<IDatabaseProvider>().Name));
        var providerServices = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>().ShouldNotBeNull($"{provider} names no design-time services.");
        ((IDesignTimeServices)Activator.CreateInstance(provider.GetType(providerServices.TypeName, throwOnError: true)!)!).ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();

        using var designTime = services.BuildServiceProvider();

        return designTime.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "SnapshotProbe");
    }

    private static ModelSnapshot Compile(string code, Assembly context)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                                                                                     .Append(context.Location)
                                                                                     .Distinct(StringComparer.Ordinal)
                                                                                     .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            $"Snapshot{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(code)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        emitted.Success.ShouldBeTrue($"The scaffolded snapshot does not compile:{Environment.NewLine}{string.Join(Environment.NewLine, emitted.Diagnostics)}{Environment.NewLine}{code}");

        var snapshot = Assembly.Load(image.ToArray()).GetTypes().Single(type => type.IsSubclassOf(typeof(ModelSnapshot)));

        return (ModelSnapshot)Activator.CreateInstance(snapshot)!;
    }
}
