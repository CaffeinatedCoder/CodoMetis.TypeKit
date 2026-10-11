using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
#if !AOT
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
#endif
using ResultHttp;

Directory.CreateDirectory("out");
var flavour = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot";
var mvc = args.Contains("--mvc");
if (mvc) flavour += "-mvc";
if (args.Contains("--a-only")) flavour += "-a";
if (args.Contains("--result-in-context")) flavour += "-ctx";

await RunAsync("mapped", mapFaults: true);
await RunAsync("unmapped", mapFaults: false);
return;

async Task RunAsync(string name, bool mapFaults)
{
    Console.WriteLine($"==> {flavour}/{name}");
    var failures = new List<string>();

    var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.ConfigureHttpJsonOptions(json =>
    {
        json.SerializerOptions.TypeInfoResolverChain.Insert(0, SpikeJson.Default);
        if (args.Contains("--result-in-context")) json.SerializerOptions.TypeInfoResolverChain.Insert(0, ResultJson.Default);
    });
    builder.Services.AddOpenApi(openApi => openApi.AddOperationTransformer<FaultResponsesOperationTransformer>());
    if (!args.Contains("--no-check")) builder.Services.AddTransient<IStartupFilter, FaultResponsesStartupCheck>();
    if (mapFaults)
    {
        builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound => StatusCodes.Status404NotFound,
            OrderFault.AlreadyShipped => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status403Forbidden,
        });
    }
#if !AOT
    if (mvc) builder.Services.AddControllers().AddApplicationPart(typeof(OrdersController).Assembly);
#endif

    WebApplication app;
    try
    {
        app = builder.Build();
    }
    catch (Exception e)
    {
        Console.WriteLine($"  build threw {e.GetType().Name}: {e.Message}");
        return;
    }

    app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (Exception e) { failures.Add($"{context.Request.Method} {context.Request.Path}: {e.GetType().Name}: {e.Message}"); throw; }
    });
    app.MapOpenApi();
    app.MapGet("/a/orders/{id:int}", (int id) => Orders.Find(id).ToHttpResult());
    app.MapGet("/a/orders-async/{id:int}", async (int id) => (await Orders.FindAsync(id)).ToHttpResult());
    app.MapDelete("/a/orders/{id:int}", (int id) => Orders.Cancel(id).ToHttpResult());
    var shapeB = !args.Contains("--a-only");
    if (shapeB)
    {
        app.MapGet("/b/orders/{id:int}", (int id) => Orders.Find(id)).AddEndpointFilter(ResultFilter.For<Order, OrderFault>());
        app.MapGet("/raw/orders/{id:int}", (int id) => Orders.Find(id));
    }
#if !AOT
    if (mvc) app.MapControllers();
#endif

    try
    {
        await app.StartAsync();
    }
    catch (Exception e)
    {
        Console.WriteLine($"  start threw {e.GetType().Name}: {e.Message}");
        return;
    }

    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    var requests = new List<(HttpMethod, string)>
    {
        (HttpMethod.Get, "/a/orders/1"), (HttpMethod.Get, "/a/orders/2"), (HttpMethod.Get, "/a/orders/3"), (HttpMethod.Get, "/a/orders/9"),
        (HttpMethod.Get, "/a/orders-async/2"),
        (HttpMethod.Delete, "/a/orders/1"), (HttpMethod.Delete, "/a/orders/2"),
    };
    if (shapeB) requests.AddRange([(HttpMethod.Get, "/b/orders/1"), (HttpMethod.Get, "/b/orders/9"), (HttpMethod.Get, "/raw/orders/1")]);
#if !AOT
    if (mvc) requests.AddRange([(HttpMethod.Get, "/mvc/orders/1"), (HttpMethod.Get, "/mvc/orders/2")]);
#endif
    foreach (var (method, path) in requests)
    {
        try
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, path));
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"  {method} {path} -> {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType} {body}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  {method} {path} -> client {e.GetType().Name}: {e.Message}");
        }
    }

    using (var response = await client.GetAsync("/openapi/v1.json"))
    {
        var text = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"  document -> {(int)response.StatusCode}");
        if (response.IsSuccessStatusCode)
        {
            await File.WriteAllTextAsync($"out/{flavour}-{name}.json", text);
            PrintResponses(JsonNode.Parse(text)!);
        }
    }

    foreach (var failure in failures) Console.WriteLine($"  server threw {failure}");
    await app.StopAsync();
    await app.DisposeAsync();
}

static void PrintResponses(JsonNode document)
{
    foreach (var (path, item) in document["paths"]!.AsObject())
    foreach (var (method, operation) in item!.AsObject())
    {
        var responses = operation!["responses"]!.AsObject().Select(r =>
        {
            var content = r.Value!["content"]?.AsObject();
            var shape = content is null ? "no content" : string.Join(", ", content.Select(c => $"{c.Key} {c.Value!["schema"]?.ToJsonString()}"));
            return $"{r.Key}: {shape}";
        });
        Console.WriteLine($"  doc {method.ToUpperInvariant()} {path}");
        foreach (var response in responses) Console.WriteLine($"      {response}");
    }
}

[JsonSerializable(typeof(Order))]
internal partial class SpikeJson : JsonSerializerContext;

// What shape B asks of a Native AOT host: the result itself in its context.
[JsonSerializable(typeof(CodoMetis.TypeKit.Result<Order, OrderFault>))]
internal partial class ResultJson : JsonSerializerContext;

#if !AOT
[ApiController]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("/mvc/orders/{id:int}")]
    public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Orders.Find(id).ToHttpResult();
}
#endif
