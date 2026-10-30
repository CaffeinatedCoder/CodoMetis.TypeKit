using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ValueObjects;
using Shop;

async Task Check(string name, Func<Task<string>> body)
{
    try { Console.WriteLine($"{name}={await body()}"); }
    catch (Exception e) { Console.WriteLine($"{name}=THREW {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}"); }
}

// JSON through a source-generated context, the only resolver under Native AOT, which lists none of
// the wrapped types: the generated converters must not need them.
var line = new Line(OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee")), Code.FromKnownGood("ABC"), Name.From("n"), Quantity.From(3),
    Amount.From(1.5m), Link.From(new Uri("https://x.test/a")), Weekday.From(DayOfWeek.Friday),
    new() { [Quantity.From(7)] = 1 }, new() { [Weekday.From(DayOfWeek.Monday)] = 2 });
var written = "";
await Check("json", async () => written = JsonSerializer.Serialize(line, ShopJson.Default.Line));
await Check("json-roundtrip", async () => (JsonSerializer.Serialize(JsonSerializer.Deserialize(written, ShopJson.Default.Line)!, ShopJson.Default.Line) == written).ToString());
await Check("json-refused", async () => { JsonSerializer.Deserialize(written.Replace("\"ABC\"", "\"abc\""), ShopJson.Default.Line); return "accepted"; });
await Check("json-number-handling", async () =>
{
    var options = new JsonSerializerOptions(ShopJson.Default.Options) { NumberHandling = JsonNumberHandling.WriteAsString };
    return JsonSerializer.Serialize(Amount.From(2.5m), options);
});
// A context that lists no decimal: the converter registered at run time writes what the value object wraps,
// and the schema exporter describes it through the value object's attribute and the converter's description.
await Check("decimal-string", async () =>
{
    var options = new JsonSerializerOptions(ShopJson.Default.Options) { Converters = { new DecimalStringJsonConverter() } };
    string Try(Func<string> part) { try { return part(); } catch (Exception e) { return $"THREW {e.GetType().Name}"; } }
    var written = Try(() => JsonSerializer.Serialize(Amount.From(7.80m), options));
    var read = Try(() => JsonSerializer.Deserialize<Amount>("7.80", options) is var amount && amount.Value == 7.80m && amount.Value.Scale == 2 ? "True" : "False");
    var schema = Try(() => options.GetTypeInfo(typeof(Amount)).GetJsonSchemaAsNode(DescribedJsonSchema.ExporterOptions())["type"]!.ToString());
    return $"{written} {read} {schema}";
});
// A factory in the options and one a property names are each asked for the converter it makes, and the
// stored-JSON factory, which makes the generated converter, keeps the generated description: all through
// the attributes the trimmer keeps, none through a name.
await Check("decimal-factory", async () =>
{
    string Try(Func<string> part) { try { return part(); } catch (Exception e) { return $"THREW {e.GetType().Name}"; } }
    var options = new JsonSerializerOptions(ShopJson.Default.Options) { Converters = { new StringsFactory() } };
    var written = Try(() => JsonSerializer.Serialize(Amount.From(7.80m), options));
    var schema = Try(() => options.GetTypeInfo(typeof(Amount)).GetJsonSchemaAsNode(DescribedJsonSchema.ExporterOptions())["type"]!.ToString());
    var tagged = Try(() => JsonSerializer.Serialize(new Tagged(7.80m), ShopJson.Default.Tagged));
    var taggedSchema = Try(() => ShopJson.Default.Options.GetJsonSchemaAsNode(typeof(Tagged), DescribedJsonSchema.ExporterOptions())["properties"]!["Price"]!["type"]!.ToString());
    var stored = new JsonSerializerOptions(ShopJson.Default.Options) { Converters = { new StoredJsonConverterFactory(), new DecimalStringJsonConverter() } };
    var storedSchema = Try(() => stored.GetTypeInfo(typeof(Amount)).GetJsonSchemaAsNode(DescribedJsonSchema.ExporterOptions())["type"]!.ToString());
    return $"{written} {schema} {tagged} {taggedSchema} {storedSchema}";
});
await Check("stored", async () =>
{
    var store = new JsonSerializerOptions { TypeInfoResolver = ShopJson.Default, Converters = { new StoredJsonConverterFactory() } };
    return JsonSerializer.Deserialize<Code>("\"abc\"", store)!.Value;
});
await Check("option-refused", async () => JsonSerializer.Serialize(new Holder(Option.Some(1)), ShopJson.Default.Holder));
await Check("set-json", async () => JsonSerializer.Serialize(new Tracking(OrderStatus.Shipped, new() { [OrderStatus.Open] = 1 }), ShopJson.Default.Tracking));
await Check("set-json-read", async () =>
{
    var tracking = JsonSerializer.Deserialize("""{"Status":"Open","Counts":{"Shipped":2}}""", ShopJson.Default.Tracking)!;
    return $"{ReferenceEquals(tracking.Status, OrderStatus.Open)} {tracking.Counts[OrderStatus.Shipped]}";
});

// Arithmetic a value object declares: the wrapped type's operators, called directly, through generic code,
// and in a checked context, which picks the checked operator as it would for an int.
await Check("arithmetic", async () =>
{
    var total = (Amount.From(1.5m) + Amount.From(2m)) * 2m;
    string overflow;
    try { _ = checked(Quantity.From(int.MaxValue) + Quantity.From(1)); overflow = "wrapped around"; }
    catch (OverflowException) { overflow = "OverflowException"; }
    return $"{total} {Twice(Quantity.From(21))} {overflow}";
});

static T Twice<T>(T value) where T : System.Numerics.IAdditionOperators<T, T, T> => value + value;

// Parsing, including through a TypeConverter (NodaTime's LocalDate).
await Check("parse", async () => $"{Quantity.Parse("42", null).Value} {Weekday.Parse("Monday", null).Value} {Link.Parse("https://p.test/", null).Value} {Day.Parse("2026-09-27", null).Value:yyyy-MM-dd}");
await Check("tryparse-refused", async () => Code.TryParse("abc", null, out _).ToString());

// The same after something asked TypeDescriptor for the wrapped type's converter first, as MVC does
// binding a plain Instant: the type is then described but cannot be registered.
await Check("parse-after-lookup", async () => $"{LookUpConverter(typeof(NodaTime.Instant)).CanConvertFrom(typeof(string))} {Due.Parse("2026-09-27T12:00:00Z", null).Value}");

[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Stands for MVC's lookup, which is the application's.")]
static System.ComponentModel.TypeConverter LookUpConverter([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] Type type) =>
    System.ComponentModel.TypeDescriptor.GetConverter(type);

// OpenAPI. Under source-generated JSON the host's context lists what the value objects wrap, since
// the wrapped type's schema is ASP.NET's own, built from the host's JSON contract for it.
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, HostJson.Default));
// Decimals as strings in their minimal spelling, which the document describes with nothing more registered.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalStringJsonConverter()));
builder.Services.AddOpenApi(options => options.AddTypeKit());
// The domain's fault, answered as problem details: the problem-details context AddFaultResponses
// brings is the only way a source-generated host can write one.
builder.Services.AddFaultResponses<CodeFault>(fault => fault switch
{
    CodeFault.Blank        => StatusCodes.Status400BadRequest,
    CodeFault.NotUpperCase => StatusCodes.Status422UnprocessableEntity,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
// A fault set answered by its kinds, and the fault on the request for the host's own extension.
builder.Services.AddFaultResponses<AccountFault>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (context.HttpContext.Features.Get<IFaultFeature>() is { } fault) context.ProblemDetails.Extensions["code"] = $"ACC-{fault.Name}";
});

await using var app = builder.Build();
app.MapOpenApi();
app.MapGet("/orders/{id}", (OrderId id, Quantity? limit) => new OrderView(id, [Quantity.From(1)], Amount.From(1.5m)));
app.MapPost("/orders", (OrderView body) => body.Amount);
app.MapPost("/tracking", (Tracking body) => body.Status.Name);
app.MapPatch("/orders/{id}", (OrderId id, OrderPatch patch) => $"{patch.Code.Match(code => $"Value({code.Value})", () => "Null", () => "Absent")} {patch.Note.Match(note => $"Value({note})", () => "Null", () => "Absent")}");
app.MapGet("/codes/{text}", (string text) => Code.Create(text).ToHttpResult());
app.MapGet("/accounts/{id}", (string id) => (id == "taken" ? Result<string, AccountFault>.Error(AccountFault.EmailTaken) : Result<string, AccountFault>.Error(AccountFault.NotFound)).ToHttpResult());
app.MapGet("/strict/accounts/{id}", (string id) => Result<string, AccountFault>.Error(AccountFault.EmailTaken).ToHttpResult()).WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
app.MapGet("/detailed/accounts/{id}", (string id) => Result<string, Fault<AccountFault>>.Error(Fault.Of(AccountFault.EmailTaken, "In use since 3 May.")).ToHttpResult());
app.MapPost("/accounts", () => Result<string, AccountFault>.Success("a1").ToHttpResult(id => TypedResults.Created($"/accounts/{id}", id)));
app.MapGet("/ruled/accounts/{id}", (string id) => Result<string, Fault<AccountFault>>.Error(id == "set" ? Fault.Of(AccountFault.EmailTaken).WithRule(OrderStatus.Shipped) : Fault.Of(AccountFault.EmailTaken).WithRule(Billing.Rule.Reserved)).ToHttpResult());
// Faults of two types side by side, each answered as it is mapped, and a member the route leaves out.
app.MapGet("/composed/accounts/{id}", (string id) => Result<Fault<AccountFault, CodeFault>>.Error(id == "code" ? Fault<AccountFault, CodeFault>.Of(CodeFault.NotUpperCase) : Fault<AccountFault, CodeFault>.Of(AccountFault.EmailTaken, "In use since 3 May.")).ToHttpResult())
   .WithoutFaults(AccountFault.NotFound);
await app.StartAsync();

using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
JsonNode? document = null;
await Check("openapi", async () =>
{
    var response = await http.GetAsync("/openapi/v1.json");
    document = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    return ((int)response.StatusCode).ToString();
});
var schemas = document?["components"]?["schemas"];
Console.WriteLine($"openapi-component={schemas?["OrderId"]?.ToJsonString() ?? "missing"}");
Console.WriteLine($"openapi-parameter={document?["paths"]?["/orders/{id}"]?["get"]?["parameters"]?[0]?["schema"]?.ToJsonString() ?? "missing"}");
Console.WriteLine($"openapi-items={schemas?["OrderView"]?["properties"]?["quantities"]?["items"]?.ToJsonString() ?? "missing"}");
Console.WriteLine($"openapi-set={schemas?["OrderStatus"]?.ToJsonString() ?? "missing"}");
Console.WriteLine($"openapi-decimal={schemas?["Amount"]?["type"]?.ToJsonString() ?? "missing"} {schemas?["Amount"]?["format"]?.ToJsonString() ?? "missing"}");
await Check("bound", async () => ((int)(await http.GetAsync($"/orders/{Guid.NewGuid()}?limit=3")).StatusCode).ToString());
await Check("malformed", async () => ((int)(await http.GetAsync("/orders/nope")).StatusCode).ToString());
// A merge patch through the context: the patch's converter hides Code and string from the generator,
// so the value object reads with its own converter and the string with the serializer's built-in one.
await Check("patch", async () => await (await http.PatchAsync($"/orders/{Guid.NewGuid()}", new StringContent("""{"code":"ABC","note":null}""", System.Text.Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync());
await Check("patch-refused", async () => ((int)(await http.PatchAsync($"/orders/{Guid.NewGuid()}", new StringContent("""{"code":"abc"}""", System.Text.Encoding.UTF8, "application/json"))).StatusCode).ToString());
Console.WriteLine($"openapi-patch={schemas?["OrderPatch"]?["properties"]?["code"]?.ToJsonString() ?? "missing"} required={schemas?["OrderPatch"]?["required"]?.ToJsonString() ?? "none"}");
await Check("result-success", async () => await http.GetStringAsync("/codes/ABC"));
await Check("result-fault", async () =>
{
    var response = await http.GetAsync("/codes/abc");
    var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    return $"{(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType} {problem?["fault"]}";
});
Console.WriteLine($"result-responses={string.Join(",", document?["paths"]?["/codes/{text}"]?["get"]?["responses"]?.AsObject().Select(r => r.Key) ?? [])}");
async Task<string> Problem(string path)
{
    var response = await http.GetAsync(path);
    var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    return $"{(int)response.StatusCode} {problem?["fault"]} {problem?["detail"]?.ToString() ?? "-"} {problem?["code"]}";
}
await Check("fault-kind", () => Problem("/accounts/missing"));
await Check("fault-strict", () => Problem("/strict/accounts/x"));
await Check("fault-detail", () => Problem("/detailed/accounts/x"));
async Task<string> Rule(string path)
{
    var response = await http.GetAsync(path);
    return $"{(int)response.StatusCode} {JsonNode.Parse(await response.Content.ReadAsStringAsync())?["rule"]}";
}
await Check("fault-rule", () => Rule("/ruled/accounts/x"));
await Check("fault-rule-set", () => Rule("/ruled/accounts/set"));
await Check("fault-created", async () =>
{
    var response = await http.PostAsync("/accounts", content: null);
    return $"{(int)response.StatusCode} {response.Headers.Location}";
});
Console.WriteLine($"fault-responses={string.Join(",", document?["paths"]?["/strict/accounts/{id}"]?["get"]?["responses"]?.AsObject().Select(r => r.Key) ?? [])}");
await Check("composed-shared", () => Problem("/composed/accounts/code"));
await Check("composed-own", () => Problem("/composed/accounts/x"));
Console.WriteLine($"composed-responses={string.Join(",", document?["paths"]?["/composed/accounts/{id}"]?["get"]?["responses"]?.AsObject().Select(r => r.Key) ?? [])}");
await app.StopAsync();

public sealed record Line(OrderId Id, Code Code, Name Name, Quantity Quantity, Amount Amount, Link Link, Weekday Weekday,
    Dictionary<Quantity, int> ByQuantity, Dictionary<Weekday, int> ByDay);

public sealed record OrderView(OrderId Id, List<Quantity> Quantities, Amount Amount);

public sealed record OrderPatch(Patch<Code> Code, Patch<string> Note);

public sealed record Holder(Option<int> Value);

public sealed record Tracking(OrderStatus Status, Dictionary<OrderStatus, int> Counts);

// Lists no wrapped type: the generated converters must not need one.
public sealed record Tagged([property: JsonConverter(typeof(StringsFactory))] decimal Price);

public sealed class StringsFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(decimal);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new DecimalStringJsonConverter();
}

[JsonSerializable(typeof(Line))]
[JsonSerializable(typeof(Holder))]
[JsonSerializable(typeof(Code))]
[JsonSerializable(typeof(Amount))]
[JsonSerializable(typeof(Tagged))]
[JsonSerializable(typeof(Tracking))]
internal partial class ShopJson : JsonSerializerContext;

// The web host's: what the endpoints exchange, and what their value objects wrap, for the schemas.
[JsonSerializable(typeof(OrderView))]
[JsonSerializable(typeof(OrderPatch))]
[JsonSerializable(typeof(Tracking))]
[JsonSerializable(typeof(Amount))]
[JsonSerializable(typeof(Code))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(decimal))]
internal partial class HostJson : JsonSerializerContext;
