using Microsoft.Data.SqlClient;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// A guard on the harness: a database many callers ask for at once is created once, as the rows of a sweep ask for
/// it when they run in parallel (#251). Created more than once, one caller drops it under another, "Database
/// 'typekit_queries' already exists", or after another has returned, and every row reading it fails.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Needs", "Docker")]
public sealed class SqlServerFixtureTests(SqlServer server)
{
    [Fact]
    public async Task A_database_asked_for_by_many_callers_at_once_is_created_once()
    {
        var name = $"typekit_fixture_{Guid.NewGuid():N}";

        var answers = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => server.DatabaseAsync(name))));

        answers.Distinct().ShouldHaveSingleItem();

        // A creation that lost the race could still drop the database after the winner returned; give it the time.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await using var connection = new SqlConnection(answers[0]);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME()";
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(name);
    }
}
