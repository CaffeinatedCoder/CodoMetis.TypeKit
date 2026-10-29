// sample: CodoMetis.TypeKit.AspNetCore/every-document
using Microsoft.AspNetCore.OpenApi;   // OpenApiOptions

// end sample
// sample: CodoMetis.TypeKit.AspNetCore/bind-required
using Microsoft.AspNetCore.Mvc.ModelBinding;   // BindRequired

// end sample
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace ReadmeSamples.Web;

// sample: CodoMetis.TypeKit.AspNetCore/document
public readonly partial record struct OrderId : IValue<Guid>;
public readonly partial record struct Quantity : IValue<int>;

// end sample
public interface IOrders
{
    OrderDto Find(OrderId id, Quantity? limit = null);
}

public static class Endpoints
{
    public static void AddDocument(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/setup
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        // end sample
    }

    public static void MapOrder(WebApplication app, IOrders orders)
    {
#pragma warning disable ASP0020 // the value objects are declared in this project, as the README's section on ASP0020 describes
        // sample: CodoMetis.TypeKit.AspNetCore/document
        app.MapGet("/orders/{id}", (OrderId id, Quantity? limit) => orders.Find(id, limit));   // returns OrderDto
        // end sample
#pragma warning restore ASP0020
    }

    public static void MapOrderInTheDeclaringProject(WebApplication app, IOrders orders)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/asp0020
#pragma warning disable ASP0020 // OrderId implements IParsable once generated
        app.MapGet("/orders/{id}", (OrderId id) => orders.Find(id));
#pragma warning restore ASP0020
        // end sample
    }
}

// sample: CodoMetis.TypeKit.AspNetCore/document
public sealed record OrderDto(OrderId Id, List<OrderId> Related, Dictionary<string, Quantity> PerWarehouse);
// end sample

// sample: CodoMetis.TypeKit.AspNetCore/source-generated-json
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(Guid))]   // what OrderId wraps
[JsonSerializable(typeof(int))]    // what Quantity wraps
internal partial class AppJson : JsonSerializerContext;

// end sample
public static class EveryDocument
{
    public static void Use(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/every-document
        builder.Services.ConfigureAll<OpenApiOptions>(options => options.AddTypeKit());
        // end sample
    }
}

public static class SourceGeneratedJson
{
    public static void Use(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/source-generated-json
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default));
        // end sample
    }
}

public static class RequiredProperties
{
    public static void Use(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/required-properties
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectRequiredConstructorParameters = true);
        builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.RespectRequiredConstructorParameters = true);
        // end sample
    }
}

// sample: CodoMetis.TypeKit.AspNetCore/result-response
public enum OrderFault { NotFound, AlreadyShipped }

// end sample
public interface IOrderService
{
    Result<OrderDto, OrderFault> Find(OrderId id);

    Result<OrderFault> Cancel(OrderId id);
}

public static class ResultResponses
{
    public static void Use(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/result-response
        builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound       => StatusCodes.Status404NotFound,
            OrderFault.AlreadyShipped => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });

        // end sample
    }

    public static void Map(WebApplication app, IOrderService orders)
    {
#pragma warning disable ASP0020 // the value objects are declared in this project, as the README's section on ASP0020 describes
        // sample: CodoMetis.TypeKit.AspNetCore/result-response
        app.MapGet("/orders/{id}", (OrderId id) => orders.Find(id).ToHttpResult());      // Result<OrderDto, OrderFault>
        app.MapDelete("/orders/{id}", (OrderId id) => orders.Cancel(id).ToHttpResult()); // Result<OrderFault>
        // end sample
#pragma warning restore ASP0020
    }
}

public sealed record PlaceOrder(OrderId Id, Quantity Quantity);

[ApiController]
[Route("mvc/orders")]
public sealed class PlaceOrderController : ControllerBase
{
    [HttpPost]
    public OrderId Post(PlaceOrder order) => order.Id;
}

/// <summary>
/// What the README's binding section says about a property missing from a body: without the option both
/// minimal APIs and MVC bind it as <c>default</c> and answer 200; with the sample's two lines both answer 400.
/// </summary>
public sealed class RequiredPropertyTests
{
    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.BadRequest)]
    public async Task A_body_without_a_value_object_is_refused_only_with_the_option(bool respectRequired, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(PlaceOrderController).Assembly);
        if (respectRequired) RequiredProperties.Use(builder);

        await using var app = builder.Build();
        app.MapPost("/orders", (PlaceOrder order) => order.Id);
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var client = app.GetTestClient();
        foreach (var path in new[] { "/orders", "/mvc/orders" })
        {
            var response = await client.PostAsync(path, new StringContent("""{"quantity":3}""", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(expected, path);
        }
    }
}

// Internal, so no host of this assembly discovers it unasked; the test below adds it.
[ApiController]
internal sealed class FindOrderController(IOrders orders) : ControllerBase
{
    // sample: CodoMetis.TypeKit.AspNetCore/bind-required
    [HttpGet("/mvc/orders")]
    public OrderDto Find([FromQuery, BindRequired] OrderId id, [FromQuery] Quantity? limit) => orders.Find(id, limit);
    // end sample
}

/// <summary>
/// What the README's binding section says about a value missing from an MVC query: with <c>[BindRequired]</c>
/// a 400 naming the parameter, and a nullable parameter <c>null</c>. MvcBindingTests in the AspNetCore tests
/// pins the <c>default</c> MVC binds without either.
/// </summary>
public sealed class BindRequiredTests
{
    [Fact]
    public async Task A_required_value_object_missing_from_the_query_is_a_400_and_an_optional_one_is_null()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var orders = new RecordingOrders();
        builder.Services.AddSingleton<IOrders>(orders);
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.FeatureProviders.Add(new OnlyFindOrder());
        });

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();

        using var missing = await client.GetAsync("/mvc/orders", TestContext.Current.CancellationToken);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("A value for the 'id' parameter or property was not provided.");
        orders.Calls.ShouldBe(0, "the action never ran");

        var id = Guid.NewGuid();
        using var found = await client.GetAsync($"/mvc/orders?id={id}", TestContext.Current.CancellationToken);
        found.StatusCode.ShouldBe(HttpStatusCode.OK);
        orders.Calls.ShouldBe(1);
        orders.Limit.ShouldBeNull();
    }

    private sealed class RecordingOrders : IOrders
    {
        public int Calls { get; private set; }

        public Quantity? Limit { get; private set; }

        public OrderDto Find(OrderId id, Quantity? limit = null)
        {
            Calls++;
            Limit = limit;
            return new OrderDto(id, [], []);
        }
    }

    private sealed class OnlyFindOrder : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(FindOrderController).GetTypeInfo());
    }
}

public sealed partial class AspNetCoreTests
{
    private const string Readme = "src/CodoMetis.TypeKit.AspNetCore/README.md";

    /// <summary>
    /// What the README says around the sample: <c>AddTypeKit()</c> configures one document, a document without
    /// it describes a value object as <c>{}</c>, <c>ConfigureAll</c> reaches every document, and a document
    /// that already had it is unchanged.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureAll_describes_value_objects_in_every_document(bool configureAll)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi("public");
        builder.Services.AddOpenApi("internal", options => options.AddTypeKit());
        if (configureAll) EveryDocument.Use(builder);

        await using var app = builder.Build();
        app.MapOpenApi();
        Endpoints.MapOrder(app, new NoOrders());
        await app.StartAsync(TestContext.Current.CancellationToken);

        var uuid = new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        foreach (var (name, expected) in new[] { ("public", configureAll ? uuid : new JsonObject()), ("internal", uuid) })
        {
            var json = await app.GetTestClient().GetStringAsync($"/openapi/{name}.json", TestContext.Current.CancellationToken);
            var orderId = JsonNode.Parse(json)!.Component("OrderId");

            JsonNode.DeepEquals(orderId, expected).ShouldBeTrue($"'{name}' describes OrderId as {orderId.ToJsonString()}");
        }
    }

    /// <summary>
    /// The README's JSON block, read from the README, against the document the sample's host publishes: every
    /// keyword the README shows, the document has with the same value.
    /// </summary>
    [Fact]
    public async Task The_document_says_what_the_README_shows()
    {
        var document = await SampleOutputs.OpenApiDocumentAsync(Endpoints.AddDocument, app => Endpoints.MapOrder(app, new NoOrders()));
        var shown = ShownComponents();

        shown.Select(component => component.Key).ShouldBe(["OrderId", "Quantity", "OrderDto"]);
        foreach (var (name, schema) in shown) document.Component(name).ShouldContainSchema(schema!, name);
    }

    /// <summary>The block after the "What the document says" sample: component entries, without the enclosing braces.</summary>
    private static JsonObject ShownComponents()
    {
        var json = JsonBlock().Match(SampleOutputs.Readme(Readme));
        json.Success.ShouldBeTrue($"{Readme} has no ```json block.");

        return JsonNode.Parse($"{{{json.Groups["body"].Value}}}")!.AsObject();
    }

    /// <summary>
    /// The problem the README shows under "A Result as a response", read from the README, against what the
    /// sample's host answers for a fault: the same members with the same values, the trace id aside.
    /// </summary>
    [Fact]
    public async Task A_fault_is_answered_with_the_problem_the_README_shows()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        ResultResponses.Use(builder);

        await using var app = builder.Build();
        ResultResponses.Map(app, new ShippedOrders());
        await app.StartAsync(TestContext.Current.CancellationToken);

        var client = app.GetTestClient();
        using var response = await client.GetAsync($"/orders/{Guid.Empty}", TestContext.Current.CancellationToken);
        var answered = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

        var section = SampleOutputs.Readme(Readme).Split("## A Result as a response")[1];
        var shown = JsonNode.Parse(JsonBlock().Match(section).Groups["body"].Value)!.AsObject();

        ((int)response.StatusCode).ShouldBe(shown["status"]!.GetValue<int>());
        answered.Select(member => member.Key).Order().ShouldBe(shown.Select(member => member.Key).Order());
        foreach (var (name, value) in shown.Where(member => member.Key != "traceId"))
            JsonNode.DeepEquals(answered[name], value).ShouldBeTrue($"{name}: {answered[name]?.ToJsonString()} where the README shows {value?.ToJsonString()}");

        (await client.DeleteAsync($"/orders/{Guid.Empty}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [GeneratedRegex(@"^```json\n(?<body>.*?)\n```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex JsonBlock();

    private sealed class ShippedOrders : IOrderService
    {
        public Result<OrderDto, OrderFault> Find(OrderId id) => Result.Error(OrderFault.AlreadyShipped);

        public Result<OrderFault> Cancel(OrderId id) => Result.Success();
    }

    private sealed class NoOrders : IOrders
    {
        public OrderDto Find(OrderId id, Quantity? limit = null) => new(id, [], []);
    }
}

/// <summary>The README's NodaTime host: the types described beside their converters, and the document following them.</summary>
public static class NodaTimeHost
{
    public readonly partial record struct Deadline : CodoMetis.TypeKit.ValueObjects.IValue<Instant>;

    public sealed record Assignment(string Title, Instant Created, Deadline Due, LocalDate Day);

    public static void Configure(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/nodatime
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
            options.SerializerOptions.Converters.Add(new JsonSchemaDescriptions()
                .Describe<Instant>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["format"] = "date-time" }))
                .Describe<LocalDate>(JsonSchemaDescription.Of(new JsonObject { ["type"] = "string", ["format"] = "date" })));
        });
        // end sample
    }
}

public sealed class NodaTimeHostTests
{
    [Fact]
    public async Task The_document_describes_NodaTime_types_and_the_value_objects_wrapping_them()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        NodaTimeHost.Configure(builder);
        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/assignments", (NodaTimeHost.Assignment assignment) => assignment);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var document = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var schemas = document["components"]!["schemas"]!;
        var properties = schemas["Assignment"]!["properties"]!;

        JsonNode Resolve(JsonNode schema) => schema["$ref"] is { } reference ? schemas[reference.GetValue<string>().Split('/')[^1]]! : schema;

        Resolve(properties["created"]!).ToJsonString().ShouldBe("""{"type":"string","format":"date-time"}""");
        Resolve(properties["day"]!).ToJsonString().ShouldBe("""{"type":"string","format":"date"}""");
        Resolve(properties["due"]!).ToJsonString().ShouldBe("""{"type":"string","format":"date-time"}""", "a value object over an Instant, through its wrapped type");
    }
}
