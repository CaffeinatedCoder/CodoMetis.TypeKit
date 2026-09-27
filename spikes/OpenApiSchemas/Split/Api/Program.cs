using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.TestHost;

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Logging.ClearProviders();
await using var app = builder.Build();

app.MapGet("/domain/{id}", (SplitId id) => id.Value);   // declared in Domain: woven when this project compiles
#if PRAGMA
#pragma warning disable ASP0020 // LocalId is parsable once woven; the route analyzer sees it unwoven
#endif
app.MapGet("/local/{id}", (LocalId id) => id.Value);    // declared here: unwoven when this project's analyzers run
#if PRAGMA
#pragma warning restore ASP0020
#endif
app.MapGet("/local", (LocalId id) => id.Value);         // the same type as a query parameter

int unused; // CS0168: the suppressor's control

await app.StartAsync();
var client = app.GetTestClient();
foreach (var path in new[] { "/domain/6f9619ff-8b86-d011-b42d-00cf4fc964ff", "/local/6f9619ff-8b86-d011-b42d-00cf4fc964ff", "/local/nope", "/local?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff" })
{
    var response = await client.GetAsync(path);
    Console.WriteLine($"{path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
}

public readonly partial record struct LocalId : IValue<Guid>;
