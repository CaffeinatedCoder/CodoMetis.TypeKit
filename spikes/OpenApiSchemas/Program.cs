using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.OpenApi;

var roundTrip = true;
var stringEnums = false;
var nodaHost = false;
string[] valueObjects = ["OrderId", "Quantity", "Price", "Code", "ShipDate", "Since", "Day", "Note", "Stamp", "TicketId"];

var modes = args.Where(a => !a.StartsWith("--")).ToArray() is { Length: > 0 } chosen
    ? chosen
    : ["baseline", "observe", "candidate", "candidate+description", "consumer-first", "consumer-last", "string-enums", "noda-plain", "noda-host"];

if (args.Contains("--extensions"))
{
    foreach (var type in typeof(OpenApiSchema).Assembly.GetExportedTypes().Where(t => typeof(IOpenApiExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
        Console.WriteLine($"  {type.FullName}: {string.Join(" | ", type.GetConstructors().Select(c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name))))}");
    return;
}
if (args.Contains("--props"))
{
    foreach (var property in typeof(OpenApiSchema).GetProperties().Where(p => p.CanWrite).OrderBy(p => p.Name))
        Console.WriteLine($"  {property.Name}: {ObservingTransformer.Name(property.PropertyType)} = {property.GetValue(new OpenApiSchema()) ?? "null"}");
    return;
}
var versions = args.Contains("--v30") ? [OpenApiSpecVersion.OpenApi3_0] : new[] { OpenApiSpecVersion.OpenApi3_1 };

Directory.CreateDirectory("out");

foreach (var version in versions)
foreach (var mode in modes)
{
    var visits = new List<string>();
    stringEnums = mode == "string-enums";
    nodaHost = mode == "noda-host";
    var json = await Generate(version, mode: mode, configure: options =>
    {
        switch (mode)
        {
            case "baseline": break;
            case "observe": options.AddSchemaTransformer(new ObservingTransformer(visits)); break;
            case "candidate": options.AddSchemaTransformer(new CandidateTransformer(true, true, passParameterDescription: false)); break;
            case "consumer-first":
                options.AddSchemaTransformer(new ConsumerInstantTransformer());
                options.AddSchemaTransformer(new CandidateTransformer(true, true, true));
                break;
            case "consumer-last":
                options.AddSchemaTransformer(new CandidateTransformer(true, true, true));
                options.AddSchemaTransformer(new ConsumerInstantTransformer());
                break;
            default: // candidate+description, string-enums, noda-plain, noda-host: the candidate as it would ship
                options.AddSchemaTransformer(new CandidateTransformer(true, true, true));
                break;
        }
    });

    var file = $"out/{mode}-{version}.json";
    await File.WriteAllTextAsync(file, json);
    Console.WriteLine($"=== {mode} / {version}  ({file})");
    Summarize(JsonNode.Parse(json)!);
    if (CandidateTransformer.Notes.Count > 0)
    {
        Console.WriteLine("  -- candidate notes");
        foreach (var note in CandidateTransformer.Notes.Distinct()) Console.WriteLine($"  {note}");
        CandidateTransformer.Notes.Clear();
    }
    if (visits.Count > 0 && args.Contains("--visits"))
    {
        Console.WriteLine("  -- transformer visits");
        foreach (var visit in visits) Console.WriteLine($"  {visit}");
    }
    Console.WriteLine();
}

async Task<string> Generate(OpenApiSpecVersion version, Action<OpenApiOptions> configure, string mode)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseTestServer();
    builder.Logging.ClearProviders();
    builder.Services.AddControllers().AddApplicationPart(typeof(TicketsController).Assembly);
    if (nodaHost)
        builder.Services.ConfigureHttpJsonOptions(o => NodaTime.Serialization.SystemTextJson.Extensions.ConfigureForNodaTime(o.SerializerOptions, NodaTime.DateTimeZoneProviders.Tzdb));
    if (stringEnums)
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
    builder.Services.AddOpenApi(options =>
    {
        options.OpenApiVersion = version;
        configure(options);
    });

    await using var app = builder.Build();
    app.MapOpenApi();
    app.MapControllers();
    app.MapGet("/orders/{id}", (OrderId id) => Sample(id));
    app.MapGet("/orders", (Since since, Code? code) => new List<OrderDto>());
    app.MapGet("/orders/{id}/id", (OrderId id) => id);
    app.MapPost("/orders", (OrderDto order) => Results.NoContent());
    app.MapGet("/orders/by-ids", ([Microsoft.AspNetCore.Mvc.FromQuery] OrderId[] ids) => ids.Length);
    app.MapPost("/numbers", (NumbersDto numbers) => numbers);
    app.MapPost("/day", (DayDto day) => day);
    app.MapPost("/stamp", (StampDto stamp) => stamp);
    app.MapGet("/search", ([AsParameters] SearchQuery query) => 0);
    app.MapGet("/control/{id}", (Guid id, int count, long? parent, DateTimeOffset at, DayOfWeek day) => 0);

    await app.StartAsync();
    var client = app.GetTestClient();
    if (mode is "noda-host" or "noda-plain")
    {
        var response = await client.PostAsync("/stamp", new StringContent("{\"stamp\":\"2026-09-27T10:00:00Z\",\"raw\":\"2026-09-27T10:00:00Z\"}", System.Text.Encoding.UTF8, "application/json"));
        Console.WriteLine($"  noda round trip -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    if (stringEnums)
    {
        var response = await client.PostAsync("/day", new StringContent("{\"day\":\"Monday\"}", System.Text.Encoding.UTF8, "application/json"));
        Console.WriteLine($"  string enum round trip -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    var json = await client.GetStringAsync("/openapi/v1.json");
    if (roundTrip)
    {
        roundTrip = false;
        foreach (var body in new[] { """{"count":"5","price":"1.5","ticket":"7"}""", """{"count":5,"price":1.5,"ticket":7}""", """{"count":"abc","price":1.5,"ticket":7}""" })
        {
            var response = await client.PostAsync("/numbers", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            var text = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"  round trip {body} -> {(int)response.StatusCode} {(text.Length > 160 ? text[..160] : text)}");
        }
        foreach (var query in new[] { "/orders/by-ids?ids=6f9619ff-8b86-d011-b42d-00cf4fc964ff&ids=7f9619ff-8b86-d011-b42d-00cf4fc964ff", "/orders/by-ids?ids=nope" })
        {
            var response = await client.GetAsync(query);
            Console.WriteLine($"  query {query} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }
    await app.StopAsync();
    return json;
}

static OrderDto Sample(OrderId id) => throw new NotSupportedException("document generation only");

void Summarize(JsonNode document)
{
    var schemas = document["components"]?["schemas"]?.AsObject();
    Console.WriteLine("  -- components");
    foreach (var name in valueObjects)
        Console.WriteLine($"  {name,-10} {(schemas?[name] is { } s ? Compact(s) : "(no component)")}");

    foreach (var dto in new[] { "OrderDto", "TicketDto" })
    {
        Console.WriteLine($"  -- {dto}.properties");
        if (schemas?[dto]?["properties"]?.AsObject() is not { } properties) { Console.WriteLine("  (none)"); continue; }
        foreach (var (name, schema) in properties) Console.WriteLine($"  {name,-12} {Compact(schema!)}");
    }

    Console.WriteLine("  -- parameters and top-level bodies");
    foreach (var (path, item) in document["paths"]!.AsObject())
    foreach (var (verb, operation) in item!.AsObject())
    {
        foreach (var parameter in operation!["parameters"]?.AsArray() ?? [])
            Console.WriteLine($"  {verb} {path} param {parameter!["name"]} ({parameter["in"]}): {Compact(parameter["schema"]!)}");

        if (operation["requestBody"]?["content"]?["application/json"]?["schema"] is { } body)
            Console.WriteLine($"  {verb} {path} body: {Compact(body)}");

        if (operation["responses"]?["200"]?["content"]?["application/json"]?["schema"] is { } response)
            Console.WriteLine($"  {verb} {path} 200: {Compact(response)}");
    }
}

static string Compact(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
