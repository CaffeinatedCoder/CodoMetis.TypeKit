using System.Net;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Web.Faults;

// sample: CodoMetis.TypeKit.AspNetCore/fault-composed
using Refusal = Fault<AccountFault, EditFault>;   // the boundary's own faults, beside the shared ones

// end sample
// sample: CodoMetis.TypeKit.AspNetCore/fault-shared-set
public sealed partial class EditFault : IFaultSet<EditFault>
{
    public static readonly EditFault Changed = new(FaultKind.Conflict, "Someone changed it meanwhile; read it again.");
    public static readonly EditFault Stale = new(FaultKind.PreconditionFailed, null);

    private EditFault(FaultKind kind, string? description) { Kind = kind; Description = description; }

    public FaultKind Kind { get; }
    public string? Description { get; }
}

public static class Edits   // decides these refusals once, for every boundary
{
    public static Fault<EditFault> Stale(int current, int sent) => Fault.Of(EditFault.Stale, $"It is at version {current}, not {sent}.");
}
// end sample

public sealed record VersionedAccount(int Id, int Version);

public sealed class AccountEdits(Func<int, VersionedAccount?> find)
{
    private VersionedAccount? Find(int id) => find(id);

    // sample: CodoMetis.TypeKit.AspNetCore/fault-composed
    public Result<Refusal> Close(int id, int version) =>
        Find(id) is not { } account  ? Refusal.Of(AccountFault.NotFound)
        : account.Version != version ? Refusal.Of(Edits.Stale(account.Version, version))   // the shared fault, as it is
        : Result<Refusal>.Success();
    // end sample
}

public static class ComposedFaults
{
    public static void Responses(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-composed-host
        builder.Services.AddFaultResponses<EditFault>();   // once for the host, beside AccountFault

        // end sample
    }

    public static void Endpoints(WebApplication app, AccountEdits edits)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-composed-host
        app.MapDelete("/accounts/{id:int}", (int id, int version) => edits.Close(id, version).ToHttpResult());
        // end sample
    }
}

/// <summary>
/// What the README's "Faults of several types" says: each fault answered as its own set maps it, and the
/// operation listing both sets' statuses.
/// </summary>
public sealed class ComposedFaultSampleTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        Faults.Responses(builder);
        ComposedFaults.Responses(builder);

        var app = builder.Build();
        app.MapOpenApi();
        ComposedFaults.Endpoints(app, new AccountEdits(id => id == 7 ? new VersionedAccount(7, 3) : null));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Fact]
    public async Task Each_fault_is_answered_as_its_own_set_maps_it()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var closed = await client.DeleteAsync("/accounts/7?version=3", TestContext.Current.CancellationToken);
        closed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var missing = await client.DeleteAsync("/accounts/8?version=3", TestContext.Current.CancellationToken);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonNode.Parse(await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["fault"]!.GetValue<string>().ShouldBe("NotFound");

        using var stale = await client.DeleteAsync("/accounts/7?version=2", TestContext.Current.CancellationToken);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var problem = JsonNode.Parse(await stale.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        problem["fault"]!.GetValue<string>().ShouldBe("Stale");
        problem["detail"]!.GetValue<string>().ShouldBe("It is at version 3, not 2.");
    }

    [Fact]
    public async Task The_document_lists_the_statuses_of_both_sets()
    {
        await using var app = await StartAsync();

        var paths = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!["paths"]!;

        paths["/accounts/{id}"]!["delete"]!["responses"]!.AsObject().Select(response => response.Key).ShouldBe(["204", "403", "404", "409", "412"]);
    }
}
