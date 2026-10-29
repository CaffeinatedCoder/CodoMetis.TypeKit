using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// One SQL Server container for every class of the <see cref="SqlServerCollection"/>, started when the first of
/// them runs: the server takes seconds to start, and a class that needs it never starts one of its own.
/// </summary>
/// <remarks>
/// SQL Server 2022's Linux image, which CI runs and the design/knowledge entries pin. It is amd64 only: on an
/// arm64 machine Docker must emulate it with Rosetta, since under QEMU it stops at start ("Unable to create a new
/// asynchronous I/O context"). <c>TYPEKIT_MSSQL_IMAGE</c> names another image, such as the native arm64
/// <c>mcr.microsoft.com/azure-sql-edge</c>, whose older engine lacks some of 2022's functions. A database is made
/// for each class (<see cref="DatabaseAsync"/>), under the server's default collation,
/// <c>SQL_Latin1_General_CP1_CI_AS</c>, which is case-insensitive.
/// </remarks>
public sealed class SqlServer : IAsyncLifetime
{
    public const string ImageVariable = "TYPEKIT_MSSQL_IMAGE";

    private const string Password = "TypeKit-2026!";

    /// <summary>Started by its log line: Testcontainers' module waits through <c>sqlcmd</c>, which Azure SQL Edge's image has not.</summary>
    private readonly IContainer _container =
        new ContainerBuilder(Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } image ? image : "mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", Password)
            .WithPortBinding(1433, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready for client connections"))
            .Build();

    private readonly ConcurrentDictionary<string, Task<string>> _databases = new();

    private string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = $"{_container.Hostname},{_container.GetMappedPublicPort(1433)}",
        UserID = "sa",
        Password = Password,
        InitialCatalog = database,
        TrustServerCertificate = true,
    }.ConnectionString;

    /// <summary>A database of its own on the server, created empty the first time it is asked for, and its connection string.</summary>
    public Task<string> DatabaseAsync(string name) => _databases.GetOrAdd(name, CreateAsync);

    private async Task<string> CreateAsync(string name)
    {
        await using var connection = await OpenAsync(ConnectionString("master"));
        await using var create = connection.CreateCommand();
        create.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL DROP DATABASE [{name}]; CREATE DATABASE [{name}];";
        await create.ExecuteNonQueryAsync();

        return ConnectionString(name);
    }

    /// <summary>
    /// The server logs that it is ready before the login works, the first time it starts (a "Login failed for user 'sa'"
    /// for some seconds after), so the first connection is tried again for a minute.
    /// </summary>
    private static async Task<SqlConnection> OpenAsync(string connectionString)
    {
        for (var attempt = 1; ; attempt++)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (SqlException) when (attempt < 60)
            {
                await connection.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>The classes that share the SQL Server container; CI's Windows and macOS legs, which cannot start it, leave them out.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServer>
{
    public const string Name = "SQL Server";
}
