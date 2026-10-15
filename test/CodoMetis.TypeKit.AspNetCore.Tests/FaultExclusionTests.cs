using System.Reflection;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A route leaves out of its document the members it never answers with: no status for them, and none at
/// all where no member is left at it. Reporting one anyway is refused rather than answered with a status
/// the document does not list, and a declaration that names no member, contradicts a status, or covers no
/// endpoint of its fault type stops the host (decision 78).
/// </summary>
public sealed class FaultExclusionTests
{
    public enum OrderFault { NotFound, NameTaken, Stale }

    private static Result<int, OrderFault> Find(int id) => id switch
    {
        1 => 1,
        2 => Result.Error(OrderFault.NameTaken),
        _ => Result.Error(OrderFault.NotFound),
    };

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task An_endpoint_leaves_its_members_out_of_its_document_and_answers_the_rest(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, endpoints: app =>
        {
            app.MapPost("/orders/{id:int}", (int id) => Find(id).ToHttpResult()).WithoutFaults(OrderFault.NotFound, OrderFault.Stale);
            app.MapGet("/orders/{id:int}", (int id) => Find(id).ToHttpResult());
        });

        await ProblemAsync(app, HttpMethod.Post, "/orders/2", HttpStatusCode.Conflict);

        var document = await DocumentAsync(app);
        Statuses(document, "/orders/{id}", "post").ShouldBe(["200", "409"]);
        Statuses(document, "/orders/{id}", "get").ShouldBe(["200", "404", "409", "412"]);
    }

    [Fact]
    public async Task A_status_another_member_still_gives_stays_with_only_that_member_s_problem_type()
    {
        await using var app = await StartAsync(typed: true, endpoints: app =>
            app.MapPost("/orders/{id:int}", (int id) => Find(id).ToHttpResult()).WithFaultStatus(OrderFault.Stale, StatusCodes.Status409Conflict).WithoutFaults(OrderFault.NotFound));

        var responses = (await DocumentAsync(app))["paths"]!["/orders/{id}"]!["post"]!["responses"]!;

        responses.AsObject().Select(response => response.Key).ShouldBe(["200", "409"]);
        responses["409"]!["description"]!.GetValue<string>().ShouldBe("Conflict. Problem types: https://problems.example/NameTaken, https://problems.example/Stale");
    }

    [Fact]
    public async Task Reporting_a_member_left_out_is_refused_naming_the_endpoint_and_the_member()
    {
        await using var app = await StartAsync(endpoints: app =>
            app.MapPost("/orders/{id:int}", (int id) => Find(id).ToHttpResult()).WithoutFaults(OrderFault.NotFound));

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => app.GetTestClient().PostAsync("/orders/9", null, TestContext.Current.CancellationToken));

        refusal.Message.ShouldStartWith("HTTP: POST /orders/{id:int} reported FaultExclusionTests.OrderFault.NotFound, which WithoutFaults(FaultExclusionTests.OrderFault.NotFound) leaves out of its document");
        refusal.Message.ShouldEndWith("README.md#leaving-out-what-a-route-cannot-answer");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_group_leaves_members_out_of_its_endpoints_that_answer_with_the_fault_type(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, endpoints: app =>
        {
            var orders = app.MapGroup("/orders").WithoutFaults(OrderFault.Stale);
            orders.MapGet("/{id:int}", (int id) => Find(id).ToHttpResult());
            orders.MapGet("/health", () => "ok");
        });

        (await app.GetTestClient().GetStringAsync("/orders/health", TestContext.Current.CancellationToken)).ShouldBe("ok");
        Statuses(await DocumentAsync(app), "/orders/{id}", "get").ShouldBe(["200", "404", "409"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task An_action_s_attribute_leaves_members_out_and_its_controller_s_applies_to_its_actions(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, mvc: [typeof(OrdersController)]);

        await ProblemAsync(app, HttpMethod.Get, "/mvc/orders/2", HttpStatusCode.Conflict);
        await Should.ThrowAsync<InvalidOperationException>(() => app.GetTestClient().PostAsync("/mvc/orders/9", null, TestContext.Current.CancellationToken));

        var document = await DocumentAsync(app);
        Statuses(document, "/mvc/orders/{id}", "get").ShouldBe(["200", "404", "409"]);
        Statuses(document, "/mvc/orders/{id}", "post").ShouldBe(["200", "409"]);
    }

    /// <summary>
    /// What the builder <c>MapControllers()</c> returns adds is on the endpoint, not on the action's descriptor:
    /// the document read the descriptor, so it listed the 404 the endpoint refuses to answer with, and a client
    /// of the document got a 500 for it (reported 2026-10-14).
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task WithoutFaults_on_MapControllers_leaves_members_out_of_every_action_s_document(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, mvc: [typeof(OrdersController)], controllers: actions => actions.WithoutFaults(OrderFault.NotFound));

        await ProblemAsync(app, HttpMethod.Get, "/mvc/orders/2", HttpStatusCode.Conflict);
        await Should.ThrowAsync<InvalidOperationException>(() => app.GetTestClient().GetAsync("/mvc/orders/9", TestContext.Current.CancellationToken));

        Statuses(await DocumentAsync(app), "/mvc/orders/{id}", "get").ShouldBe(["200", "409"]);
    }

    [Fact]
    public async Task A_value_no_member_has_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
            app.MapGet("/orders/{id:int}", (int id) => Find(id).ToHttpResult()).WithoutFaults((OrderFault)42)));

        refusal.Message.ShouldContain("WithoutFaults(FaultExclusionTests.OrderFault.42) names a value no member of FaultExclusionTests.OrderFault has, on HTTP: GET /orders/{id:int}.");
    }

    [Fact]
    public async Task An_attribute_naming_no_member_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: [typeof(MisnamedController)]));

        refusal.Message.ShouldContain("[WithoutFaults<FaultExclusionTests.OrderFault>(\"NotFound\", \"Gone\")] names no member \"Gone\" of FaultExclusionTests.OrderFault, on ");
    }

    /// <summary>
    /// A status that every endpoint of its scope leaves out serves nothing: a group of one endpoint, or the
    /// endpoint itself declaring both, is a contradiction, and the host does not start.
    /// </summary>
    [Fact]
    public async Task A_member_given_a_status_and_left_out_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
            app.MapGroup("/orders").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone)
               .MapGet("/{id:int}", (int id) => Find(id).ToHttpResult()).WithoutFaults(OrderFault.NotFound)));

        refusal.Message.ShouldContain("WithFaultStatus(FaultExclusionTests.OrderFault.NotFound, 410) gives a status to FaultExclusionTests.OrderFault.NotFound, which WithoutFaults(FaultExclusionTests.OrderFault.NotFound) leaves out, on HTTP: GET /orders/{id:int}.");

        var own = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
            app.MapGet("/orders/{id:int}", (int id) => Find(id).ToHttpResult())
               .WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone).WithoutFaults(OrderFault.NotFound)));

        own.Message.ShouldContain("WithFaultStatus(FaultExclusionTests.OrderFault.NotFound, 410) gives a status to FaultExclusionTests.OrderFault.NotFound, which WithoutFaults(FaultExclusionTests.OrderFault.NotFound) leaves out, on HTTP: GET /orders/{id:int}.");
    }

    /// <summary>
    /// An endpoint that leaves a member out narrows the status its group gives that member, as it narrows the host's
    /// mapping: the group's other endpoints answer with the group's status. Judged per endpoint, the group's status
    /// stopped the host as soon as one endpoint inside left the member out (reported 2026-10-14).
    /// </summary>
    [Fact]
    public async Task Leaving_a_member_out_narrows_its_group_s_status()
    {
        await using var app = await StartAsync(endpoints: app =>
        {
            var orders = app.MapGroup("/orders").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            orders.MapGet("/{id:int}", (int id) => Find(id).ToHttpResult());
            orders.MapPost("/{id:int}", (int id) => Find(id).ToHttpResult()).WithoutFaults(OrderFault.NotFound);
        });

        await ProblemAsync(app, HttpMethod.Get, "/orders/9", HttpStatusCode.Gone);
        await Should.ThrowAsync<InvalidOperationException>(() => app.GetTestClient().PostAsync("/orders/9", null, TestContext.Current.CancellationToken));

        var document = await DocumentAsync(app);
        Statuses(document, "/orders/{id}", "get").ShouldBe(["200", "409", "410", "412"]);
        Statuses(document, "/orders/{id}", "post").ShouldBe(["200", "409", "412"]);
    }

    [Fact]
    public async Task A_scope_none_of_whose_endpoints_answers_with_the_fault_type_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
        {
            var orders = app.MapGroup("/orders").WithoutFaults(OrderFault.NotFound);
            orders.MapGet("/health", () => "ok");
        }));

        refusal.Message.ShouldContain("WithoutFaults(FaultExclusionTests.OrderFault.NotFound) is set on HTTP: GET /orders/health, which answers with no FaultExclusionTests.OrderFault.");
    }

    [Fact]
    public void An_empty_list_or_a_null_member_is_refused_where_it_is_written()
    {
        var group = WebApplication.CreateBuilder().Build().MapGroup("/orders");

        Should.Throw<ArgumentException>(() => group.WithoutFaults<RouteGroupBuilder, OrderFault>()).ParamName.ShouldBe("faults");
        Should.Throw<ArgumentNullException>(() => group.WithoutFaults<RouteGroupBuilder, string>(null!, "x")).ParamName.ShouldBe("faults");
        Should.Throw<ArgumentException>(() => new WithoutFaultsAttribute<OrderFault>()).ParamName.ShouldBe("faults");
        Should.Throw<ArgumentException>(() => new WithoutFaultsAttribute<OrderFault>(" ")).ParamName.ShouldBe("faults");
    }

    [Fact]
    public async Task The_list_is_copied_where_it_is_written()
    {
        OrderFault[] faults = [OrderFault.NotFound];
        await using var app = await StartAsync(endpoints: app =>
        {
            app.MapGet("/orders/{id:int}", (int id) => Find(id).ToHttpResult()).WithoutFaults(faults);
            faults[0] = OrderFault.NameTaken;
        });

        Statuses(await DocumentAsync(app), "/orders/{id}", "get").ShouldBe(["200", "409", "412"]);
    }

    private static async Task<WebApplication> StartAsync(
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1,
        Action<WebApplication>? endpoints = null,
        Type[]? mvc = null,
        bool typed = false,
        Action<ControllerActionEndpointConventionBuilder>? controllers = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = version;
            options.AddTypeKit();
        });
        builder.Services.AddFaultResponses<OrderFault>(fault =>
        {
            var status = fault switch
            {
                OrderFault.NotFound => StatusCodes.Status404NotFound,
                OrderFault.NameTaken => StatusCodes.Status409Conflict,
                OrderFault.Stale => StatusCodes.Status412PreconditionFailed,
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            };
            return typed ? FaultResponse.Status(status).WithType(new Uri($"https://problems.example/{fault}")) : status;
        });
        if (mvc is not null)
        {
            builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.FeatureProviders.Add(new OnlyControllers(mvc));
            });
        }

        var app = builder.Build();
        app.MapOpenApi();
        endpoints?.Invoke(app);
        if (mvc is not null)
        {
            var actions = app.MapControllers();
            controllers?.Invoke(actions);
        }

        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static async Task ProblemAsync(WebApplication app, HttpMethod method, string path, HttpStatusCode status)
    {
        using var response = await app.GetTestClient().SendAsync(new HttpRequestMessage(method, path), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status, path);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    private static string[] Statuses(JsonNode document, string path, string method) =>
        [.. document["paths"]![path]![method]!["responses"]!.AsObject().Select(response => response.Key)];

    private sealed class OnlyControllers(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    [ApiController]
    [WithoutFaults<OrderFault>(nameof(OrderFault.Stale))]
    public sealed class OrdersController : ControllerBase
    {
        [HttpGet("/mvc/orders/{id:int}")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();

        [HttpPost("/mvc/orders/{id:int}")]
        [WithoutFaults<OrderFault>(nameof(OrderFault.NotFound))]
        public Results<Ok<int>, FaultProblem<OrderFault>> Post(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class MisnamedController : ControllerBase
    {
        [HttpGet("/mvc/misnamed/{id:int}")]
        [WithoutFaults<OrderFault>(nameof(OrderFault.NotFound), "Gone")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }
}
