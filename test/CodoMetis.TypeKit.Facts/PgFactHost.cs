using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// A PostgreSQL database of its own for a fact, with plain EF Core and Npgsql and nothing of TypeKit's, on the
/// server the connection string in <c>TYPEKIT_PG</c> names (<c>Host=localhost;Port=55433;Username=postgres;Password=typekit</c>).
/// </summary>
/// <remarks>
/// A fact that needs one is skipped where the variable is unset. That is an interim: CI has no PostgreSQL service
/// for this project until WS6 adds one, and the entries' <c>test:</c> lines say so.
/// </remarks>
internal static class Pg
{
    public const string Variable = "TYPEKIT_PG";

    /// <summary>A fresh database for <paramref name="model"/>, created unless <paramref name="create"/> is false, and dropped when disposed.</summary>
    public static async Task<PgDb> ContextAsync(Action<ModelBuilder> model, bool create = true, [CallerFilePath] string fact = "")
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } connection)
        {
            Assert.Skip($"{Variable} is unset: this fact needs a PostgreSQL server, until WS6's CI service.");
            throw new UnreachableException();
        }

        var database = new NpgsqlConnectionStringBuilder(connection) { Database = $"kb_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<PgDb>().UseNpgsql(database.ConnectionString)
                                                          .ReplaceService<IModelCacheKeyFactory, PgDb.CacheKey>()
                                                          .Options;
        var db = new PgDb(options, model, Path.GetFileNameWithoutExtension(fact));
        if (create) await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        return db;
    }
}

/// <summary>A context whose model is the fact's, cached per fact.</summary>
internal sealed class PgDb(DbContextOptions<PgDb> options, Action<ModelBuilder> model, string fact) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => model(modelBuilder);

    public override async ValueTask DisposeAsync()
    {
        await Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is PgDb db ? (typeof(PgDb), db.FactName, designTime) : (context.GetType(), designTime);
    }

    private string FactName => fact;
}
