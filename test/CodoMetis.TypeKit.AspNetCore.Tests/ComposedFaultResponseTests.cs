using System.Reflection;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.Generators.Probes;
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
/// A fault of several types (<see cref="Fault{T1, T2}"/>) is answered as its own type maps it, one
/// <see cref="FaultProblem{TFault}"/> per type: the operation lists the statuses of every type, ascending,
/// with the problem types of all of them at a status they share, and the host does not start while a type is
/// unmapped, or while two types the endpoint answers with share a member's bare name (decision 78).
/// </summary>
public sealed class ComposedFaultResponseTests
{
    public enum OrderFault { NotFound, NameTaken }

    public enum ConcurrencyFault { Conflict, Stale }

    public enum RemovalFault { StillReferenced, ReasonRequired }

    public enum ClashingFault { NameTaken, Other }

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0];

    private static Result<Fault<OrderFault, ConcurrencyFault>> Rename(int id) => id switch
    {
        1 => Result<Fault<OrderFault, ConcurrencyFault>>.Success(),
        2 => Fault<OrderFault, ConcurrencyFault>.Of(OrderFault.NameTaken, "Order 2 has that name."),
        3 => Fault<OrderFault, ConcurrencyFault>.Of(Fault.Of(ConcurrencyFault.Stale, "The order is at version 4, not 3.").WithRule(RemovalFault.ReasonRequired)),
        _ => Fault<OrderFault, ConcurrencyFault>.Of(ConcurrencyFault.Conflict),
    };

    private static Result<int, Fault<OrderFault, ConcurrencyFault, RemovalFault>> Remove(int id) => id switch
    {
        1 => 1,
        2 => Result.Error<Fault<OrderFault, ConcurrencyFault, RemovalFault>>(Fault.Of(OrderFault.NotFound)),
        3 => Result.Error<Fault<OrderFault, ConcurrencyFault, RemovalFault>>(Fault.Of(ConcurrencyFault.Conflict)),
        _ => Result.Error<Fault<OrderFault, ConcurrencyFault, RemovalFault>>(Fault.Of(RemovalFault.StillReferenced, "Invoice 9 refers to it.")),
    };

    [Fact]
    public async Task A_fault_of_either_type_is_answered_as_its_type_maps_it()
    {
        await using var app = await StartAsync(endpoints: app => app.MapPut("/orders/{id:int}", (int id) => Rename(id).ToHttpResult()));

        (await StatusAsync(app, "PUT", "/orders/1")).ShouldBe(HttpStatusCode.NoContent);

        var own = await ProblemAsync(app, "PUT", "/orders/2", HttpStatusCode.Conflict);
        own["fault"]!.GetValue<string>().ShouldBe("NameTaken");
        own["detail"]!.GetValue<string>().ShouldBe("Order 2 has that name.");

        var shared = await ProblemAsync(app, "PUT", "/orders/3", HttpStatusCode.PreconditionFailed);
        shared["fault"]!.GetValue<string>().ShouldBe("Stale");
        shared["detail"]!.GetValue<string>().ShouldBe("The order is at version 4, not 3.");
        shared["rule"]!.GetValue<string>().ShouldBe("ComposedFaultResponseTests.RemovalFault.ReasonRequired");
    }

    [Fact]
    public async Task A_fault_of_any_of_three_types_is_answered_and_a_value_as_200()
    {
        await using var app = await StartAsync(endpoints: app => app.MapDelete("/orders/{id:int}", (int id) => Remove(id).ToHttpResult()));

        (await StatusAsync(app, "DELETE", "/orders/1")).ShouldBe(HttpStatusCode.OK);
        (await ProblemAsync(app, "DELETE", "/orders/2", HttpStatusCode.NotFound))["fault"]!.GetValue<string>().ShouldBe("NotFound");
        (await ProblemAsync(app, "DELETE", "/orders/3", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("Conflict");
        (await ProblemAsync(app, "DELETE", "/orders/4", HttpStatusCode.Conflict))["detail"]!.GetValue<string>().ShouldBe("Invoice 9 refers to it.");
    }

    [Fact]
    public async Task The_success_the_endpoint_chooses_is_answered_with_either_fault()
    {
        await using var app = await StartAsync(endpoints: app =>
        {
            app.MapPost("/orders/{id:int}", (int id) => Remove(id).ToHttpResult(value => TypedResults.Created($"/orders/{value}", value)));
            app.MapPost("/renames/{id:int}", (int id) => Rename(id).ToHttpResult(() => TypedResults.Accepted("/renames")));
        });

        (await StatusAsync(app, "POST", "/orders/1")).ShouldBe(HttpStatusCode.Created);
        await ProblemAsync(app, "POST", "/orders/4", HttpStatusCode.Conflict);
        (await StatusAsync(app, "POST", "/renames/1")).ShouldBe(HttpStatusCode.Accepted);
        await ProblemAsync(app, "POST", "/renames/3", HttpStatusCode.PreconditionFailed);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task The_operation_lists_every_type_s_statuses_ascending(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, endpoints: app =>
        {
            app.MapPut("/orders/{id:int}", (int id) => Rename(id).ToHttpResult());
            app.MapDelete("/orders/{id:int}", (int id) => Remove(id).ToHttpResult());
            app.MapPost("/stale/{id:int}", (int id) => Result<Fault<ConcurrencyFault, OrderFault>>.Success().ToHttpResult());
        });

        var document = await DocumentAsync(app);

        Statuses(document, "/orders/{id}", "put").ShouldBe(["204", "404", "409", "412"]);
        Statuses(document, "/orders/{id}", "delete").ShouldBe(["200", "404", "409", "412", "422"]);

        // The first type's statuses are the higher ones: listed by type, they read 409, 412, 404.
        Statuses(document, "/stale/{id}", "post").ShouldBe(["204", "404", "409", "412"]);
    }

    /// <summary>
    /// A status two types share is one response listing the problem types of both. The transformer skipped
    /// a status an earlier type had listed, so the second type's problem types were missing, for a composed
    /// fault and for any endpoint answering two fault types.
    /// </summary>
    [Fact]
    public async Task A_status_two_types_share_lists_the_problem_types_of_both()
    {
        await using var app = await StartAsync(typed: true, endpoints: app => app.MapDelete("/orders/{id:int}", (int id) => Remove(id).ToHttpResult()));

        var conflict = (await DocumentAsync(app))["paths"]!["/orders/{id}"]!["delete"]!["responses"]!["409"]!["description"]!.GetValue<string>();

        conflict.ShouldBe("Conflict. Problem types: https://problems.example/OrderFault.NameTaken, https://problems.example/ConcurrencyFault.Conflict, " +
                          "https://problems.example/RemovalFault.StillReferenced");
    }

    [Fact]
    public async Task A_type_left_unmapped_stops_the_host_naming_the_endpoint()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(
            removal: false,
            endpoints: app => app.MapDelete("/orders/{id:int}", (int id) => Remove(id).ToHttpResult())));

        refusal.Message.ShouldContain("No HTTP status is mapped for the fault type ComposedFaultResponseTests.RemovalFault, which these endpoints answer with: HTTP: DELETE /orders/{id:int}");
    }

    /// <summary>
    /// Two fault types an endpoint answers with that write one name are logged as a warning, and the host
    /// starts: an endpoint written with 2.0's types alone, <c>Results&lt;…, FaultProblem&lt;A&gt;, FaultProblem&lt;B&gt;&gt;</c>,
    /// started and answered under 2.0.0, and 2.1.0's refusal stopped a host that only upgraded (reported
    /// 2026-10-14). 3.0.0 refuses it (decision 90).
    /// </summary>
    [Fact]
    public async Task Two_types_sharing_a_member_s_bare_name_are_a_warning_and_the_host_starts()
    {
        var logs = new ListedLogs();
        await using var app = await StartAsync(
            logs: logs,
            endpoints: app => app.MapPut("/orders/{id:int}", Results<NoContent, FaultProblem<OrderFault>, FaultProblem<ClashingFault>> (int id) =>
                id == 1 ? TypedResults.NoContent() : NameTakenAsIn20()));

        (await ProblemAsync(app, "PUT", "/orders/2", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("NameTaken", "the 2.0 endpoint answers as it did");
        logs.Warnings.ShouldHaveSingleItem().ShouldContain("HTTP: PUT /orders/{id:int} answers ComposedFaultResponseTests.OrderFault.NameTaken and ComposedFaultResponseTests.ClashingFault.NameTaken alike, as the fault \"NameTaken\"");
    }

    [Fact]
    public async Task Qualified_names_keep_a_shared_member_name_apart()
    {
        await using var app = await StartAsync(
            qualified: true,
            endpoints: app => app.MapPut("/orders/{id:int}", (int id) =>
                (id == 1 ? Fault<OrderFault, ClashingFault>.Of(ClashingFault.NameTaken) : Fault<OrderFault, ClashingFault>.Of(OrderFault.NameTaken)).ToResult().ToHttpResult()));

        (await ProblemAsync(app, "PUT", "/orders/1", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("ComposedFaultResponseTests.ClashingFault.NameTaken");
        (await ProblemAsync(app, "PUT", "/orders/2", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("ComposedFaultResponseTests.OrderFault.NameTaken");
    }

    /// <summary>
    /// Qualified names meet too, for two types of one nesting chain in two namespaces: the name carries
    /// no namespace, so both write <c>ShopFault.NotFound</c>.
    /// </summary>
    [Fact]
    public async Task Two_types_writing_one_qualified_name_are_a_warning()
    {
        var logs = new ListedLogs();
        await using var app = await StartAsync(
            logs: logs,
            qualified: true,
            services: services =>
            {
                services.AddFaultResponses<Billing.ShopFault>(_ => StatusCodes.Status404NotFound);
                services.AddFaultResponses<Shipping.ShopFault>(_ => StatusCodes.Status404NotFound);
            },
            endpoints: app => app.MapGet("/shops/{id:int}", (int id) => Result<Fault<Billing.ShopFault, Shipping.ShopFault>>.Success().ToHttpResult()));

        logs.Warnings.ShouldHaveSingleItem().ShouldContain("HTTP: GET /shops/{id:int} answers CodoMetis.TypeKit.AspNetCore.Tests.Billing.ShopFault.NotFound and " +
                                      "CodoMetis.TypeKit.AspNetCore.Tests.Shipping.ShopFault.NotFound alike, as the fault \"ShopFault.NotFound\": rename one type");
    }

    [Fact]
    public async Task A_shared_name_the_endpoint_leaves_out_of_one_type_does_not_stop_the_host()
    {
        await using var app = await StartAsync(endpoints: app =>
            app.MapPut("/orders/{id:int}", (int id) => Result<Fault<OrderFault, ClashingFault>>.Success().ToHttpResult())
               .WithoutFaults(ClashingFault.NameTaken));

        Statuses(await DocumentAsync(app), "/orders/{id}", "put").ShouldBe(["204", "404", "409", "422"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Closed_sets_compose_answered_by_their_kinds(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, qualified: true, endpoints: app => app.MapPut("/sets/{id:int}", (int id) => (id switch
        {
            1 => Fault<ProbeOrderFault, ProbeConcurrencyFault>.Of(ProbeOrderFault.NameTaken),
            2 => Fault<ProbeOrderFault, ProbeConcurrencyFault>.Of(ProbeConcurrencyFault.PreconditionFailed),
            _ => Fault<ProbeOrderFault, ProbeConcurrencyFault>.Of(ProbeConcurrencyFault.Conflict),
        }).ToResult().ToHttpResult()));

        (await ProblemAsync(app, "PUT", "/sets/1", HttpStatusCode.Conflict))["fault"]!.GetValue<string>().ShouldBe("ProbeOrderFault.NameTaken");
        (await ProblemAsync(app, "PUT", "/sets/2", HttpStatusCode.PreconditionFailed))["fault"]!.GetValue<string>().ShouldBe("ProbeConcurrencyFault.PreconditionFailed");
        (await ProblemAsync(app, "PUT", "/sets/3", HttpStatusCode.Conflict))["detail"]!.GetValue<string>().ShouldBe("The order changed meanwhile; read it again.");

        Statuses(await DocumentAsync(app), "/sets/{id}", "put").ShouldBe(["204", "403", "404", "409", "412"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task An_MVC_action_is_answered_and_documented_the_same(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version, mvc: [typeof(OrdersController)]);

        await ProblemAsync(app, "PUT", "/mvc/orders/3", HttpStatusCode.PreconditionFailed);
        Statuses(await DocumentAsync(app), "/mvc/orders/{id}", "put").ShouldBe(["204", "404", "409", "412"]);
    }

    [Fact]
    public void A_handler_s_test_reads_the_problem_of_either_type_without_a_host()
    {
        Rename(2).ToHttpResult().Result.ShouldBeOfType<FaultProblem<OrderFault>>().Fault.ShouldBe(OrderFault.NameTaken);
        Rename(3).ToHttpResult().Result.ShouldBeOfType<FaultProblem<ConcurrencyFault>>().Rule.ShouldBe("ComposedFaultResponseTests.RemovalFault.ReasonRequired");
    }

    private static async Task<WebApplication> StartAsync(
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1,
        Action<WebApplication>? endpoints = null,
        Type[]? mvc = null,
        bool removal = true,
        bool qualified = false,
        bool typed = false,
        Action<IServiceCollection>? services = null,
        ILoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (logs is not null) builder.Logging.AddProvider(logs);
        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = version;
            options.AddTypeKit();
        });
        builder.Services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = qualified);
        services?.Invoke(builder.Services);
        builder.Services.AddFaultResponses<OrderFault>(fault => Typed(typed, fault switch
        {
            OrderFault.NotFound => StatusCodes.Status404NotFound,
            OrderFault.NameTaken => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        }, $"OrderFault.{fault}"));
        builder.Services.AddFaultResponses<ConcurrencyFault>(fault => Typed(typed, fault switch
        {
            ConcurrencyFault.Conflict => StatusCodes.Status409Conflict,
            ConcurrencyFault.Stale => StatusCodes.Status412PreconditionFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        }, $"ConcurrencyFault.{fault}"));
        if (removal)
        {
            builder.Services.AddFaultResponses<RemovalFault>(fault => Typed(typed, fault switch
            {
                RemovalFault.StillReferenced => StatusCodes.Status409Conflict,
                RemovalFault.ReasonRequired => StatusCodes.Status422UnprocessableEntity,
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            }, $"RemovalFault.{fault}"));
        }
        builder.Services.AddFaultResponses<ClashingFault>(fault => fault switch
        {
            ClashingFault.NameTaken => StatusCodes.Status409Conflict,
            ClashingFault.Other => StatusCodes.Status422UnprocessableEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });
        builder.Services.AddFaultResponses<ProbeOrderFault>();
        builder.Services.AddFaultResponses<ProbeConcurrencyFault>();
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
        if (mvc is not null) app.MapControllers();

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

    private static FaultResponse Typed(bool typed, int status, string name) =>
        typed ? FaultResponse.Status(status).WithType(new Uri($"https://problems.example/{name}")) : status;

    private static async Task<HttpStatusCode> StatusAsync(WebApplication app, string method, string path)
    {
        using var response = await app.GetTestClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>A problem as a 2.0 handler made one, from a single fault type's <c>ToHttpResult()</c>.</summary>
    private static FaultProblem<OrderFault> NameTakenAsIn20() =>
        (FaultProblem<OrderFault>)((Result<OrderFault>)Result.Error(OrderFault.NameTaken)).ToHttpResult().Result;

    private static async Task<JsonNode> ProblemAsync(WebApplication app, string method, string path, HttpStatusCode status)
    {
        using var response = await app.GetTestClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status, path);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    // Not ordered here: the document's own order is part of what is held.
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
    public sealed class OrdersController : ControllerBase
    {
        [HttpPut("/mvc/orders/{id:int}")]
        public Results<NoContent, FaultProblem<OrderFault>, FaultProblem<ConcurrencyFault>> Put(int id) => Rename(id).ToHttpResult();
    }
}

file static class ComposedResults
{
    public static Result<Fault<T1, T2>> ToResult<T1, T2>(this Fault<T1, T2> fault) where T1 : notnull where T2 : notnull => fault;
}

/// <summary>The warnings a host logs, kept in memory.</summary>
internal sealed class ListedLogs : ILoggerProvider, ILogger
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _warnings = new();

    public IReadOnlyList<string> Warnings => [.. _warnings];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) _warnings.Enqueue(formatter(state, exception));
    }

    public void Dispose() { }
}
