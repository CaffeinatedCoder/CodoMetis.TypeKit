using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using ValidationFaults;

Directory.CreateDirectory("out");
string[] setups = args.Length > 0 ? args : ["bare", "problem", "statuspages", "validation", "throw", "bridge"];
foreach (var setup in setups) await RunAsync(setup);
return;

static async Task RunAsync(string setup)
{
    Console.WriteLine($"==> {setup}");
    var hooks = new List<string>();

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
    {
        var inner = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context =>
        {
            foreach (var (key, entry) in context.ModelState.Where(e => e.Value!.Errors.Count > 0))
            foreach (var error in entry!.Errors)
                hooks.Add($"mvc model state [{key}] message='{error.ErrorMessage}' exception={Describe(error.Exception)}");
            return inner(context);
        };
    });
    if (setup is "problem" or "statuspages" or "validation" or "throw" or "bridge")
    {
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            hooks.Add($"customize {context.HttpContext.Request.Path} status={context.ProblemDetails.Status} exception={Describe(context.Exception)} " +
                      $"feature={Describe(context.HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error)}"));
    }
    if (setup == "validation") builder.Services.AddValidation();
    if (setup is "throw" or "bridge")
    {
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddSingleton<IExceptionHandler>(new RecordingHandler(hooks));
    }
    if (setup == "bridge") builder.Services.AddExceptionHandler<BadRequestBridge>();

    var app = builder.Build();
    if (setup is "throw" or "bridge") app.UseExceptionHandler();
    if (setup == "statuspages") app.UseStatusCodePages();
    app.MapPost("/signup", (Signup signup) => signup.Email.Value);
    app.MapPost("/probe", (ProbeBody body) => body.Probe.Text);
    app.MapGet("/quantities/{seats}", (Quantity seats) => seats.Value);
    app.MapGet("/search", (Quantity seats) => seats.Value);
    app.MapControllers();
    await app.StartAsync();

    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    (string Method, string Path, string? Body)[] requests =
    [
        ("POST", "/signup", """{"email":"nobody","name":"Ann","seats":1}"""),
        ("POST", "/signup", """{"email":"a@b.test","name":"Ann","seats":0}"""),
        ("POST", "/signup", """{"email":"a@b.test","name":"A","seats":1}"""),
        ("POST", "/probe", """{"probe":"nobody"}"""),
        ("GET", "/quantities/0", null),
        ("GET", "/quantities/abc", null),
        ("GET", "/search?seats=0", null),
        ("POST", "/mvc/signup", """{"email":"nobody","name":"Ann","seats":1}"""),
        ("POST", "/mvc/signup", """{"email":"a@b.test","name":"A","seats":1}"""),
        ("POST", "/mvc/probe", """{"probe":"nobody"}"""),
        ("GET", "/mvc/quantities/0", null),
        ("GET", "/mvc/search?seats=0", null),
    ];
    foreach (var (method, path, body) in requests)
    {
        hooks.Clear();
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"  {method} {path} {body} -> {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType} {text}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  {method} {path} -> client {e.GetType().Name}: {e.Message}");
        }
        foreach (var hook in hooks) Console.WriteLine($"      hook: {hook}");
    }

    await app.StopAsync();
    await app.DisposeAsync();
}

static string Describe(Exception? exception)
{
    var parts = new List<string>();
    for (var e = exception; e is not null; e = e.InnerException)
    {
        var path = e is JsonException json ? $" path={json.Path}" : "";
        var fault = e.Data[ProbeConverter.Marker] is { } f ? $" fault={f}" : "";
        parts.Add($"{e.GetType().Name}{path}{fault} '{e.Message}'");
    }
    return parts.Count == 0 ? "none" : string.Join(" <- ", parts);
}

sealed class RecordingHandler(List<string> hooks) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        hooks.Add($"exception handler {Describe(exception)}");
        return ValueTask.FromResult(false);
    }

    static string Describe(Exception? exception)
    {
        var parts = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}{(e is JsonException json ? $" path={json.Path}" : "")}{(e.Data[ProbeConverter.Marker] is { } f ? $" fault={f}" : "")}");
        return string.Join(" <- ", parts);
    }
}

[ApiController]
public sealed class SignupController : ControllerBase
{
    [HttpPost("/mvc/signup")] public string Post(Signup signup) => signup.Email.Value;
    [HttpPost("/mvc/probe")] public string Probe(ProbeBody body) => body.Probe.Text;
    [HttpGet("/mvc/quantities/{seats}")] public int Get(Quantity seats) => seats.Value;
    [HttpGet("/mvc/search")] public int Search([FromQuery] Quantity seats) => seats.Value;
}
