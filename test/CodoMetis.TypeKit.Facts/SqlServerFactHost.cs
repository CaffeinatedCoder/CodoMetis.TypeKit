using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// A SQL Server database of its own for a fact, with plain EF Core and nothing of TypeKit's, in one container started
/// by the first fact that needs it: SQL Server 2022's Linux image, which CI's Linux leg runs and the entries pin, or the
/// image <c>TYPEKIT_MSSQL_IMAGE</c> names (the native arm64 <c>mcr.microsoft.com/azure-sql-edge</c>, on a machine that
/// would emulate 2022 under QEMU, where it does not start).
/// </summary>
/// <remarks>The facts that need it carry the trait <c>Needs=Docker</c>, which CI's Windows and macOS legs leave out.</remarks>
internal static class SqlServer
{
    private const string Password = "TypeKit-2026!";

    private static readonly Lazy<Task<IContainer>> Container = new(async () =>
    {
        var container = new ContainerBuilder(Environment.GetEnvironmentVariable("TYPEKIT_MSSQL_IMAGE") is { Length: > 0 } image ? image : "mcr.microsoft.com/mssql/server:2022-latest")
                        .WithEnvironment("ACCEPT_EULA", "Y")
                        .WithEnvironment("MSSQL_SA_PASSWORD", Password)
                        .WithPortBinding(1433, assignRandomHostPort: true)
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready for client connections"))
                        .Build();
        await container.StartAsync();
        return container;
    });

    /// <summary>A fresh database for <paramref name="model"/>, created, and dropped when disposed; the model is cached per fact and test.</summary>
    public static async Task<SqlServerDb> ContextAsync(Action<ModelBuilder> model, Action<SqlServerDbContextOptionsBuilder>? sql = null,
                                                       [CallerFilePath] string fact = "", [CallerMemberName] string test = "")
    {
        var container = await Container.Value;
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = $"{container.Hostname},{container.GetMappedPublicPort(1433)}",
            UserID = "sa",
            Password = Password,
            InitialCatalog = $"kb_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<SqlServerDb>().UseSqlServer(connection, sql)
                                                                 .ReplaceService<IModelCacheKeyFactory, SqlServerDb.CacheKey>()
                                                                 .Options;
        var db = new SqlServerDb(options, model, $"{Path.GetFileNameWithoutExtension(fact)}.{test}");

        // The server logs that it is ready before its login works, the first time it starts.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
                return db;
            }
            catch (SqlException) when (attempt < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            }
        }
    }
}

/// <summary>A context whose model is the fact's, cached per fact.</summary>
internal sealed class SqlServerDb(DbContextOptions<SqlServerDb> options, Action<ModelBuilder> model, string fact) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => model(modelBuilder);

    public override async ValueTask DisposeAsync()
    {
        await Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is SqlServerDb db ? (typeof(SqlServerDb), db.FactName, designTime) : (context.GetType(), designTime);
    }

    private string FactName => fact;
}
