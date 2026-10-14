using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// <see cref="FaultResponseOptions.BareFaultNameMember"/> writes the bare member name under a second member
/// beside a qualified <c>fault</c>, for a client that predates it, and the document describes it; a member a
/// problem has already stops the host (decision 80).
/// </summary>
public sealed class BareFaultNameTests
{
    [Fact]
    public async Task The_bare_name_is_written_beside_the_qualified_one()
    {
        await using var app = await StartAsync(qualified: true, bare: "code");

        var problem = await ProblemAsync(app);
        problem["fault"]!.GetValue<string>().ShouldBe("ProbeOrderFault.NotFound");
        problem["code"]!.GetValue<string>().ShouldBe("NotFound");
    }

    [Fact]
    public async Task It_is_written_beside_a_bare_fault_too()
    {
        await using var app = await StartAsync(qualified: false, bare: "code");

        var problem = await ProblemAsync(app);
        problem["fault"]!.GetValue<string>().ShouldBe("NotFound");
        problem["code"]!.GetValue<string>().ShouldBe("NotFound");
    }

    [Fact]
    public async Task Without_it_the_problem_has_no_second_member()
    {
        await using var app = await StartAsync(qualified: true, bare: null);

        (await ProblemAsync(app)).AsObject().Select(member => member.Key).ShouldBe(["type", "title", "status", "detail", "fault", "traceId"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_document_describes_it_beside_fault()
    {
        await using var app = await StartAsync(qualified: true, bare: "code");

        var document = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var properties = document["components"]!["schemas"]!["ProblemDetails"]!["properties"]!;
        properties["code"]!["type"]!.GetValue<string>().ShouldBe("string");
        properties["code"]!["description"]!.GetValue<string>().ShouldBe(
            "The fault this problem reports: the name of its member, without its type. Present only on a problem that answers a fault of the endpoint's Result.");
    }

    [Theory]
    [InlineData("fault", "fault")]
    [InlineData("Rule", "rule")]
    [InlineData("DETAIL", "detail")]
    [InlineData("traceid", "traceId")]
    public async Task A_member_a_problem_has_already_stops_the_host(string bare, string taken)
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(qualified: true, bare));

        refusal.Message.ShouldStartWith($"FaultResponseOptions.BareFaultNameMember is \"{bare}\", which a problem writes as \"{taken}\" already: name another member.");
    }

    [Fact]
    public async Task A_blank_member_stops_the_host() =>
        (await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(qualified: true, bare: " ")))
            .Message.ShouldStartWith("FaultResponseOptions.BareFaultNameMember is blank");

    private static Result<Fault<ProbeOrderFault>> Missing() => Fault.Of(ProbeOrderFault.NotFound);

    private static async Task<WebApplication> StartAsync(bool qualified, string? bare)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        builder.Services.AddFaultResponses<ProbeOrderFault>();
        builder.Services.Configure<FaultResponseOptions>(options =>
        {
            options.QualifiedFaultNames = qualified;
            options.BareFaultNameMember = bare;
        });

        var app = builder.Build();
        app.MapOpenApi();
        app.MapDelete("/orders", () => Missing().ToHttpResult());

        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static async Task<JsonNode> ProblemAsync(WebApplication app)
    {
        using var response = await app.GetTestClient().DeleteAsync("/orders", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }
}
