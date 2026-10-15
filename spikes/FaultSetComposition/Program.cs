using System.Text;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
using FaultSetComposition.Shared;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

string[] modes = args.Length > 0 ? args : ["wire", "document", "typed-document", "unmapped", "side-effect", "middleware"];
foreach (var mode in modes) { if (mode == "middleware") { Console.WriteLine("==> middleware"); await FaultSetComposition.SideEffect.RunAsync(); } else await RunAsync(mode); }
return;

static async Task RunAsync(string mode)
{
    Console.WriteLine($"==> {mode}");
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddOpenApi(options => options.AddTypeKit());

    if (mode != "side-effect")
    {
        builder.Services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);
        if (mode == "typed-document")
        {
            // A problem type per member, so the document's descriptions name each one.
            builder.Services.AddFaultResponses<FaultSetComposition.Union.TeamFault>(f => FaultResponse.For(f.Kind).WithType(Uri(f.QualifiedName)));
            builder.Services.AddFaultResponses<ConcurrencyFault>(f => FaultResponse.For(f.Kind).WithType(Uri(f.QualifiedName)));
            builder.Services.AddFaultResponses<RemovalFault>(f => FaultResponse.For(f.Kind).WithType(Uri(f.QualifiedName)));
        }
        else
        {
            builder.Services.AddFaultResponses<ConcurrencyFault>();
            if (mode != "unmapped") builder.Services.AddFaultResponses<RemovalFault>();
            builder.Services.AddFaultResponses<FaultSetComposition.Union.TeamFault>();
        }
        builder.Services.AddFaultResponses<FaultSetComposition.Baseline.TeamFault>();
        builder.Services.AddFaultResponses<FaultSetComposition.Copy.TeamFault>();
    }

    var app = builder.Build();
    app.MapOpenApi();
    if (mode.StartsWith("side-effect", StringComparison.Ordinal))
    {
        app.MapGet("/other/problem", () => Results.Problem("A problem the host writes itself.", statusCode: 409));
        app.MapGet("/other/validation", () => Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Required."] }));
        app.MapGet("/other/bad-request", () => Results.BadRequest());
    }
    else
    {
        FaultSetComposition.Baseline.TeamEndpoints.Map(app);
        FaultSetComposition.Copy.TeamEndpoints.Map(app);
        FaultSetComposition.Union.TeamEndpoints.Map(app);
    }

    try
    {
        await app.StartAsync();
    }
    catch (Exception refused)
    {
        Console.WriteLine($"  start refused: {refused.GetType().Name}: {refused.Message}");
        return;
    }

    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    switch (mode)
    {
        case "wire" or "unmapped":
            foreach (var shape in new[] { "baseline", "copy", "union" })
            {
                await Show(client, "POST", $"/{shape}/teams?outcome=Raced", """{"name":"Ann"}""");
                await Show(client, "PUT", $"/{shape}/teams/{Guid.NewGuid()}?outcome=Stale", """{"name":"Ann"}""");
                await Show(client, "PUT", $"/{shape}/teams/{Guid.NewGuid()}", """{"name":"taken"}""");
                await Show(client, "DELETE", $"/{shape}/teams/{Guid.NewGuid()}?outcome=Referenced", null);
            }
            break;
        case "document" or "typed-document":
            var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
            foreach (var (path, item) in document["paths"]!.AsObject())
            foreach (var (method, operation) in item!.AsObject())
            {
                var responses = operation!["responses"]!.AsObject().Select(r => mode == "document" ? r.Key : $"{r.Key} [{r.Value!["description"]}]");
                Console.WriteLine($"  {method.ToUpperInvariant(),-6} {path,-24} {string.Join(mode == "document" ? " " : "\n" + new string(' ', 34), responses)}");
            }
            break;
        case "side-effect":
            foreach (var path in new[] { "/other/problem", "/other/validation", "/other/bad-request" }) await Show(client, "GET", path, null);
            await app.StopAsync();
            await RunWithFaultResponses();
            break;
    }

    await app.StopAsync();
}

// The same three routes in a host that also calls AddFaultResponses.
static async Task RunWithFaultResponses()
{
    Console.WriteLine("  -- the same host, after services.AddFaultResponses<ConcurrencyFault>():");
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddFaultResponses<ConcurrencyFault>();
    var app = builder.Build();
    app.MapGet("/other/problem", () => Results.Problem("A problem the host writes itself.", statusCode: 409));
    app.MapGet("/other/validation", () => Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Required."] }));
    app.MapGet("/other/bad-request", () => Results.BadRequest());
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    foreach (var path in new[] { "/other/problem", "/other/validation", "/other/bad-request" }) await Show(client, "GET", path, null);
    await app.StopAsync();
}

static async Task Show(HttpClient client, string method, string path, string? body)
{
    var request = new HttpRequestMessage(new HttpMethod(method), path);
    if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"  {method,-6} {path.Split('?')[0][..Math.Min(path.Split('?')[0].Length, 22)],-22} {(path.Contains('?') ? path[path.IndexOf('?')..] : ""),-18} {(int)response.StatusCode} {text}");
}

static Uri Uri(string name) => new($"https://problems.example/{name}");
