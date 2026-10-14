using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
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

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// The fault responses a client can act on (spikes/FaultResponses): a response per member, a closed set
/// as a fault type answered by its kinds, a sentence per occurrence, a status per endpoint, the
/// endpoint's own success, and the fault on the request while its problem is written. Each claim of the
/// spike is a test here; decision 38's default response is held by <see cref="FaultResponseTests"/>.
/// </summary>
public sealed class FaultResponsePartsTests
{
    public enum OrderFault { NotFound, NameTaken }

    /// <summary>The rules behind <see cref="OrderFault.NameTaken"/>, named on the problem as its <c>rule</c>.</summary>
    public enum NameRule { Reserved }

    public sealed record Order(int Id, string Name);

    private static readonly Uri NameTakenType = new("https://example.com/problems/name-taken");

    private static Result<Order, OrderFault> Rename(int id) =>
        id == 1 ? new Order(1, "renamed") : Result.Error(id == 2 ? OrderFault.NameTaken : OrderFault.NotFound);

    private static Result<Order, Fault<OrderFault>> RenameWithDetail(int id) =>
        id == 1 ? new Order(1, "renamed") : Result.Error(Fault.Of(OrderFault.NameTaken, "Order 7 is called that."));

    private static Result<Order, Fault<OrderFault>> RenameWithRule(int id) =>
        id == 1 ? new Order(1, "renamed") : Result.Error(Fault.Of(OrderFault.NameTaken, "Order 7 is called that.").WithRule(NameRule.Reserved));

    private static Result<Fault<OrderFault>> ArchiveWithRule(int id) =>
        id == 1 ? Result.Success() : Result.Error(Fault.Of(OrderFault.NameTaken).WithRule(ProbeStatus.Shipped));

    private static Result<Fault<OrderFault>> ArchiveWithDetail(int id) =>
        id == 1 ? Result.Success() : Result.Error(Fault.Of(OrderFault.NameTaken, "Order 7 is called that."));

    private static Result<Order, ProbeOrderFault> Find(int id) => id switch
    {
        1 => new Order(1, "book"),
        2 => Result.Error(ProbeOrderFault.NameTaken),
        3 => Result.Error(ProbeOrderFault.Locked),
        _ => Result.Error(ProbeOrderFault.NotFound),
    };

    private static void Responses(IServiceCollection services)
    {
        services.AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound  => StatusCodes.Status404NotFound,
            OrderFault.NameTaken => FaultResponse.Status(StatusCodes.Status409Conflict).WithType(NameTakenType).WithTitle("The name is taken"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });
        services.AddFaultResponses<ProbeOrderFault>();
    }

    private static void Endpoints(WebApplication app)
    {
        app.MapPut("/orders/{id:int}", (int id) => Rename(id).ToHttpResult());
        app.MapPut("/strict/orders/{id:int}", (int id) => Rename(id).ToHttpResult()).WithFaultStatus(OrderFault.NameTaken, StatusCodes.Status422UnprocessableEntity);
        app.MapPut("/detailed/orders/{id:int}", (int id) => RenameWithDetail(id).ToHttpResult());
        app.MapGet("/catalog/{id:int}", (int id) => Find(id).ToHttpResult());
        app.MapPost("/orders", () => Rename(1).ToHttpResult(order => TypedResults.Created($"/orders/{order.Id}", order)));
        app.MapGet("/elsewhere", () => TypedResults.Problem(statusCode: StatusCodes.Status418ImATeapot));
    }

    // 1: the FaultResponse overload checks every member where it is mapped, as the int one does.
    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(600)]
    public void A_response_per_member_is_checked_where_it_is_mapped(int status)
    {
        var refusal = Should.Throw<ArgumentException>(() => new ServiceCollection().AddFaultResponses<OrderFault>(
            fault => fault == OrderFault.NameTaken ? FaultResponse.Status(status).WithType(NameTakenType) : 404));

        refusal.ParamName.ShouldBe("responseOf");
        refusal.Message.ShouldStartWith($"FaultResponsePartsTests.OrderFault.NameTaken is mapped to {status}, which is not an error status (400 to 599).");
    }

    /// <summary>
    /// Each kind's status, as the README and <c>FaultKind</c>'s own documentation give them; a kind added
    /// without a status fails here rather than at the first host that maps a member of it.
    /// </summary>
    [Fact]
    public void Every_kind_is_answered_with_its_status()
    {
        var statuses = Enum.GetValues<FaultKind>().ToDictionary(kind => kind, kind => FaultResponse.For(kind).StatusCode);

        statuses.ShouldBe(new Dictionary<FaultKind, int>
        {
            [FaultKind.NotFound]           = 404,
            [FaultKind.Conflict]           = 409,
            [FaultKind.Invalid]            = 422,
            [FaultKind.Forbidden]          = 403,
            [FaultKind.Unavailable]        = 503,
            [FaultKind.PreconditionFailed] = 412,
            [FaultKind.Malformed]          = 400,
        });
        Should.Throw<ArgumentOutOfRangeException>(() => FaultResponse.For(default));
    }

    /// <summary>A kind is required: a member whose kind is <c>default</c> has none, and the set is refused where it is mapped, naming the member.</summary>
    [Fact]
    public void A_fault_set_with_a_member_of_no_kind_is_refused_where_it_is_mapped()
    {
        var refusal = Should.Throw<ArgumentException>(() => new ServiceCollection().AddFaultResponses<ProbeKindlessFault>());

        refusal.Message.ShouldStartWith("ProbeKindlessFault.Unset: 0 is no FaultKind");
    }

    // 2: a type URI and a title reach the body and the operation's problem response; nothing of the input does.
    [Fact]
    public async Task A_type_and_a_title_reach_the_problem_and_the_document()
    {
        await using var app = await StartAsync();

        var body = await ProblemAsync(app, "/orders/2", HttpStatusCode.Conflict);
        body["type"]!.GetValue<string>().ShouldBe(NameTakenType.ToString());
        body["title"]!.GetValue<string>().ShouldBe("The name is taken");
        body["fault"]!.GetValue<string>().ShouldBe("NameTaken");

        var conflict = (await DocumentAsync(app))["paths"]!["/orders/{id}"]!["put"]!["responses"]!["409"]!;
        conflict["description"]!.GetValue<string>().ShouldBe($"Conflict. Problem types: {NameTakenType}");
    }

    public enum TypedFault { Escaped }

    /// <summary>
    /// A problem type is written as the URI it is: <see cref="Uri.ToString"/> unescaped <c>%20</c> and an
    /// escaped non-ASCII character, and the problem's <c>type</c> was then no URI at all, in the response
    /// and in the document's description alike.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/problems/a%20b", "https://example.com/problems/a%20b")]
    [InlineData("https://example.com/problems/%C3%BCber", "https://example.com/problems/%C3%BCber")]
    [InlineData("https://example.com/problems/a%2Fb", "https://example.com/problems/a%2Fb")]
    [InlineData("urn:example:problems:a%20b", "urn:example:problems:a%20b")]
    [InlineData("https://example.com/problems/a b", "https://example.com/problems/a%20b")]   // given unescaped: written as a URI
    [InlineData("/problems/a%20b", "/problems/a%20b")]                                     // relative: as given
    public async Task A_problem_type_is_written_with_its_escapes(string given, string written)
    {
        var type = new Uri(given, UriKind.RelativeOrAbsolute);
        await using var app = await StartAsync(
            services: services =>
            {
                Responses(services);
                services.AddFaultResponses<TypedFault>(_ => FaultResponse.Status(StatusCodes.Status409Conflict).WithType(type));
            },
            endpoints: app => app.MapGet("/typed", () => Result<int, TypedFault>.Error(TypedFault.Escaped).ToHttpResult()));

        (await ProblemAsync(app, "/typed", HttpStatusCode.Conflict))["type"]!.GetValue<string>().ShouldBe(written);
        (await DocumentAsync(app))["paths"]!["/typed"]!["get"]!["responses"]!["409"]!["description"]!.GetValue<string>()
            .ShouldBe($"Conflict. Problem types: {written}");
    }

    // 3: ToHttpResult over Result<T, Fault<TFault>>, Result<T, TFault> and a closed set's resolves without type arguments.
    [Fact]
    public void ToHttpResult_resolves_for_an_enum_a_fault_and_a_closed_set_without_type_arguments()
    {
        Rename(2).ToHttpResult().Result.ShouldBeOfType<FaultProblem<OrderFault>>().Fault.ShouldBe(OrderFault.NameTaken);

        var detailed = RenameWithDetail(2).ToHttpResult().Result.ShouldBeOfType<FaultProblem<OrderFault>>();
        detailed.Fault.ShouldBe(OrderFault.NameTaken);
        detailed.Detail.ShouldBe("Order 7 is called that.");

        ReferenceEquals(Find(3).ToHttpResult().Result.ShouldBeOfType<FaultProblem<ProbeOrderFault>>().Fault, ProbeOrderFault.Locked).ShouldBeTrue();
        Rename(1).ToHttpResult(order => TypedResults.Created($"/orders/{order.Id}", order)).Result.ShouldBeOfType<Created<Order>>();
    }

    // 4: a detail reaches the body only when set, and the default response is unchanged.
    [Fact]
    public async Task A_detail_reaches_the_problem_only_when_there_is_one()
    {
        await using var app = await StartAsync();

        var plain = (await ProblemAsync(app, "/orders/2", HttpStatusCode.Conflict)).AsObject();
        plain.Select(member => member.Key).Order().ShouldBe(["fault", "status", "title", "traceId", "type"]);

        var detailed = (await ProblemAsync(app, "/detailed/orders/2", HttpStatusCode.Conflict)).AsObject();
        detailed["detail"]!.GetValue<string>().ShouldBe("Order 7 is called that.");
        detailed["fault"]!.GetValue<string>().ShouldBe("NameTaken", "the mapping stays keyed on the enum");
    }

    /// <summary>
    /// Every overload over a <see cref="Fault{TFault}"/> carries the occurrence's detail to the problem:
    /// the valued one, the command, and each with the endpoint's own success.
    /// </summary>
    [Theory]
    [InlineData("PUT", "/detailed/orders/2")]            // Result<T, Fault<TFault>>
    [InlineData("DELETE", "/detailed/orders/2")]         // Result<Fault<TFault>>
    [InlineData("POST", "/detailed/orders/2/copies")]    // Result<T, Fault<TFault>>, with the endpoint's success
    [InlineData("POST", "/detailed/orders/2/archive")]   // Result<Fault<TFault>>, with the endpoint's success
    public async Task An_occurrence_s_detail_reaches_the_problem_through_every_overload(string method, string path)
    {
        await using var app = await StartAsync(endpoints: app =>
        {
            app.MapDelete("/detailed/orders/{id:int}", (int id) => ArchiveWithDetail(id).ToHttpResult());
            app.MapPost("/detailed/orders/{id:int}/copies", (int id) => RenameWithDetail(id).ToHttpResult(order => TypedResults.Created($"/orders/{order.Id}", order)));
            app.MapPost("/detailed/orders/{id:int}/archive", (int id) => ArchiveWithDetail(id).ToHttpResult(() => TypedResults.Accepted("/jobs/7")));
        });

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{method} {path}");
        problem["fault"]!.GetValue<string>().ShouldBe("NameTaken");
        problem.AsObject().ContainsKey("detail").ShouldBeTrue($"{method} {path} lost the occurrence's detail: {problem.ToJsonString()}");
        problem["detail"]!.GetValue<string>().ShouldBe("Order 7 is called that.");
    }

    /// <summary>
    /// The rule that refused reaches the problem as <c>rule</c> through every overload over a
    /// <see cref="Fault{TFault}"/>, an enum's qualified after its nesting chain and a closed set's as its
    /// <c>QualifiedName</c>, whatever <c>QualifiedFaultNames</c> says, beside the fault the mapping and
    /// the status stay keyed on. A fault without one writes none
    /// (<see cref="A_detail_reaches_the_problem_only_when_there_is_one"/>).
    /// </summary>
    [Theory]
    [InlineData("PUT", "/ruled/orders/2", "FaultResponsePartsTests.NameRule.Reserved")]              // Result<T, Fault<TFault>>
    [InlineData("DELETE", "/ruled/orders/2", "ProbeStatus.Shipped")]                                 // Result<Fault<TFault>>, a closed set's member
    [InlineData("POST", "/ruled/orders/2/copies", "FaultResponsePartsTests.NameRule.Reserved")]      // with the endpoint's success
    [InlineData("POST", "/ruled/orders/2/archive", "ProbeStatus.Shipped")]                           // with the endpoint's success
    public async Task An_occurrence_s_rule_reaches_the_problem_through_every_overload(string method, string path, string rule)
    {
        await using var app = await StartAsync(endpoints: app =>
        {
            app.MapPut("/ruled/orders/{id:int}", (int id) => RenameWithRule(id).ToHttpResult());
            app.MapDelete("/ruled/orders/{id:int}", (int id) => ArchiveWithRule(id).ToHttpResult());
            app.MapPost("/ruled/orders/{id:int}/copies", (int id) => RenameWithRule(id).ToHttpResult(order => TypedResults.Created($"/orders/{order.Id}", order)));
            app.MapPost("/ruled/orders/{id:int}/archive", (int id) => ArchiveWithRule(id).ToHttpResult(() => TypedResults.Accepted("/jobs/7")));
        });

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{method} {path}");
        problem["fault"]!.GetValue<string>().ShouldBe("NameTaken");
        problem["rule"].ShouldNotBeNull($"{method} {path} lost the occurrence's rule: {problem.ToJsonString()}").GetValue<string>().ShouldBe(rule);
    }

    /// <summary>
    /// A closed set answered by its kinds: 404, 409 and 403 with nothing mapped by hand, and a member's
    /// description as the problem's detail, where the member has one.
    /// </summary>
    [Fact]
    public async Task A_fault_set_is_answered_by_its_kinds_and_its_descriptions()
    {
        await using var app = await StartAsync();

        var notFound = await ProblemAsync(app, "/catalog/9", HttpStatusCode.NotFound);
        notFound["fault"]!.GetValue<string>().ShouldBe("NotFound");
        notFound["detail"]!.GetValue<string>().ShouldBe("The order does not exist.");

        (await ProblemAsync(app, "/catalog/2", HttpStatusCode.Conflict))["detail"]!.GetValue<string>().ShouldBe("Another order has that name.");
        (await ProblemAsync(app, "/catalog/3", HttpStatusCode.Forbidden)).AsObject().ContainsKey("detail").ShouldBeFalse("a member that must not explain itself has no detail");

        var responses = (await DocumentAsync(app))["paths"]!["/catalog/{id}"]!["get"]!["responses"]!.AsObject();
        responses.Select(response => response.Key).Order().ShouldBe(["200", "403", "404", "409"]);
    }

    // 5: WithFaultStatus changes the status for that endpoint only, in the response and in its document entry.
    [Fact]
    public async Task A_status_per_endpoint_changes_that_endpoint_alone()
    {
        await using var app = await StartAsync();

        await ProblemAsync(app, "/strict/orders/2", HttpStatusCode.UnprocessableEntity);
        await ProblemAsync(app, "/orders/2", HttpStatusCode.Conflict);

        var paths = (await DocumentAsync(app))["paths"]!;
        paths["/strict/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "404", "422"]);
        paths["/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "404", "409"]);
    }

    /// <summary>
    /// A route group's status reaches every endpoint of it, and an endpoint's own replaces it for that
    /// endpoint: the last status the metadata sets, as ASP.NET reads metadata, in the response and the document.
    /// </summary>
    [Fact]
    public async Task A_group_s_status_reaches_its_endpoints_and_an_endpoint_s_own_replaces_it()
    {
        await using var app = await StartAsync(endpoints: app =>
        {
            var archive = app.MapGroup("/archive").WithFaultStatus(OrderFault.NotFound, StatusCodes.Status410Gone);
            archive.MapPut("/orders/{id:int}", (int id) => Rename(id).ToHttpResult());
            archive.MapPut("/restored/orders/{id:int}", (int id) => Rename(id).ToHttpResult()).WithFaultStatus(OrderFault.NotFound, StatusCodes.Status404NotFound);
        });

        (await ProblemAsync(app, "/archive/orders/9", HttpStatusCode.Gone))["fault"]!.GetValue<string>().ShouldBe("NotFound");
        await ProblemAsync(app, "/archive/restored/orders/9", HttpStatusCode.NotFound);
        await ProblemAsync(app, "/archive/restored/orders/2", HttpStatusCode.Conflict);

        var paths = (await DocumentAsync(app))["paths"]!;
        paths["/archive/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "409", "410"]);
        paths["/archive/restored/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "404", "409"]);
    }

    /// <summary>The same for MVC: a controller's attribute reaches every action of it, and an action's own replaces it.</summary>
    [Fact]
    public async Task A_controller_s_status_attribute_reaches_its_actions_and_an_action_s_own_replaces_it()
    {
        await using var app = await StartAsync(mvc: typeof(ArchiveController));

        await ProblemAsync(app, "/mvc/archive/orders/9", HttpStatusCode.Gone);
        await ProblemAsync(app, "/mvc/archive/restored/orders/9", HttpStatusCode.NotFound);

        var paths = (await DocumentAsync(app))["paths"]!;
        paths["/mvc/archive/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "409", "410"]);
        paths["/mvc/archive/restored/orders/{id}"]!["put"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "404", "409"]);
    }

    [Fact]
    public void A_status_per_endpoint_is_an_error_status() =>
        Should.Throw<ArgumentOutOfRangeException>(() => WebApplication.CreateBuilder().Build().MapGet("/x", () => Rename(1).ToHttpResult()).WithFaultStatus(OrderFault.NameTaken, 200))
              .ParamName.ShouldBe("status");

    // 6: the startup check refuses a WithFaultStatus for a fault type the endpoint does not answer with.
    [Fact]
    public async Task A_status_for_a_fault_type_the_endpoint_does_not_answer_with_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(endpoints: app =>
            app.MapGet("/stray/{id:int}", (int id) => Find(id).ToHttpResult()).WithFaultStatus(OrderFault.NameTaken, 422)));

        refusal.Message.ShouldContain("WithFaultStatus(FaultResponsePartsTests.OrderFault.NameTaken, 422) is set on");
        refusal.Message.ShouldContain("which answers with no FaultResponsePartsTests.OrderFault");
    }

    // 7: MVC: the attribute reaches the response and the operation transformer.
    [Fact]
    public async Task An_MVC_action_s_status_attribute_reaches_the_response_and_the_document()
    {
        await using var app = await StartAsync(mvc: typeof(RenamesController));

        await ProblemAsync(app, "/mvc/orders/2", HttpStatusCode.UnprocessableEntity);
        (await DocumentAsync(app))["paths"]!["/mvc/orders/{id}"]!["put"]!["responses"]!.AsObject()
            .Select(response => response.Key).Order().ShouldBe(["200", "404", "422"]);
    }

    [Fact]
    public async Task An_attribute_naming_no_member_stops_the_host()
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(mvc: typeof(MisnamedController)));

        refusal.Message.ShouldContain("[FaultStatus<FaultResponsePartsTests.OrderFault>(\"Taken\", 422)] names no member of FaultResponsePartsTests.OrderFault");
    }

    // 8: the endpoint's own success: 201 with its body in the document, and a Location header.
    [Fact]
    public async Task The_endpoint_s_own_success_is_answered_and_described()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().PostAsync("/orders", content: null, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldBe(new Uri("/orders/1", UriKind.Relative));

        var responses = (await DocumentAsync(app))["paths"]!["/orders"]!["post"]!["responses"]!.AsObject();
        responses.Select(entry => entry.Key).Order().ShouldBe(["201", "404", "409"]);
        responses["201"]!["content"]!["application/json"]!["schema"].ShouldNotBeNull("the success's body is described");
    }

    // 9: CustomizeProblemDetails sees the fault for a problem ToHttpResult wrote, and for no other.
    [Fact]
    public async Task CustomizeProblemDetails_sees_the_fault_of_a_problem_ToHttpResult_wrote_and_of_no_other()
    {
        await using var app = await StartAsync(services: services =>
        {
            Responses(services);
            services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["code"] = context.HttpContext.Features.Get<IFaultFeature>() is { } fault
                    ? $"{fault.FaultType.Name}:{fault.Name}:{fault.Detail ?? "-"}"
                    : "none");
        });

        (await ProblemAsync(app, "/detailed/orders/2", HttpStatusCode.Conflict))["code"]!.GetValue<string>().ShouldBe("OrderFault:NameTaken:Order 7 is called that.");
        (await ProblemAsync(app, "/catalog/9", HttpStatusCode.NotFound))["code"]!.GetValue<string>().ShouldBe("ProbeOrderFault:NotFound:The order does not exist.");
        (await ProblemAsync(app, "/elsewhere", (HttpStatusCode)418))["code"]!.GetValue<string>().ShouldBe("none");
    }

    /// <summary>The feature carries the rule for a host that writes it its own way, and none where the fault names none.</summary>
    [Fact]
    public async Task CustomizeProblemDetails_sees_the_rule()
    {
        await using var app = await StartAsync(
            services: services =>
            {
                Responses(services);
                services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
                    context.ProblemDetails.Extensions["code"] = context.HttpContext.Features.Get<IFaultFeature>() is { } fault ? fault.Rule ?? "-" : "none");
            },
            endpoints: app => app.MapPut("/ruled/orders/{id:int}", (int id) => RenameWithRule(id).ToHttpResult()));

        (await ProblemAsync(app, "/ruled/orders/2", HttpStatusCode.Conflict))["code"]!.GetValue<string>().ShouldBe("FaultResponsePartsTests.NameRule.Reserved");
        (await ProblemAsync(app, "/detailed/orders/2", HttpStatusCode.Conflict))["code"]!.GetValue<string>().ShouldBe("-");
    }

    /// <summary>The rule joined the feature with a default, so a host's or a test's own implementation still builds, and names none.</summary>
    [Fact]
    public void A_fault_feature_of_the_host_s_own_names_no_rule() =>
        ((IFaultFeature)new HostFeature()).Rule.ShouldBeNull();

    private sealed class HostFeature : IFaultFeature
    {
        public Type FaultType => typeof(OrderFault);
        public object Fault => OrderFault.NotFound;
        public string Name => nameof(OrderFault.NotFound);
        public string? Detail => null;
    }

    /// <summary>The feature is on the request only while its problem is written: whatever the request writes afterwards does not see it.</summary>
    [Fact]
    public async Task The_fault_is_off_the_request_once_its_problem_is_written()
    {
        var after = new List<IFaultFeature?>();
        await using var app = await StartAsync(endpoints: endpoints =>
            endpoints.MapPut("/observed/orders/{id:int}", (int id) => new Observed(Rename(id).ToHttpResult(), after)));

        await ProblemAsync(app, "/observed/orders/2", HttpStatusCode.Conflict);

        after.ShouldHaveSingleItem().ShouldBeNull();
    }

    /// <summary>Executes the endpoint's result, then records what the request's features hold.</summary>
    private sealed class Observed(IResult inner, List<IFaultFeature?> after) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await inner.ExecuteAsync(httpContext);
            after.Add(httpContext.Features.Get<IFaultFeature>());
        }
    }

    // 10: qualified fault names are opt-in; the default stays the bare member name.
    [Fact]
    public async Task Qualified_fault_names_are_opt_in()
    {
        await using var app = await StartAsync(services: services =>
        {
            Responses(services);
            services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);
        });

        (await ProblemAsync(app, "/orders/9", HttpStatusCode.NotFound))["fault"]!.GetValue<string>().ShouldBe("FaultResponsePartsTests.OrderFault.NotFound");
        (await ProblemAsync(app, "/catalog/9", HttpStatusCode.NotFound))["fault"]!.GetValue<string>().ShouldBe("ProbeOrderFault.NotFound");
    }

    // 11: fault stays a string on the shared problem component; no enum across fault types.
    [Fact]
    public async Task The_problem_component_is_shared_and_says_nothing_of_any_fault_type()
    {
        await using var app = await StartAsync();

        var document = await DocumentAsync(app);
        var problem = document["components"]!["schemas"]!["ProblemDetails"]!.ToJsonString();
        problem.ShouldNotContain("NameTaken");
        problem.ShouldNotContain("\"enum\"");
    }

    /// <summary>A member a cast makes is refused for a closed set too, since an instance that is no member has no answer.</summary>
    [Fact]
    public void A_fault_set_member_is_one_of_its_members()
    {
        var services = new ServiceCollection();
        services.AddFaultResponses<ProbeOrderFault>();
        services.Any(descriptor => descriptor.ServiceType == typeof(FaultResponses<ProbeOrderFault>)).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => services.AddFaultResponses<ProbeOrderFault>()).Message.ShouldContain("mapped already");
    }

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection>? services = null, Action<WebApplication>? endpoints = null, Type? mvc = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        (services ?? Responses)(builder.Services);
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
        Endpoints(app);
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

    private static async Task<JsonNode> ProblemAsync(WebApplication app, string path, HttpStatusCode status)
    {
        using var response = path.Contains("orders/", StringComparison.Ordinal) && !path.StartsWith("/catalog", StringComparison.Ordinal)
            ? await app.GetTestClient().PutAsync(path, content: null, TestContext.Current.CancellationToken)
            : await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status, path);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    private sealed class OnlyControllers(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    [ApiController]
    public sealed class RenamesController : ControllerBase
    {
        [HttpPut("/mvc/orders/{id:int}")]
        [FaultStatus<OrderFault>(nameof(OrderFault.NameTaken), StatusCodes.Status422UnprocessableEntity)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Rename(int id) => FaultResponsePartsTests.Rename(id).ToHttpResult();
    }

    [ApiController]
    [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status410Gone)]
    public sealed class ArchiveController : ControllerBase
    {
        [HttpPut("/mvc/archive/orders/{id:int}")]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Rename(int id) => FaultResponsePartsTests.Rename(id).ToHttpResult();

        [HttpPut("/mvc/archive/restored/orders/{id:int}")]
        [FaultStatus<OrderFault>(nameof(OrderFault.NotFound), StatusCodes.Status404NotFound)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Restore(int id) => FaultResponsePartsTests.Rename(id).ToHttpResult();
    }

    [ApiController]
    public sealed class MisnamedController : ControllerBase
    {
        [HttpPut("/mvc/misnamed/{id:int}")]
        [FaultStatus<OrderFault>("Taken", StatusCodes.Status422UnprocessableEntity)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Rename(int id) => FaultResponsePartsTests.Rename(id).ToHttpResult();
    }
}
