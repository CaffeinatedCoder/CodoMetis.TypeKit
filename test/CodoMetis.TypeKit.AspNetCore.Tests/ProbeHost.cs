using System.Collections.Concurrent;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// An in-memory host serving the probe API and its OpenAPI document, with minimal APIs and MVC
/// controllers side by side.
/// </summary>
internal sealed class ProbeHost : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<OpenApiSpecVersion, Task<OpenApiJson>> Defaults = new();

    private readonly WebApplication _app;

    private ProbeHost(WebApplication app) => _app = app;

    public HttpClient Client => _app.GetTestClient();

    /// <summary>The document with <c>AddTypeKit()</c> and nothing else configured, built once per version.</summary>
    public static Task<OpenApiJson> DefaultDocumentAsync(OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1) =>
        Defaults.GetOrAdd(version, static v => DocumentAsync(openApi => openApi.AddTypeKit(), version: v));

    public static async Task<OpenApiJson> DocumentAsync(
        Action<OpenApiOptions>? openApi = null,
        Action<JsonOptions>? json = null,
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1,
        Action<WebApplication>? endpoints = null)
    {
        await using var host = await StartAsync(openApi, json, version, endpoints);
        return await host.DocumentAsync();
    }

    public static async Task<ProbeHost> StartAsync(
        Action<OpenApiOptions>? openApi = null,
        Action<JsonOptions>? json = null,
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1,
        Action<WebApplication>? endpoints = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
        if (json is not null) builder.Services.ConfigureHttpJsonOptions(json);
        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = version;
            openApi?.Invoke(options);
        });

        var app = builder.Build();
        app.MapOpenApi();
        app.MapControllers();
        MapProbes(app);
        endpoints?.Invoke(app);

        await app.StartAsync();
        return new ProbeHost(app);
    }

    public async Task<OpenApiJson> DocumentAsync() =>
        new(JsonNode.Parse(await Client.GetStringAsync("/openapi/v1.json"))!);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static void MapProbes(WebApplication app)
    {
        app.MapGet("/probes/{id}", ProbeDocument (ProbeId id) => throw new NotSupportedException());
        app.MapPost("/probes", ProbeId (ProbeDocument document) => document.Id);
        app.MapGet("/probes", int (ProbeMoment since, ProbeCount? limit, ProbeCode? code, [FromQuery] ProbeId[] ids) => ids.Length);
        app.MapGet("/probes/search", int ([AsParameters] ProbeSearch search) => 0);
        app.MapGet("/probes/header", int ([FromHeader(Name = "X-Probe")] ProbeId probe) => 0);
        app.MapPost("/probes/numbers", ProbeNumbers (ProbeNumbers numbers) => numbers);

        app.MapGet("/controls/{id}", ControlDocument (Guid id) => throw new NotSupportedException());
        app.MapGet("/controls", int (DateTimeOffset since, int? limit, string? code, [FromQuery] Guid[] ids) => ids.Length);
        app.MapGet("/controls/search", int ([AsParameters] ControlSearch search) => 0);
        app.MapGet("/controls/header", int ([FromHeader(Name = "X-Probe")] Guid probe) => 0);
    }
}

/// <summary>An emitted OpenAPI document, with the lookups the tests need.</summary>
internal sealed class OpenApiJson(JsonNode root)
{
    public JsonNode Root => root;

    public JsonObject Components => root["components"]?["schemas"]?.AsObject() ?? [];

    public JsonNode Component(string name) =>
        Components[name] ?? throw new InvalidOperationException($"No component '{name}'. Components: {string.Join(", ", Components.Select(c => c.Key))}");

    public JsonNode Property(string component, string property) =>
        Component(component)["properties"]?[property] ?? throw new InvalidOperationException($"'{component}' has no property '{property}'.");

    /// <summary>The schema a reference points at, or the schema itself.</summary>
    public JsonNode Resolve(JsonNode schema) =>
        schema["$ref"] is { } reference ? Component(reference.GetValue<string>().Split('/')[^1]) : schema;

    public JsonNode Parameter(string path, string method, string name) =>
        Operation(path, method)["parameters"]?.AsArray().SingleOrDefault(p => p!["name"]!.GetValue<string>() == name)?["schema"]
     ?? throw new InvalidOperationException($"{method} {path} has no parameter '{name}'.");

    public JsonNode Request(string path, string method) =>
        Operation(path, method)["requestBody"]!["content"]!["application/json"]!["schema"]!;

    public JsonNode Response(string path, string method) =>
        Operation(path, method)["responses"]!["200"]!["content"]!["application/json"]!["schema"]!;

    private JsonNode Operation(string path, string method) =>
        root["paths"]?[path]?[method] ?? throw new InvalidOperationException($"No operation {method} {path}.");
}

internal static class SchemaAssertions
{
    /// <summary>Structural equality, reporting both schemas when they differ.</summary>
    public static void ShouldDescribeTheSameAs(this JsonNode actual, JsonNode expected, string? context = null)
    {
        if (!JsonNode.DeepEquals(actual, expected))
            throw new ShouldAssertException($"{context}{(context is null ? "" : ": ")}expected {expected.ToJsonString()}, but was {actual.ToJsonString()}");
    }
}
