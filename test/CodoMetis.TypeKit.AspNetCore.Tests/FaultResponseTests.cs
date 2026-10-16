using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A <c>Result</c> as an HTTP response (spikes/ResultHttp): a success as its value, a fault as problem
/// details with the status the host mapped, and the same in the OpenAPI document as ASP.NET's own
/// <c>ProducesProblem</c> publishes.
/// </summary>
public sealed partial class FaultResponseTests
{
    public enum OrderFault { NotFound, AlreadyShipped, NotYours }

    public enum OtherFault { Gone }

    public sealed record Order(int Id, string Item);

    private static readonly Dictionary<int, (HttpStatusCode Status, string? Fault)> Answers = new()
    {
        [1] = (HttpStatusCode.OK, null),
        [2] = (HttpStatusCode.Conflict, nameof(OrderFault.AlreadyShipped)),
        [3] = (HttpStatusCode.Forbidden, nameof(OrderFault.NotYours)),
        [9] = (HttpStatusCode.NotFound, nameof(OrderFault.NotFound)),
    };

    public static TheoryData<int> Ids => [.. Answers.Keys];

    private static Result<Order, OrderFault> Find(int id) => id switch
    {
        1 => new Order(1, "Book"),
        2 => Result.Error(OrderFault.AlreadyShipped),
        3 => Result.Error(OrderFault.NotYours),
        42 => Result.Error((OrderFault)42),
        _ => Result.Error(OrderFault.NotFound),
    };

    private static Result<OrderFault> Cancel(int id) => id == 1 ? Result.Success() : Result.Error(OrderFault.AlreadyShipped);

    private static void MapOrders(IServiceCollection services) =>
        services.AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound => StatusCodes.Status404NotFound,
            OrderFault.AlreadyShipped => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status403Forbidden,
        });

    private static void MapOrderProblemTypes(IServiceCollection services) =>
        services.AddFaultResponses<OrderFault>(fault =>
            FaultResponse.Status(fault switch
            {
                OrderFault.NotFound => StatusCodes.Status404NotFound,
                OrderFault.AlreadyShipped => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status403Forbidden,
            }).WithType(new Uri($"https://problems.example/{fault}")));

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task A_success_is_its_value_and_a_fault_is_problem_details_with_its_mapped_status(int id)
    {
        await using var app = await StartAsync();
        var (status, fault) = Answers[id];

        foreach (var path in new[] { $"/orders/{id}", $"/orders-async/{id}" })
        {
            using var response = await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

            response.StatusCode.ShouldBe(status, path);
            if (fault is null)
            {
                response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
                body.ToJsonString().ShouldBe("""{"id":1,"item":"Book"}""");
            }
            else
            {
                response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
                body["status"]!.GetValue<int>().ShouldBe((int)status);
                body["fault"]!.GetValue<string>().ShouldBe(fault);
            }
        }
    }

    [Fact]
    public async Task A_result_without_a_value_is_204_on_success_and_problem_details_on_a_fault()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        (await client.DeleteAsync("/orders/1", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refused = await client.DeleteAsync("/orders/2", TestContext.Current.CancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        refused.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task The_problem_says_the_status_and_the_fault_and_nothing_of_the_request()
    {
        await using var app = await StartAsync();

        var body = (await ProblemAsync(app, "/orders/9")).AsObject();

        body.Select(member => member.Key).Order().ShouldBe(["fault", "status", "title", "traceId", "type"]);
        body["title"]!.GetValue<string>().ShouldBe("Not Found");
    }

    [Fact]
    public void The_fault_is_readable_from_the_result_a_handler_returns()
    {
        Find(2).ToHttpResult().Result.ShouldBeOfType<FaultProblem<OrderFault>>().Fault.ShouldBe(OrderFault.AlreadyShipped);
        Find(1).ToHttpResult().Result.ShouldBeOfType<Ok<Order>>().Value.ShouldBe(new Order(1, "Book"));
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    public async Task The_document_describes_the_value_and_a_problem_per_mapped_status_as_ProducesProblem_does(OpenApiSpecVersion version)
    {
        await using var app = await StartAsync(version: version);
        var document = await DocumentAsync(app);

        foreach (var (method, path, control) in new[]
                 {
                     ("get", "/orders/{id}", "/control/orders/{id}"),
                     ("get", "/orders-async/{id}", "/control/orders/{id}"),
                     ("delete", "/orders/{id}", "/control/orders/{id}"),
                 })
        {
            var responses = Responses(document, path, method);
            var expected = Responses(document, control, method);

            string[] statuses = [method == "get" ? "200" : "204", "403", "404", "409"];
            responses.Select(r => r.Key).Order().ToArray().ShouldBe(statuses, $"{method} {path}");
            foreach (var (status, response) in responses)
                JsonNode.DeepEquals(response, expected[status]).ShouldBeTrue($"{method} {path} {status}: {response?.ToJsonString()} differs from ProducesProblem's {expected[status]?.ToJsonString()}");
        }

        document["components"]!["schemas"]!["ProblemDetails"].ShouldNotBeNull();
    }

    /// <summary>Without ASP.NET's own problem responses beside them, the component is this package's to add.</summary>
    [Fact]
    public async Task The_problem_component_is_added_where_no_other_endpoint_made_one()
    {
        await using var app = await StartAsync(controls: false);
        var document = await DocumentAsync(app);

        var schema = Responses(document, "/orders/{id}", "get")["404"]!["content"]!["application/problem+json"]!["schema"]!;

        schema["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProblemDetails");
        var component = document["components"]!["schemas"]!["ProblemDetails"].ShouldNotBeNull();
        component["properties"]!["status"].ShouldNotBeNull();
    }

    /// <summary>
    /// ASP.NET reads endpoint metadata from the declared return type, as for its own typed results: a
    /// handler typed <see cref="IResult"/> loses it. Pinned, because the README says so.
    /// </summary>
    [Fact]
    public async Task A_handler_typed_IResult_answers_alike_but_its_operation_lists_no_problem()
    {
        await using var app = await StartAsync(endpoints: app => app.MapGet("/untyped/{id:int}", IResult (int id) => Find(id).ToHttpResult()));

        using var refused = await app.GetTestClient().GetAsync("/untyped/2", TestContext.Current.CancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        Responses(await DocumentAsync(app), "/untyped/{id}", "get").Select(r => r.Key).ShouldNotContain("409");
    }

    [Fact]
    public void An_uninitialized_result_is_refused_rather_than_answered()
    {
        var uninitialized = (new Result<Order, OrderFault>[1])[0];
        var uninitializedCommand = (new Result<OrderFault>[1])[0];

        Should.Throw<InvalidOperationException>(() => uninitialized.ToHttpResult());
        Should.Throw<InvalidOperationException>(() => uninitializedCommand.ToHttpResult());
    }

    [Fact]
    public async Task An_MVC_action_answers_and_is_documented_as_the_minimal_API()
    {
        await using var app = await StartAsync(mvc: true);
        var client = app.GetTestClient();

        (await client.GetStringAsync("/mvc/orders/1", TestContext.Current.CancellationToken)).ShouldBe("""{"id":1,"item":"Book"}""");
        using (var refused = await client.GetAsync("/mvc/orders/2", TestContext.Current.CancellationToken))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            JsonNode.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["fault"]!.GetValue<string>().ShouldBe("AlreadyShipped");
        }

        var document = await DocumentAsync(app);
        var minimal = Responses(document, "/orders/{id}", "get");
        var mvc = Responses(document, "/mvc/orders/{id}", "get");

        mvc.Select(r => r.Key).Order().ShouldBe(minimal.Select(r => r.Key).Order());
        foreach (var (status, response) in mvc)
            JsonNode.DeepEquals(response, minimal[status]).ShouldBeTrue(status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_host_with_an_endpoint_whose_fault_type_is_not_mapped_does_not_start(bool mvc)
    {
        var refusal = await Should.ThrowAsync<InvalidOperationException>(() =>
            StartAsync(services => services.AddFaultResponses<OtherFault>(_ => StatusCodes.Status410Gone), mvc: mvc));

        refusal.Message.ShouldStartWith("No HTTP status is mapped for the fault type FaultResponseTests.OrderFault, which these endpoints answer with: ");
        refusal.Message.ShouldContain("HTTP: GET /orders/{id:int}");
        refusal.Message.ShouldContain("HTTP: DELETE /orders/{id:int}");
        if (mvc) refusal.Message.ShouldContain("OrdersController.Get");
        refusal.Message.ShouldContain("services.AddFaultResponses<FaultResponseTests.OrderFault>(fault => …)");
    }

    /// <summary>
    /// The startup check comes with <c>AddFaultResponses</c>. A host that never calls it learns of the
    /// missing mapping from the first fault, and from the document, never from a plausible status.
    /// </summary>
    [Fact]
    public async Task Without_any_mapping_a_fault_and_the_document_are_refused()
    {
        await using var app = await StartAsync(services: _ => { }, map: false);
        var client = app.GetTestClient();

        (await client.GetStringAsync("/orders/1", TestContext.Current.CancellationToken)).ShouldBe("""{"id":1,"item":"Book"}""");

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => client.GetAsync("/orders/2", TestContext.Current.CancellationToken));
        refusal.Message.ShouldStartWith("No HTTP status is mapped for the fault type FaultResponseTests.OrderFault. Map each of its members with services.AddFaultResponses<FaultResponseTests.OrderFault>(fault => …).");

        var document = await Should.ThrowAsync<InvalidOperationException>(() => client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        document.Message.ShouldStartWith("No HTTP status is mapped for the fault type FaultResponseTests.OrderFault.");
    }

    [Fact]
    public async Task A_value_no_member_names_is_refused_rather_than_answered()
    {
        await using var app = await StartAsync();

        var refusal = await Should.ThrowAsync<InvalidOperationException>(() => app.GetTestClient().GetAsync("/orders/42", TestContext.Current.CancellationToken));

        refusal.Message.ShouldStartWith("FaultResponseTests.OrderFault has no member with the value 42, so no status is mapped for it.");
    }

    [Fact]
    public void A_fault_type_is_mapped_once()
    {
        var services = new ServiceCollection();
        MapOrders(services);

        Should.Throw<InvalidOperationException>(() => MapOrders(services))
              .Message.ShouldBe("The fault type FaultResponseTests.OrderFault is mapped already. Map it once, with a status for each member.");
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status302Found)]
    [InlineData(600)]
    public void A_member_mapped_to_a_status_that_is_no_error_is_refused_where_it_is_mapped(int status)
    {
        var refusal = Should.Throw<ArgumentException>(() => new ServiceCollection().AddFaultResponses<OrderFault>(
            fault => fault == OrderFault.NotYours ? status : StatusCodes.Status404NotFound));

        refusal.ParamName.ShouldBe("statusOf");
        refusal.Message.ShouldStartWith($"FaultResponseTests.OrderFault.NotYours is mapped to {status}, which is not an error status (400 to 599).");
    }

    [Fact]
    public void A_mapping_that_throws_for_a_member_fails_where_it_is_mapped() =>
#pragma warning disable CS8509 // the defect under test: NotYours has no arm
        Should.Throw<SwitchExpressionException>(() => new ServiceCollection().AddFaultResponses<OrderFault>(fault => fault switch
        {
            OrderFault.NotFound => StatusCodes.Status404NotFound,
            OrderFault.AlreadyShipped => StatusCodes.Status409Conflict,
        }));
#pragma warning restore CS8509

    [Fact]
    public async Task The_hosts_problem_details_customization_applies()
    {
        await using var app = await StartAsync(services =>
        {
            services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["tenant"] = "north");
            MapOrders(services);
        });

        var body = await ProblemAsync(app, "/orders/9");

        body["tenant"]!.GetValue<string>().ShouldBe("north");
        body["fault"]!.GetValue<string>().ShouldBe("NotFound");
    }

    /// <summary>
    /// Inline, as ProducesProblem writes it, with <c>fault</c> and <c>rule</c> beside ASP.NET's members; ProducesProblem's
    /// own inline schema is another instance and stays without it.
    /// </summary>
    [Fact]
    public async Task A_host_that_inlines_problem_details_gets_them_inline_as_ProducesProblem_does()
    {
        await using var app = await StartAsync(openApi: options =>
            options.CreateSchemaReferenceId = type => type.Type == typeof(ProblemDetails) ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(type));
        var document = await DocumentAsync(app);

        var responses = Responses(document, "/orders/{id}", "get");
        var control = Responses(document, "/control/orders/{id}", "get")["404"]!;
        ProblemSchema(control)["properties"]!.AsObject().ContainsKey("fault").ShouldBeFalse("ASP.NET's own problem response is left as it writes it");

        var expected = control.DeepClone();
        ProblemSchema(expected)["properties"]!["fault"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = FaultResponsesOperationTransformer.FaultDescription(qualified: false),
        };
        ProblemSchema(expected)["properties"]!["rule"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = FaultResponsesOperationTransformer.RuleDescription,
        };

        ProblemSchema(responses["404"]!)["$ref"].ShouldBeNull();
        JsonNode.DeepEquals(responses["404"], expected).ShouldBeTrue(responses["404"]!.ToJsonString());
        document["components"]!["schemas"]!["ProblemDetails"].ShouldBeNull();
    }

    /// <summary>
    /// The member a client of a fault response matches on is in the schema the response refers to
    /// (decision 49): an optional string whose description says what it holds, as this host writes it.
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, false)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, false)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, true)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, true)]
    public async Task The_problem_schema_describes_fault_and_rule_as_optional_strings(OpenApiSpecVersion version, bool qualified)
    {
        await using var app = await StartAsync(version: version, services: services =>
        {
            MapOrders(services);
            if (qualified) services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);
        });
        var document = await DocumentAsync(app);

        ProblemSchema(Responses(document, "/orders/{id}", "get")["404"]!)["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProblemDetails");
        var problem = document["components"]!["schemas"]!["ProblemDetails"]!;
        var fault = problem["properties"]!["fault"].ShouldNotBeNull($"ProblemDetails has no fault: {problem.ToJsonString()}");

        fault.AsObject().Select(keyword => keyword.Key).Order().ShouldBe(["description", "type"]);
        fault["type"]!.ToJsonString().ShouldBe("\"string\"", "never null where it is present, in 3.1 too");
        fault["description"]!.GetValue<string>().ShouldStartWith(qualified
            ? "The fault this problem reports: the name of its member, qualified with its type's"
            : "The fault this problem reports: the name of its member. ");
        (problem["required"]?.AsArray().Select(name => name!.GetValue<string>()) ?? []).ShouldNotContain("fault");
        problem["properties"]!["status"].ShouldNotBeNull("ASP.NET's members stay");

        var rule = problem["properties"]!["rule"].ShouldNotBeNull($"ProblemDetails has no rule: {problem.ToJsonString()}");
        rule.AsObject().Select(keyword => keyword.Key).Order().ShouldBe(["description", "type"]);
        rule["type"]!.ToJsonString().ShouldBe("\"string\"");
        rule["description"]!.GetValue<string>().ShouldBe(FaultResponsesOperationTransformer.RuleDescription, "always qualified, whatever QualifiedFaultNames says");
        (problem["required"]?.AsArray().Select(name => name!.GetValue<string>()) ?? []).ShouldNotContain("rule");
    }

    /// <summary>
    /// A problem response the endpoint declares itself is kept, but it answers the fault too: with every mapped
    /// status declared by <c>ProducesProblem</c>, nothing described <c>fault</c> or <c>rule</c>, though the problems
    /// carried them, and no description named the problem types (reported 2026-10-14). Inline, the declared
    /// response's own schema gets them.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Problem_responses_the_endpoint_declares_itself_describe_fault_and_rule(bool inline)
    {
        await using var app = await StartAsync(
            orders: false,
            controls: false,
            openApi: options =>
            {
                if (inline) options.CreateSchemaReferenceId = type => type.Type == typeof(ProblemDetails) ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            },
            services: MapOrderProblemTypes,
            endpoints: app => app.MapGet("/declared/{id:int}", (int id) => Find(id).ToHttpResult())
                                 .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict));

        (await ProblemAsync(app, "/declared/9"))["fault"]!.GetValue<string>().ShouldBe(nameof(OrderFault.NotFound), "the control: the problem carries it");

        var document = await DocumentAsync(app);
        var notFound = Responses(document, "/declared/{id}", "get")["404"]!;
        var problem  = inline ? ProblemSchema(notFound) : document["components"]!["schemas"]!["ProblemDetails"]!;

        problem["properties"]!["fault"].ShouldNotBeNull(problem.ToJsonString());
        problem["properties"]!["rule"].ShouldNotBeNull(problem.ToJsonString());
        notFound["description"]!.GetValue<string>().ShouldBe("Not Found. Problem types: https://problems.example/NotFound");
    }

    /// <summary>
    /// Every way an endpoint declares a problem response itself describes <c>fault</c> and <c>rule</c> and names the
    /// problem types, and keeps the media type it declares: <c>Produces&lt;ProblemDetails&gt;(404)</c> and
    /// <c>[ProducesResponseType&lt;ProblemDetails&gt;(404)]</c> declare <c>application/json</c>, where the transformer
    /// looked for <c>application/problem+json</c> alone, so their 404 named no problem type and its schema had
    /// neither (reported 2026-10-16). A type derived from <see cref="ProblemDetails"/> is a component of its own,
    /// which gets them too. Decision 95's input class: declaration form × host × media type declared × problem
    /// type × document version × a host that inlines the problem schema.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclaredProblems))]
    public async Task A_problem_response_the_endpoint_declares_itself_describes_fault_and_rule_under_the_media_type_it_declares(
        string host, string form, string media, string type, OpenApiSpecVersion version, bool inline)
    {
        var withTypeKit = await DeclaredAsync(typeKit: true);
        var aspNet = await DeclaredAsync(typeKit: false);

        var notFound = Responses(withTypeKit, "/declared/{id}", "get")["404"].ShouldNotBeNull();
        notFound["description"]!.GetValue<string>().ShouldBe("Not Found. Problem types: https://problems.example/NotFound");
        if (version == OpenApiSpecVersion.OpenApi2_0)
            DeclaredMediaTypes(aspNet).Except(DeclaredMediaTypes(withTypeKit)).ShouldBeEmpty("2.0's produces is the operation's, the added 403 and 409 among it");
        else
            DeclaredMediaTypes(withTypeKit).ShouldBe(DeclaredMediaTypes(aspNet), "the media type is the one the endpoint declares");

        var schemas = notFound["content"] is JsonObject content ? [.. content.Select(each => each.Value!["schema"]!)] : (List<JsonNode>)[notFound["schema"]!];
        schemas.ShouldNotBeEmpty();
        foreach (var schema in schemas)
        {
            var problem = schema["$ref"] is { } reference
                ? reference.GetValue<string>()[2..].Split('/').Aggregate(withTypeKit, (node, step) => node[step]!)
                : schema;
            problem["properties"]!["fault"].ShouldNotBeNull(problem.ToJsonString());
            problem["properties"]!["rule"].ShouldNotBeNull(problem.ToJsonString());
        }

        async Task<JsonNode> DeclaredAsync(bool typeKit)
        {
            await using var app = await StartAsync(
                services: MapOrderProblemTypes,
                openApi: options =>
                {
                    if (inline) options.CreateSchemaReferenceId = info => typeof(ProblemDetails).IsAssignableFrom(info.Type) ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(info);
                },
                version: version,
                mvc: host == Mvc,
                controllers: host == Mvc ? [DeclaringControllers[(form, media, type)]] : null,
                endpoints: host == Mvc ? null : app => MapDeclared(app, form, media, type),
                controls: false,
                orders: false,
                typeKit: typeKit);

            return await DocumentAsync(app);
        }
    }

    private const string Minimal = "minimal", Mvc = "mvc";
    private const string ProducesProblem = "ProducesProblem", Produces = "Produces<T>", ProducesOfType = "Produces(typeof)";
    private const string Attribute = "[ProducesResponseType<T>]", AttributeOfType = "[ProducesResponseType(typeof)]";
    private const string Unnamed = "(none)", Json = "application/json", ProblemJson = "application/problem+json";
    private const string Problem = nameof(ProblemDetails), ValidationProblem = nameof(HttpValidationProblemDetails);

    /// <summary>
    /// The declarations of a 404 problem response, by host: ASP.NET's builder methods on a minimal API
    /// (<c>ProducesProblem</c> is <c>ProducesValidationProblem</c> for the derived type), the attributes on a lambda
    /// or an action; each with no content type, <c>application/json</c> and <c>application/problem+json</c>.
    /// </summary>
    public static TheoryData<string, string, string, string, OpenApiSpecVersion, bool> DeclaredProblems()
    {
        var data = new TheoryData<string, string, string, string, OpenApiSpecVersion, bool>();
        foreach (var (host, forms) in new (string, string[])[] { (Minimal, [ProducesProblem, Produces, ProducesOfType, Attribute, AttributeOfType]), (Mvc, [Attribute, AttributeOfType]) })
        foreach (var form in forms)
        foreach (var media in (string[])[Unnamed, Json, ProblemJson])
        foreach (var type in (string[])[Problem, ValidationProblem])
        foreach (var version in (OpenApiSpecVersion[])[OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi2_0])
        foreach (var inline in (bool[])[false, true])
            data.Add(host, form, media, type, version, inline);

        return data;
    }

    private static void MapDeclared(WebApplication app, string form, string media, string type)
    {
        const string path = "/declared/{id:int}";
        const int status = StatusCodes.Status404NotFound;
        var contentType = media == Unnamed ? null : media;

        _ = (form, type) switch
        {
            (ProducesProblem, Problem) => app.MapGet(path, (int id) => Find(id).ToHttpResult()).ProducesProblem(status, contentType),
            (ProducesProblem, _) => app.MapGet(path, (int id) => Find(id).ToHttpResult()).ProducesValidationProblem(status, contentType),
            (Produces, Problem) => app.MapGet(path, (int id) => Find(id).ToHttpResult()).Produces<ProblemDetails>(status, contentType),
            (Produces, _) => app.MapGet(path, (int id) => Find(id).ToHttpResult()).Produces<HttpValidationProblemDetails>(status, contentType),
            (ProducesOfType, _) => app.MapGet(path, (int id) => Find(id).ToHttpResult())
                                      .Produces(status, type == Problem ? typeof(ProblemDetails) : typeof(HttpValidationProblemDetails), contentType),
            _ => app.MapGet(path, DeclaringLambdas[(form, media, type)]),
        };
    }

    private static readonly Dictionary<(string Form, string Media, string Type), Delegate> DeclaringLambdas = new()
    {
        [(Attribute, Unnamed, Problem)] = [ProducesResponseType<ProblemDetails>(404)] (int id) => Find(id).ToHttpResult(),
        [(Attribute, Json, Problem)] = [ProducesResponseType<ProblemDetails>(404, Json)] (int id) => Find(id).ToHttpResult(),
        [(Attribute, ProblemJson, Problem)] = [ProducesResponseType<ProblemDetails>(404, ProblemJson)] (int id) => Find(id).ToHttpResult(),
        [(Attribute, Unnamed, ValidationProblem)] = [ProducesResponseType<HttpValidationProblemDetails>(404)] (int id) => Find(id).ToHttpResult(),
        [(Attribute, Json, ValidationProblem)] = [ProducesResponseType<HttpValidationProblemDetails>(404, Json)] (int id) => Find(id).ToHttpResult(),
        [(Attribute, ProblemJson, ValidationProblem)] = [ProducesResponseType<HttpValidationProblemDetails>(404, ProblemJson)] (int id) => Find(id).ToHttpResult(),
        [(AttributeOfType, Unnamed, Problem)] = [ProducesResponseType(typeof(ProblemDetails), 404)] (int id) => Find(id).ToHttpResult(),
        [(AttributeOfType, Json, Problem)] = [ProducesResponseType(typeof(ProblemDetails), 404, Json)] (int id) => Find(id).ToHttpResult(),
        [(AttributeOfType, ProblemJson, Problem)] = [ProducesResponseType(typeof(ProblemDetails), 404, ProblemJson)] (int id) => Find(id).ToHttpResult(),
        [(AttributeOfType, Unnamed, ValidationProblem)] = [ProducesResponseType(typeof(HttpValidationProblemDetails), 404)] (int id) => Find(id).ToHttpResult(),
        [(AttributeOfType, Json, ValidationProblem)] = [ProducesResponseType(typeof(HttpValidationProblemDetails), 404, Json)] (int id) => Find(id).ToHttpResult(),
        [(AttributeOfType, ProblemJson, ValidationProblem)] = [ProducesResponseType(typeof(HttpValidationProblemDetails), 404, ProblemJson)] (int id) => Find(id).ToHttpResult(),
    };

    private static readonly Dictionary<(string Form, string Media, string Type), Type> DeclaringControllers = new()
    {
        [(Attribute, Unnamed, Problem)] = typeof(DeclaresProblem),
        [(Attribute, Json, Problem)] = typeof(DeclaresJsonProblem),
        [(Attribute, ProblemJson, Problem)] = typeof(DeclaresProblemJsonProblem),
        [(Attribute, Unnamed, ValidationProblem)] = typeof(DeclaresValidationProblem),
        [(Attribute, Json, ValidationProblem)] = typeof(DeclaresJsonValidationProblem),
        [(Attribute, ProblemJson, ValidationProblem)] = typeof(DeclaresProblemJsonValidationProblem),
        [(AttributeOfType, Unnamed, Problem)] = typeof(DeclaresProblemOfType),
        [(AttributeOfType, Json, Problem)] = typeof(DeclaresJsonProblemOfType),
        [(AttributeOfType, ProblemJson, Problem)] = typeof(DeclaresProblemJsonProblemOfType),
        [(AttributeOfType, Unnamed, ValidationProblem)] = typeof(DeclaresValidationProblemOfType),
        [(AttributeOfType, Json, ValidationProblem)] = typeof(DeclaresJsonValidationProblemOfType),
        [(AttributeOfType, ProblemJson, ValidationProblem)] = typeof(DeclaresProblemJsonValidationProblemOfType),
    };

    /// <summary>The media types the declared 404 is written with: its content's, or a 2.0 operation's <c>produces</c>, which are every response's.</summary>
    private static string[] DeclaredMediaTypes(JsonNode document)
    {
        var operation = document["paths"]!["/declared/{id}"]!["get"]!;

        return operation["responses"]!["404"]!["content"] is JsonObject content
            ? [.. content.Select(media => media.Key)]
            : [.. operation["produces"]!.AsArray().Select(media => media!.GetValue<string>())];
    }

    /// <summary>A host whose document has no fault response keeps ASP.NET's problem schema, and the whole document, byte for byte.</summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    public async Task A_document_without_a_fault_response_is_as_ASP_NET_writes_it(OpenApiSpecVersion version)
    {
        await using var withTypeKit = await StartAsync(version: version, orders: false);
        await using var without = await StartAsync(version: version, orders: false, typeKit: false, services: _ => { });

        var json = await withTypeKit.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        json.ShouldBe(await without.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        JsonNode.Parse(json)!["components"]!["schemas"]!["ProblemDetails"]!["properties"]!.AsObject().ContainsKey("fault").ShouldBeFalse();
    }

    [Fact]
    public async Task A_response_the_operation_already_has_is_kept()
    {
        await using var app = await StartAsync(endpoints: app =>
            app.MapGet("/described/{id:int}", (int id) => Find(id).ToHttpResult()).Produces<string>(StatusCodes.Status404NotFound, "text/plain"));
        var document = await DocumentAsync(app);

        var notFound = Responses(document, "/described/{id}", "get")["404"]!["content"]!.AsObject();

        notFound.Select(content => content.Key).ShouldBe(["text/plain"]);
    }

    [Fact]
    public async Task Adding_TypeKit_twice_publishes_the_same_document()
    {
        await using var once = await StartAsync();
        await using var twice = await StartAsync(openApi: options => options.AddTypeKit());

        JsonNode.DeepEquals(await DocumentAsync(twice), await DocumentAsync(once)).ShouldBeTrue();
    }

    [Fact]
    public void Each_refusal_links_to_a_subsection_of_the_README()
    {
        var invalid = Should.Throw<ArgumentException>(() => new ServiceCollection().AddFaultResponses<OrderFault>(_ => 200));
        var refusals = new[]
        {
            FaultResponses<OrderFault>.Unmapped().Message,
            // ArgumentException puts the parameter after the message; the link ends the message itself.
            invalid.Message.Replace($" (Parameter '{invalid.ParamName}')", "", StringComparison.Ordinal),
        }.Concat(typeof(FaultRefusals).GetFields(BindingFlags.Public | BindingFlags.Static).Select(link => (string)link.GetRawConstantValue()!));

        var anchors = File.ReadLines(ReadmePath()).Select(line => Heading().Match(line)).Where(match => match.Success)
                          .Select(match => Slug(match.Groups["text"].Value)).ToHashSet();

        foreach (var refusal in refusals)
        {
            var link = LinkAtTheEnd().Match(refusal);
            link.Success.ShouldBeTrue($"The refusal does not end with a link to the package README: {refusal}");
            anchors.ShouldContain(link.Groups["anchor"].Value, $"No heading of the README is #{link.Groups["anchor"].Value}.");
        }
    }

    private static async Task<WebApplication> StartAsync(
        Action<IServiceCollection>? services = null,
        Action<OpenApiOptions>? openApi = null,
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1,
        bool mvc = false,
        Type[]? controllers = null,
        Action<WebApplication>? endpoints = null,
        bool map = true,
        bool controls = true,
        bool orders = true,
        bool typeKit = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = version;
            if (typeKit) options.AddTypeKit();
            openApi?.Invoke(options);
        });
        if (services is not null) services(builder.Services);
        else if (map) MapOrders(builder.Services);
        if (mvc)
        {
            // This controller only: ProbeHost adds the whole assembly, and a nested type is no controller there.
            builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.FeatureProviders.Add(new OnlyControllers(controllers ?? [typeof(OrdersController)]));
            });
        }

        var app = builder.Build();
        app.MapOpenApi();
        if (orders)
        {
            app.MapGet("/orders/{id:int}", (int id) => Find(id).ToHttpResult());
            app.MapGet("/orders-async/{id:int}", async (int id) => (await Task.FromResult(Find(id))).ToHttpResult());
            app.MapDelete("/orders/{id:int}", (int id) => Cancel(id).ToHttpResult());
        }

        if (controls) MapControls(app);
        if (mvc) app.MapControllers();
        endpoints?.Invoke(app);

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

    /// <summary>The same responses, declared with ASP.NET's own <c>ProducesProblem</c>.</summary>
    private static void MapControls(WebApplication app)
    {
        app.MapGet("/control/orders/{id:int}", (int id) => (Results<Ok<Order>, ProblemHttpResult>)TypedResults.Ok(new Order(id, "Book")))
           .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        app.MapDelete("/control/orders/{id:int}", (int id) => (Results<NoContent, ProblemHttpResult>)TypedResults.NoContent())
           .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<JsonNode> ProblemAsync(WebApplication app, string path)
    {
        using var response = await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    private static JsonObject Responses(JsonNode document, string path, string method) =>
        document["paths"]![path]![method]!["responses"]!.AsObject();

    private static JsonNode ProblemSchema(JsonNode response) => response["content"]!["application/problem+json"]!["schema"]!;

    private static string ReadmePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx")))
                return Path.Combine(directory.FullName, "src", "CodoMetis.TypeKit.AspNetCore", "README.md");
        }

        throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
    }

    private static string Slug(string heading) =>
        NotInASlug().Replace(WebUtility.HtmlDecode(heading).ToLowerInvariant(), "").Replace(' ', '-');

    [GeneratedRegex(@" See https://github\.com/CaffeinatedCoder/CodoMetis\.TypeKit/blob/main/src/CodoMetis\.TypeKit\.AspNetCore/README\.md#(?<anchor>[^\s#]+)$")]
    private static partial Regex LinkAtTheEnd();

    [GeneratedRegex(@"^#{1,6} (?<text>.+)$")]
    private static partial Regex Heading();

    /// <summary>What GitHub drops from a heading: everything but letters, marks, digits, the underscore, spaces and hyphens.</summary>
    [GeneratedRegex(@"[^\p{L}\p{M}\p{Nd}\p{Pc} -]")]
    private static partial Regex NotInASlug();

    private sealed class OnlyControllers(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    [ApiController]
    public sealed class OrdersController : ControllerBase
    {
        [HttpGet("/mvc/orders/{id:int}")]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresProblem : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType<ProblemDetails>(404)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresJsonProblem : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType<ProblemDetails>(404, Json)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresProblemJsonProblem : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType<ProblemDetails>(404, ProblemJson)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresValidationProblem : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType<HttpValidationProblemDetails>(404)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresJsonValidationProblem : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType<HttpValidationProblemDetails>(404, Json)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresProblemJsonValidationProblem : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType<HttpValidationProblemDetails>(404, ProblemJson)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresProblemOfType : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType(typeof(ProblemDetails), 404)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresJsonProblemOfType : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType(typeof(ProblemDetails), 404, Json)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresProblemJsonProblemOfType : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType(typeof(ProblemDetails), 404, ProblemJson)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresValidationProblemOfType : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType(typeof(HttpValidationProblemDetails), 404)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresJsonValidationProblemOfType : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType(typeof(HttpValidationProblemDetails), 404, Json)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }

    [ApiController]
    public sealed class DeclaresProblemJsonValidationProblemOfType : ControllerBase
    {
        [HttpGet("/declared/{id:int}"), ProducesResponseType(typeof(HttpValidationProblemDetails), 404, ProblemJson)]
        public Results<Ok<Order>, FaultProblem<OrderFault>> Get(int id) => Find(id).ToHttpResult();
    }
}
