using CodoMetis.TypeKit.ClosedSets;
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
/// One mapping for every fault set, <c>AddFaultResponses()</c> or <c>AddFaultResponses(fault =&gt; …)</c>, in
/// place of one per set: a set mapped on its own keeps its mapping, an enum still needs one, and what the
/// default cannot map stops the host at start (decision 80).
/// </summary>
public sealed class FaultSetDefaultTests
{
    public enum PlainFault { Gone }

    private static Result<Fault<ProbeOrderFault, ProbeConcurrencyFault>> Rename(int id) => id switch
    {
        1 => Fault<ProbeOrderFault, ProbeConcurrencyFault>.Of(ProbeOrderFault.NotFound),
        2 => Fault<ProbeOrderFault, ProbeConcurrencyFault>.Of(ProbeConcurrencyFault.PreconditionFailed, "It is at version 4, not 3."),
        _ => Fault<ProbeOrderFault, ProbeConcurrencyFault>.Of(ProbeConcurrencyFault.Conflict),
    };

    private static Result<PlainFault> Gone() => PlainFault.Gone;

    private static Result<ProbeStatus> Opened() => ProbeStatus.Open;

    private static Result<Fault<ProbeKindlessFault>> Kindless() => Fault.Of(ProbeKindlessFault.Known);

    /// <summary>What a host writes once for every set: the kind's status, and a problem type per kind.</summary>
    private static FaultResponse ResponseFor(IFault fault) =>
        FaultResponse.For(fault.Kind).WithType(new Uri($"https://example.com/problems/{fault.Kind}"));

    [Fact]
    public async Task Every_fault_set_is_answered_by_its_kind_with_one_call()
    {
        await using var app = await StartAsync(services => services.AddFaultResponses());

        (await ProblemAsync(app, "/orders/1", HttpStatusCode.NotFound))["detail"]!.GetValue<string>().ShouldBe("The order does not exist.");
        (await ProblemAsync(app, "/orders/2", HttpStatusCode.PreconditionFailed))["detail"]!.GetValue<string>().ShouldBe("It is at version 4, not 3.");
        (await ProblemAsync(app, "/orders/3", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("ProbeConcurrencyFault.Conflict");

        Statuses(await DocumentAsync(app)).ShouldBe(["204", "403", "404", "409", "412"]);
    }

    [Fact]
    public async Task A_mapping_of_the_host_s_own_reaches_every_set_through_IFault()
    {
        await using var app = await StartAsync(services => services.AddFaultResponses(ResponseFor));

        (await ProblemAsync(app, "/orders/1", HttpStatusCode.NotFound))["type"]!.GetValue<string>().ShouldBe("https://example.com/problems/NotFound");
        (await ProblemAsync(app, "/orders/2", HttpStatusCode.PreconditionFailed))["type"]!.GetValue<string>().ShouldBe("https://example.com/problems/PreconditionFailed");

        var document = await DocumentAsync(app);
        Response(document, "409")["description"]!.GetValue<string>().ShouldBe("Conflict. Problem types: https://example.com/problems/Conflict");
    }

    [Fact]
    public async Task A_set_mapped_on_its_own_keeps_its_mapping()
    {
        await using var app = await StartAsync(services =>
        {
            services.AddFaultResponses();
            services.AddFaultResponses<ProbeOrderFault>(fault => fault == ProbeOrderFault.NotFound ? StatusCodes.Status410Gone : FaultResponse.For(fault.Kind));
        });

        await ProblemAsync(app, "/orders/1", HttpStatusCode.Gone);
        await ProblemAsync(app, "/orders/2", HttpStatusCode.PreconditionFailed);
        Statuses(await DocumentAsync(app)).ShouldBe(["204", "403", "404", "409", "410", "412"]);
    }

    [Fact]
    public async Task An_enum_still_needs_a_mapping_of_its_own()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddFaultResponses(),
            app => app.MapDelete("/plain", () => Gone().ToHttpResult())));

        refusal.Message.ShouldStartWith("No HTTP status is mapped for the fault type FaultSetDefaultTests.PlainFault, which these endpoints answer with: HTTP: DELETE /plain.");
    }

    /// <summary>
    /// A closed set whose members are no <see cref="IFault"/> is no fault set: it has no default, as an enum
    /// has none, and the host is refused with the unmapped message. The default reached for the members' kind
    /// and failed on the cast instead.
    /// </summary>
    [Fact]
    public async Task A_closed_set_that_is_no_fault_set_still_needs_a_mapping_of_its_own()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(
            services => services.AddFaultResponses(),
            app => app.MapDelete("/status", () => Opened().ToHttpResult())));

        refusal.Message.ShouldStartWith("No HTTP status is mapped for the fault type ProbeStatus, which these endpoints answer with: HTTP: DELETE /status.");
    }

    /// <summary>
    /// The README's "Any closed set can be an error type": a set that is no fault set, mapped on its own, is answered
    /// through <c>ToHttpResult()</c> with the status its mapping gives and its member's qualified name as the fault.
    /// </summary>
    [Fact]
    public async Task A_closed_set_that_is_no_fault_set_is_answered_with_a_mapping_of_its_own()
    {
        await using var app = await StartAsync(
            services =>
            {
                services.AddFaultResponses();   // the fault sets the other endpoints answer with
                services.AddFaultResponses<ProbeStatus>(status => status == ProbeStatus.Open ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity);
            },
            app => app.MapPut("/status", () => Opened().ToHttpResult()));

        (await ProblemAsync(app, "/status", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("ProbeStatus.Open");
    }

    [Fact]
    public async Task A_member_of_no_kind_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<ArgumentException>(() => StartAsync(
            services => services.AddFaultResponses(),
            app => app.MapDelete("/kindless", () => Kindless().ToHttpResult())));

        refusal.Message.ShouldStartWith("ProbeKindlessFault.Unset: 0 is no FaultKind");
    }

    [Fact]
    public async Task A_status_that_is_no_error_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<ArgumentException>(() => StartAsync(services => services.AddFaultResponses(_ => StatusCodes.Status200OK)));

        refusal.Message.ShouldStartWith("ProbeOrderFault.NotFound is mapped to 200, which is not an error status (400 to 599).");
        refusal.ParamName.ShouldBe("responseOf");
    }

    [Fact]
    public void A_second_default_is_refused() =>
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddFaultResponses().AddFaultResponses(ResponseFor))
              .Message.ShouldStartWith("A mapping for every fault set is given already.");

    [Fact]
    public async Task Without_a_default_a_set_is_unmapped_as_before()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(services => services.AddFaultResponses<ProbeOrderFault>()));

        refusal.Message.ShouldStartWith("No HTTP status is mapped for the fault type ProbeConcurrencyFault, which these endpoints answer with: HTTP: PUT /orders/{id:int}.");
    }

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> services, Action<WebApplication>? endpoints = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        builder.Services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);   // the probe sets share NotFound
        services(builder.Services);

        var app = builder.Build();
        app.MapOpenApi();
        app.MapPut("/orders/{id:int}", (int id) => Rename(id).ToHttpResult());
        endpoints?.Invoke(app);

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

    private static async Task<JsonNode> ProblemAsync(WebApplication app, string path, HttpStatusCode status)
    {
        using var response = await app.GetTestClient().PutAsync(path, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status, path);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    private static JsonNode Response(JsonNode document, string status) => document["paths"]!["/orders/{id}"]!["put"]!["responses"]![status]!;

    private static string[] Statuses(JsonNode document) =>
        [.. document["paths"]!["/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key)];
}
