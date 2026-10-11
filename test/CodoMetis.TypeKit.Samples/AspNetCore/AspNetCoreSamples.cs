// sample: CodoMetis.TypeKit.AspNetCore/inline
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;   // GeneratedValueObjectAttribute, on every value object

// end sample
// sample: CodoMetis.TypeKit.AspNetCore/every-document
using Microsoft.AspNetCore.OpenApi;   // OpenApiOptions

// end sample
using System.Net;
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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

public static class Inlining
{
    public static void Use(OpenApiOptions options)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/inline
        options.AddTypeKit();
        options.CreateSchemaReferenceId = type =>
            (Nullable.GetUnderlyingType(type.Type) ?? type.Type).GetCustomAttribute<GeneratedValueObjectAttribute>() is not null
                ? null
                : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
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
