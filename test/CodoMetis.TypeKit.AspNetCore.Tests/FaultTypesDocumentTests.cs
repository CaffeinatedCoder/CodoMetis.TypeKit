using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// An operation that answers two fault types lists one response per status, ascending, with the problem
/// types of both where they share a status. The transformer skipped a status an earlier type had listed,
/// so the second type's problem types were missing, and listed the statuses in the order of the types.
/// </summary>
public sealed class FaultTypesDocumentTests
{
    public enum StockFault { Gone, Reserved }

    public enum LockFault { Held, Expired }

    [Fact]
    public async Task Two_fault_types_are_listed_together_by_status_ascending()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        builder.Services.AddFaultResponses<LockFault>(fault => FaultResponse.Status(fault == LockFault.Held ? StatusCodes.Status423Locked : StatusCodes.Status409Conflict)
                                                                            .WithType(new Uri($"https://problems.example/LockFault.{fault}")));
        builder.Services.AddFaultResponses<StockFault>(fault => FaultResponse.Status(fault == StockFault.Gone ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict)
                                                                              .WithType(new Uri($"https://problems.example/StockFault.{fault}")));
        await using var app = builder.Build();
        app.MapOpenApi();
        // The document reads the declared return type; the handler never runs.
        app.MapPost("/stock/{id:int}", Results<NoContent, FaultProblem<LockFault>, FaultProblem<StockFault>> (int id) => throw new NotSupportedException());
        await app.StartAsync(TestContext.Current.CancellationToken);

        var responses = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!
                                ["paths"]!["/stock/{id}"]!["post"]!["responses"]!;

        responses["409"]!["description"]!.GetValue<string>()
            .ShouldBe("Conflict. Problem types: https://problems.example/LockFault.Expired, https://problems.example/StockFault.Reserved");
        responses.AsObject().Select(response => response.Key).ShouldBe(["204", "404", "409", "423"]);
    }
}
