using System.Collections.Concurrent;
using CodoMetis.TypeKit.AspNetCore;
using ConsumerSmokeTest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Walkthrough;

namespace ReadmeSamples.Docs;

/// <summary>
/// The walkthrough's orders service, run in memory from the project references: every answer, log line and SQL
/// statement <c>docs/walkthrough.md</c> states in a comment is what the service gives. The consumer smoke test
/// runs the same service from the packed packages (<c>tools/ConsumerSmokeTest</c>, its <c>walkthrough</c>
/// consumer, whose sources this project compiles too); this runs it on every pull request's test leg.
/// </summary>
/// <remarks>
/// The expected text is read from the page by <see cref="WalkthroughPage"/>, the smoke test's own reader, compiled
/// here too: never written into this test, so a changed answer is corrected in the page's comment.
/// </remarks>
[Collection(nameof(Serilog.ConsoleOutput))]
public sealed class WalkthroughTests
{
    private static readonly string Page = SampleOutputs.Readme(WalkthroughPage.Path);

    [Fact]
    public async Task The_service_answers_what_the_page_states()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var sql = new ConcurrentQueue<string>();

        // The setup Program.cs has, but for the host's address and the SQL, which is kept here rather than printed.
        // The log is written to the console as there, with the console captured: the collection runs alone.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound => StatusCodes.Status404NotFound,
            OrderFault.AlreadyShipped or OrderFault.Cancelled => StatusCodes.Status409Conflict,
            OrderFault.Cleared => StatusCodes.Status422UnprocessableEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.RespectRequiredConstructorParameters = true);
        builder.Services.AddDbContext<OrdersDb>(options => options.UseSqlite(connection).UseTypeKit()
            .LogTo(sql.Enqueue, [RelationalEventId.CommandExecuted]));
        builder.Services.AddSingleton<global::Serilog.ILogger>(new LoggerConfiguration()
            .Destructure.WithTypeKit()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger());

        await using var app = builder.Build();
        app.UseStatusCodePages();
        app.MapOpenApi();
        app.MapOrders();
        using (var scope = app.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<OrdersDb>().Database.EnsureCreated();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var reported = new List<(string Name, string Value)>();
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            await Tour.RunAsync(app.GetTestClient(), (name, value) => reported.Add((name, value)));
        }
        finally
        {
            Console.SetOut(original);
        }
        await app.StopAsync(TestContext.Current.CancellationToken);

        var problems = new List<string>();
        foreach (var (name, value) in reported)
        {
            var stated = WalkthroughPage.Stated(Page, WalkthroughPage.Answer(name));
            if (!WalkthroughPage.Is(stated, value)) problems.Add($"the page states '{stated.Single()}' for {name}, but it is '{value}'");
        }

        WalkthroughPage.Declarations(Page).Except(reported.Select(answer => answer.Name))
                       .ShouldBeEmpty("The page states these answers, which the tour never reports.");

        var logged = console.ToString().ReplaceLineEndings("\n").Split('\n').Where(line => line.StartsWith("[INF] ", StringComparison.Ordinal)).ToArray();
        Within(problems, "log.Information(\"Placed", logged, "the log");

        var statements = sql.SelectMany(message => message.Split('\n')).ToArray();
        Within(problems, "order.Quantity.Value >= minimum.Value", statements, "the SQL");
        Within(problems, "Database.EnsureCreated()", statements, "the SQL");

        logged.ShouldNotContain(line => line.Contains("ann@example.com", StringComparison.Ordinal), "The e-mail address is masked in every log line.");
        problems.ShouldBeEmpty();
    }

    private static void Within(List<string> problems, string fragment, IReadOnlyList<string> lines, string what)
    {
        var stated = WalkthroughPage.Stated(Page, fragment);
        if (!WalkthroughPage.Within(stated, lines))
            problems.Add($"the page states '{string.Join(" / ", stated)}' for '{fragment}', which {what} does not hold: {string.Join(" | ", lines)}");
    }
}
