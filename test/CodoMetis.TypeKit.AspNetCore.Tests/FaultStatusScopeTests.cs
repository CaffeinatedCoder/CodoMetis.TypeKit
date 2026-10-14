using System.Reflection;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A fault status declared on a scope, a route group or a controller, applies to the endpoints of that
/// scope that answer with its fault type and leaves the others alone; the host refuses it only where its
/// own scope has no such endpoint, whatever other scopes have (decision 61). A status on one endpoint or
/// action is a scope of one, and stays strict.
/// </summary>
public sealed class FaultStatusScopeTests
{
    public enum OrderFault { NotFound, NameTaken }

    private static Result<int, OrderFault> Find(int id) => id switch
    {
        1 => 1,
        2 => Result.Error(OrderFault.NameTaken),
        _ => Result.Error(OrderFault.NotFound),
    };

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    // Mixed scopes: the fault endpoint takes the scope's status, the plain one answers and is documented as before.

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_group_s_status_applies_to_its_endpoints_that_answer_with_the_fault_type_and_leaves_the_others(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, endpoints: app =>
        {
            var orders = app.MapGroup("/orders").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            orders.MapGet("/{id:int}", (int id) => Find(id).ToHttpResult());
            orders.MapGet("/health", () => "ok");
        });

        await ProblemAsync(app, "/orders/9", HttpStatusCode.Gone);
        (await app.GetTestClient().GetStringAsync("/orders/health", TestContext.Current.CancellationToken)).ShouldBe("ok");

        var document = await DocumentAsync(app);
        Statuses(document, "/orders/{id}").ShouldBe(["200", "409", "410"]);
        Statuses(document, "/orders/health").ShouldBe(["200"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_controller_s_status_applies_to_its_actions_that_answer_with_the_fault_type_and_leaves_the_others(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, mvc: [typeof(MixedController)]);

        await ProblemAsync(app, "/mvc/orders/9", HttpStatusCode.Gone);
        (await app.GetTestClient().GetStringAsync("/mvc/orders/health", TestContext.Current.CancellationToken)).ShouldBe("ok");

        var document = await DocumentAsync(app);
        Statuses(document, "/mvc/orders/{id}").ShouldBe(["200", "409", "410"]);
        Statuses(document, "/mvc/orders/health").ShouldBe(["200"]);
    }

    // Wholly stale scopes: no endpoint of the scope answers with the fault type.

    [Fact]
    public async Task A_group_none_of_whose_endpoints_answers_with_the_fault_type_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
        {
            var probes = app.MapGroup("/probes").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            probes.MapGet("/health", () => "ok");
            probes.MapGet("/ready", () => "ok");
        }));

        refusal.Message.ShouldContain(
            "WithFaultStatus(FaultStatusScopeTests.OrderFault.NotFound, 410) is set on 2 endpoints, none of which answers with FaultStatusScopeTests.OrderFault: " +
            "HTTP: GET /probes/health, HTTP: GET /probes/ready.");
    }

    [Fact]
    public async Task A_controller_none_of_whose_actions_answers_with_the_fault_type_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: [typeof(StaleController)]));

        refusal.Message.ShouldContain("[FaultStatus<FaultStatusScopeTests.OrderFault>(\"NotFound\", 410)] is set on 2 endpoints, none of which answers with FaultStatusScopeTests.OrderFault: ");
        refusal.Message.ShouldContain("StaleController.Health");
        refusal.Message.ShouldContain("StaleController.Ready");
    }

    // Unrelated scopes: an endpoint of another scope that answers with the fault type does not save a stale declaration.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stale_group_stops_the_host_beside_another_group_that_answers_with_the_fault_type(bool otherDeclaresItsOwn)
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
        {
            var probes = app.MapGroup("/probes").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            probes.MapGet("/health", () => "ok");
            probes.MapGet("/ready", () => "ok");

            var orders = app.MapGroup("/orders");
            if (otherDeclaresItsOwn) orders.WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            orders.MapGet("/{id:int}", (int id) => Find(id).ToHttpResult());
        }));

        refusal.Message.ShouldContain(
            "WithFaultStatus(FaultStatusScopeTests.OrderFault.NotFound, 410) is set on 2 endpoints, none of which answers with FaultStatusScopeTests.OrderFault: " +
            "HTTP: GET /probes/health, HTTP: GET /probes/ready.");
        refusal.Message.ShouldNotContain("GET /orders");
    }

    [Fact]
    public async Task A_stale_controller_stops_the_host_beside_another_controller_that_answers_with_the_fault_type()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: [typeof(StaleController), typeof(MixedController)]));

        refusal.Message.ShouldContain("[FaultStatus<FaultStatusScopeTests.OrderFault>(\"NotFound\", 410)] is set on 2 endpoints, none of which answers with FaultStatusScopeTests.OrderFault: ");
        refusal.Message.ShouldContain("StaleController.Health");
        refusal.Message.ShouldNotContain("MixedController");
    }

    /// <summary>An attribute on a base controller is declared again on each controller that derives from it: each is its own scope.</summary>
    [Fact]
    public async Task Each_controller_deriving_from_a_base_with_a_status_is_a_scope_of_its_own()
    {
        await using (var app = await StartAsync(mvc: [typeof(ArchivedOrdersController)]))
            await ProblemAsync(app, "/mvc/archive/orders/9", HttpStatusCode.Gone);

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: [typeof(ArchivedOrdersController), typeof(ArchiveHealthController)]));

        refusal.Message.ShouldContain("[FaultStatus<FaultStatusScopeTests.OrderFault>(\"NotFound\", 410)] is set on ");
        refusal.Message.ShouldContain("ArchiveHealthController.Health");
        refusal.Message.ShouldContain("which answers with no FaultStatusScopeTests.OrderFault.");
        refusal.Message.ShouldNotContain("ArchivedOrdersController");
    }

    // Nested scopes: the innermost status wins, and an outer declaration is satisfied by an endpoint of an inner group.

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Nested_groups_and_an_endpoint_answer_with_the_innermost_status(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, endpoints: app =>
        {
            // The outer group's only endpoints that answer with the fault type are the inner group's.
            var outer = app.MapGroup("/outer")
                           .WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone)
                           .WithFaultStatus(OrderFault.NameTaken, StatusCodes.Status422UnprocessableEntity);
            outer.MapGet("/health", () => "ok");

            var inner = outer.MapGroup("/inner").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status451UnavailableForLegalReasons);
            inner.MapGet("/a/{id:int}", (int id) => Find(id).ToHttpResult());
            inner.MapGet("/b/{id:int}", (int id) => Find(id).ToHttpResult()).WithFaultStatus(OrderFault.NotFound, StatusCodes.Status404NotFound);
        });

        await ProblemAsync(app, "/outer/inner/a/9", HttpStatusCode.UnavailableForLegalReasons);
        await ProblemAsync(app, "/outer/inner/a/2", HttpStatusCode.UnprocessableEntity);
        await ProblemAsync(app, "/outer/inner/b/9", HttpStatusCode.NotFound);
        await ProblemAsync(app, "/outer/inner/b/2", HttpStatusCode.UnprocessableEntity);
        (await app.GetTestClient().GetStringAsync("/outer/health", TestContext.Current.CancellationToken)).ShouldBe("ok");

        var document = await DocumentAsync(app);
        Statuses(document, "/outer/inner/a/{id}").ShouldBe(["200", "422", "451"]);
        Statuses(document, "/outer/inner/b/{id}").ShouldBe(["200", "404", "422"]);
        Statuses(document, "/outer/health").ShouldBe(["200"]);
    }

    /// <summary>
    /// The builder <c>MapControllers()</c> returns covers every action, and ASP.NET puts its metadata after
    /// the controllers' attributes: its status is the last, and wins over an action's own.
    /// </summary>
    [Fact]
    public async Task A_status_on_MapControllers_covers_every_action_and_comes_after_their_attributes()
    {
        await using var app = await StartAsync(
            mvc: [typeof(StrictActionController), typeof(PlainController)],
            controllers: builder => builder.WithFaultStatus(OrderFault.NotFound, StatusCodes.Status451UnavailableForLegalReasons));

        await ProblemAsync(app, "/mvc/strict/9", HttpStatusCode.UnavailableForLegalReasons);
        (await app.GetTestClient().GetStringAsync("/mvc/plain", TestContext.Current.CancellationToken)).ShouldBe("ok");
    }

    // A status on one endpoint or action stays strict, also inside a scope whose own status is satisfied.

    [Fact]
    public async Task An_endpoint_s_own_status_on_a_plain_endpoint_stops_the_host_inside_a_satisfied_group()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
        {
            var orders = app.MapGroup("/orders").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            orders.MapGet("/{id:int}", (int id) => Find(id).ToHttpResult());
            orders.MapGet("/health", () => "ok").WithFaultStatus(OrderFault.NameTaken, StatusCodes.Status422UnprocessableEntity);
        }));

        refusal.Message.ShouldContain("WithFaultStatus(FaultStatusScopeTests.OrderFault.NameTaken, 422) is set on HTTP: GET /orders/health, which answers with no FaultStatusScopeTests.OrderFault.");
        refusal.Message.ShouldNotContain("NotFound, 410");
    }

    [Fact]
    public async Task An_action_s_own_status_on_a_plain_action_stops_the_host_inside_a_satisfied_controller()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: [typeof(StrayActionController)]));

        refusal.Message.ShouldContain("[FaultStatus<FaultStatusScopeTests.OrderFault>(\"NameTaken\", 422)] is set on ");
        refusal.Message.ShouldContain("StrayActionController.Health");
        refusal.Message.ShouldContain("which answers with no FaultStatusScopeTests.OrderFault.");
        refusal.Message.ShouldNotContain("\"NotFound\", 410");
    }

    // A scope's declaration is checked as an endpoint's is: a member its type has, and an error status.

    [Fact]
    public async Task A_group_s_status_for_a_value_no_member_has_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
        {
            var orders = app.MapGroup("/orders").WithFaultStatus((OrderFault)42, StatusCodes.Status410Gone);
            orders.MapGet("/{id:int}", (int id) => Find(id).ToHttpResult());
            orders.MapGet("/health", () => "ok");
        }));

        refusal.Message.ShouldContain("WithFaultStatus(FaultStatusScopeTests.OrderFault.42, 410) names a value no member of FaultStatusScopeTests.OrderFault has, on HTTP: GET /orders/{id:int}.");
    }

    [Fact]
    public async Task A_controller_s_attribute_naming_no_member_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: [typeof(MisnamedController)]));

        refusal.Message.ShouldContain("[FaultStatus<FaultStatusScopeTests.OrderFault>(\"Gone\", 410)] names no member of FaultStatusScopeTests.OrderFault, on ");
        refusal.Message.ShouldContain("MisnamedController.Get");
    }

    [Fact]
    public void A_group_s_status_is_an_error_status() =>
        Should.Throw<ArgumentOutOfRangeException>(() => WebApplication.CreateBuilder().Build().MapGroup("/orders").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status200OK))
              .ParamName.ShouldBe("status");

    [Fact]
    public async Task A_controller_s_attribute_with_no_error_status_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<Exception>(() => StartAsync(mvc: [typeof(NoErrorController)]));

        var cause = refusal;
        while (cause is not ArgumentOutOfRangeException && cause.InnerException is { } inner) cause = inner;
        cause.ShouldBeOfType<ArgumentOutOfRangeException>($"{refusal.GetType().Name}: {refusal.Message}").ParamName.ShouldBe("status");
    }

    private static async Task<WebApplication> StartAsync(
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1,
        Action<WebApplication>? endpoints = null,
        Type[]? mvc = null,
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
        builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound => StatusCodes.Status404NotFound,
            OrderFault.NameTaken => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
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

    private static async Task ProblemAsync(WebApplication app, string path, HttpStatusCode status)
    {
        using var response = await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status, path);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    private static string[] Statuses(JsonNode document, string path) =>
        [.. document["paths"]![path]!["get"]!["responses"]!.AsObject().Select(response => response.Key).Order()];

    private sealed class OnlyControllers(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    [ApiController]
    [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status410Gone)]
    public sealed class MixedController : ControllerBase
    {
        [HttpGet("/mvc/orders/{id:int}")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();

        [HttpGet("/mvc/orders/health")]
        public string Health() => "ok";
    }

    [ApiController]
    [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status410Gone)]
    public sealed class StaleController : ControllerBase
    {
        [HttpGet("/mvc/stale/health")]
        public string Health() => "ok";

        [HttpGet("/mvc/stale/ready")]
        public string Ready() => "ok";
    }

    [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status410Gone)]
    public abstract class ArchiveControllerBase : ControllerBase;

    [ApiController]
    public sealed class ArchivedOrdersController : ArchiveControllerBase
    {
        [HttpGet("/mvc/archive/orders/{id:int}")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class ArchiveHealthController : ArchiveControllerBase
    {
        [HttpGet("/mvc/archive/health")]
        public string Health() => "ok";
    }

    [ApiController]
    [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status410Gone)]
    public sealed class StrayActionController : ControllerBase
    {
        [HttpGet("/mvc/stray/{id:int}")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();

        [HttpGet("/mvc/stray/health")]
        [FaultStatus<OrderFault>(nameof(OrderFault.NameTaken), StatusCodes.Status422UnprocessableEntity)]
        public string Health() => "ok";
    }

    [ApiController]
    public sealed class StrictActionController : ControllerBase
    {
        [HttpGet("/mvc/strict/{id:int}")]
        [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status410Gone)]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class PlainController : ControllerBase
    {
        [HttpGet("/mvc/plain")]
        public string Get() => "ok";
    }

    [ApiController]
    [FaultStatus<OrderFault>("Gone", StatusCodes.Status410Gone)]
    public sealed class MisnamedController : ControllerBase
    {
        [HttpGet("/mvc/misnamed/{id:int}")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();

        [HttpGet("/mvc/misnamed/health")]
        public string Health() => "ok";
    }

    [ApiController]
    [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status200OK)]
    public sealed class NoErrorController : ControllerBase
    {
        [HttpGet("/mvc/no-error/{id:int}")]
        public Results<Ok<int>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }
}
