using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.AspNetCore;
using MergePatch;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.OpenApi;
using System.Text.Json.Serialization.Metadata;

Directory.CreateDirectory("out");
var flavour  = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot";
var failures = 0;

void Claim(int number, string text, bool holds)
{
    Console.WriteLine($"{(holds ? "PASS" : "FAIL")} {number,2} {text}");
    if (!holds) failures++;
}

static string State<T>(PatchA<T> patch) => patch.Match(v => $"Value({v})", () => "Null", () => "Absent");

// ---- claims 1, 2, 4, 5: reading, under the reflection resolver and the source-generated context
var resolvers = new List<(string Name, JsonSerializerOptions Options)> { ("source-generated", new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SpikeJson.Default }) };
#if !AOT
resolvers.Insert(0, ("reflection", new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
#endif
foreach (var (name, options) in resolvers)
{

    UpdateA Read(string json) => JsonSerializer.Deserialize(json, (JsonTypeInfo<UpdateA>)options.GetTypeInfo(typeof(UpdateA)))!;
    string Write<T>(T value) => JsonSerializer.Serialize(value, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));

    var absent = Read("""{}""");
    Claim(1, $"[{name}] every property absent reads as Absent (default): {State(absent.Name)}, {State(absent.Email)}, {State(absent.Quantity)}, {State(absent.Address)}",
        absent.Name.State == PatchState.Absent && absent.Email.State == PatchState.Absent && absent.Quantity.State == PatchState.Absent && absent.Address.State == PatchState.Absent);

    var nulls = Read("""{"name":null,"email":null,"quantity":null,"address":null}""");
    Claim(2, $"[{name}] null reads as Null for every T, the value object included: {State(nulls.Name)}, {State(nulls.Email)}, {State(nulls.Quantity)}, {State(nulls.Address)}",
        nulls.Name.State == PatchState.Null && nulls.Email.State == PatchState.Null && nulls.Quantity.State == PatchState.Null && nulls.Address.State == PatchState.Null);

    try
    {
        var values = Read("""{"name":"Ada","email":"ada@example.com","quantity":3,"address":{"street":"1 Main","city":"Oslo"}}""");
        Claim(2, $"[{name}] a value reads as Value, a primitive and a class under the context's own metadata, the value object under its converter: {State(values.Name)}, {State(values.Email)}, {State(values.Quantity)}, {State(values.Address)}",
            values.Name.TryGetValue(out var n) && n == "Ada" && values.Email.TryGetValue(out var e) && e.Value == "ada@example.com"
         && values.Quantity.TryGetValue(out var q) && q == 3 && values.Address.TryGetValue(out var a) && a.City == "Oslo");
    }
    catch (Exception e)
    {
        Claim(4, $"[{name}] T's contract: {e.GetType().Name}: {e.Message[..Math.Min(160, e.Message.Length)]}", false);
    }

    try
    {
        Read("""{"email":"not-an-address"}""");
        Claim(5, $"[{name}] a refused value object inside a patch is refused", false);
    }
    catch (JsonException e)
    {
        Claim(5, $"[{name}] a refused value object inside a patch throws the value object's own JsonException, naming no input: {e.Message[..Math.Min(120, e.Message.Length)]}", e.Message.Contains("Email") && !e.Message.Contains("not-an-address"));
    }

    // ---- claim 7: writing
    var outgoing = Write(new OutgoingA(PatchA<string?>.Null(), PatchA<int>.Absent()));
    Claim(7, $"[{name}] Absent is left out with WhenWritingDefault, Null writes null: {outgoing}", outgoing == """{"name":null}""");
    var written = Write(new OutgoingA(PatchA<string?>.Of("Ada"), PatchA<int>.Of(2)));
    Claim(7, $"[{name}] Value writes T: {written}", written == """{"name":"Ada","quantity":2}""");
    try
    {
        Write(new BareOutgoingA(PatchA<string?>.Absent()));
        Claim(7, $"[{name}] writing an Absent patch without the ignore condition is refused", false);
    }
    catch (NotSupportedException e)
    {
        Claim(7, $"[{name}] writing an Absent patch without the ignore condition is refused, naming the condition: {e.Message[..60]}…", e.Message.Contains("WhenWritingDefault"));
    }
}

// ---- claim 3 variant: the factory without MakeGenericType, reading the struct's static field
try
{
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SpikeJson.Default };
    options.Converters.Insert(0, new PatchAStaticFieldFactory());
    var read = JsonSerializer.Deserialize("""{"quantity":5,"email":"a@b.c"}""", (JsonTypeInfo<UpdateA>)options.GetTypeInfo(typeof(UpdateA)))!;
    Claim(3, $"static-field factory (no MakeGenericType): {State(read.Quantity)}, {State(read.Email)}", read.Quantity.TryGetValue(out var q) && q == 5 && read.Email.State == PatchState.Value);
}
catch (Exception e)
{
    Claim(3, $"static-field factory (no MakeGenericType): {e.GetType().Name}: {e.Message[..Math.Min(140, e.Message.Length)]}", false);
}

#if OPEN_GENERIC
try
{
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SpikeJson.Default };
    var b = JsonSerializer.Deserialize("""{"name":"Ada","quantity":null}""", (JsonTypeInfo<UpdateB>)options.GetTypeInfo(typeof(UpdateB)))!;
    Claim(4, $"shape B, an open generic converter in [JsonConverter]: {b.Name.State}, {b.Quantity.State}", b.Name.State == PatchState.Value && b.Quantity.State == PatchState.Null);
}
catch (Exception e)
{
    Claim(4, $"shape B, an open generic converter in [JsonConverter]: {e.GetType().Name}: {e.Message[..Math.Min(160, e.Message.Length)]}", false);
}
#endif

// ---- claims 5 (HTTP), 8: a host
foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0 })
foreach (var transform in new[] { false, true })
{
    var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Insert(0, SpikeJson.Default));
    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi(openApi =>
    {
        openApi.OpenApiVersion = version;
        openApi.AddTypeKit();
        if (transform)
        {
            var inner = openApi.CreateSchemaReferenceId;
            openApi.CreateSchemaReferenceId = type => PatchSchemaTransformer.IsPatch(type.Type) ? null : inner(type);
            openApi.AddSchemaTransformer<PatchSchemaTransformer>();
        }
    });

    await using var app = builder.Build();
    app.UseStatusCodePages();
    app.MapOpenApi();
    app.MapPatch("/orders/{id}", (int id, UpdateA update) => Results.Ok(new PatchStates(State(update.Name), State(update.Email), State(update.Quantity), State(update.Address))));
    await app.StartAsync();

    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    if (version == OpenApiSpecVersion.OpenApi3_1 && !transform)
    {
        var ok = await client.PatchAsync("/orders/1", new StringContent("""{"name":"Ada","quantity":null}""", Encoding.UTF8, "application/json"));
        var body = await ok.Content.ReadAsStringAsync();
        Claim(5, $"HTTP: a patch body binds through the context: {(int)ok.StatusCode} {body}", ok.StatusCode == HttpStatusCode.OK && body.Contains("Value(Ada)") && body.Contains("\"quantity\":\"Null\"") && body.Contains("\"email\":\"Absent\""));

        var refused = await client.PatchAsync("/orders/1", new StringContent("""{"email":"nope"}""", Encoding.UTF8, "application/json"));
        var problem = await refused.Content.ReadAsStringAsync();
        Claim(5, $"HTTP: a refused value object inside a patch is 400, the input not quoted: {(int)refused.StatusCode} {problem[..Math.Min(100, problem.Length)]}", refused.StatusCode == HttpStatusCode.BadRequest && !problem.Contains("nope"));
    }

    var response = await client.GetAsync("/openapi/v1.json");
    var document = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) { Claim(8, $"document {version} transform={transform}: {(int)response.StatusCode} {document[..Math.Min(400, document.Length)]}", false); await app.StopAsync(); continue; }
    var file = $"out/{flavour}-{(version == OpenApiSpecVersion.OpenApi3_1 ? "3.1" : "3.0")}-{(transform ? "transformed" : "plain")}.json";
    await File.WriteAllTextAsync(file, document);

    var schema = JsonNode.Parse(document)!["components"]?["schemas"]?["UpdateA"];
    var props = schema?["properties"];
    var required = schema?["required"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];
    var nameSchema = props?["name"]?.ToJsonString();
    var emailSchema = props?["email"]?.ToJsonString();
    var quantitySchema = props?["quantity"]?.ToJsonString();
    Console.WriteLine($"     [{Path.GetFileName(file)}] required=[{string.Join(",", required)}] name={nameSchema} email={emailSchema} quantity={quantitySchema}");
    if (transform)
    {
        var nullable = version == OpenApiSpecVersion.OpenApi3_1 ? "\"null\"" : "\"nullable\":true";
        Claim(8, $"[{Path.GetFileName(file)}] a patch property is its argument's schema inlined, not required, admitting null",
            required.Length == 0
         && nameSchema is not null && !nameSchema.Contains("$ref") && nameSchema.Contains("string") && nameSchema.Contains(nullable)
         && quantitySchema is not null && !quantitySchema.Contains("$ref") && quantitySchema.Contains("integer") && quantitySchema.Contains(nullable)
         && emailSchema is not null && !emailSchema.Contains("$ref") && emailSchema.Contains("string") && emailSchema.Contains(nullable));
    }
    await app.StopAsync();
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures;
