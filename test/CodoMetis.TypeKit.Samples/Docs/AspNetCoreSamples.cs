// sample: aspnet-core/usings
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.AspNetCore.Http.HttpResults;
// end sample
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Docs.Web;

// sample: aspnet-core/types
public readonly partial record struct CustomerId : IValue<Guid>;

public enum EmailFault { NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value) =>
        value.Contains('@') ? new Email(value.Trim()) : Result.Error(EmailFault.NoAt);
}

public enum PageFault { NotPositive, TooLarge }

public readonly partial record struct PageSize : IValidatedValue<PageSize, int, PageFault>
{
    public static Result<PageSize, PageFault> Create(int value) =>
        value <= 0 ? Result.Error(PageFault.NotPositive) : value > 100 ? Result.Error(PageFault.TooLarge) : new PageSize(value);
}

public enum CustomerFault { NotFound, EmailTaken }

public sealed record RegisterCustomer(Email Email, string Name);

public sealed record CustomerDto(CustomerId Id, Email Email, string Name);
// end sample

// sample: aspnet-core/service
public interface ICustomers
{
    Result<CustomerDto, CustomerFault> Find(CustomerId id);

    Result<CustomerDto, CustomerFault> Register(RegisterCustomer request);

    IReadOnlyList<CustomerDto> List(Email? email, PageSize? page);
}
// end sample

// sample: aspnet-core/mvc
[ApiController]
[Route("mvc/customers")]
public sealed class CustomersController(ICustomers customers) : ControllerBase
{
    [HttpGet("{id}")]
    public Results<Ok<CustomerDto>, FaultProblem<CustomerFault>> Get([FromRoute] CustomerId id) => customers.Find(id).ToHttpResult();   // the generated TypeConverter binds it

    [HttpPost]
    public Results<Ok<CustomerDto>, FaultProblem<CustomerFault>> Post(RegisterCustomer body) => customers.Register(body).ToHttpResult();
}
// end sample

public static class Host
{
    public static void Configure(WebApplicationBuilder builder)
    {
        // sample: aspnet-core/setup
        builder.Services.AddOpenApi(options => options.AddTypeKit());          // the document: every value object as the schema of what it wraps

        builder.Services.AddFaultResponses<CustomerFault>(fault => fault switch   // a Result as a response: the status per fault
        {
            CustomerFault.NotFound => StatusCodes.Status404NotFound,
            CustomerFault.EmailTaken => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });
        // end sample
    }

    public static void Map(WebApplication app, ICustomers customers)
    {
#pragma warning disable ASP0020 // the value objects are declared in this project, as the page says
        // sample: aspnet-core/minimal-api
        app.MapGet("/customers/{id}", (CustomerId id) => customers.Find(id).ToHttpResult());                  // a route parameter, through the generated TryParse
        app.MapGet("/customers", (Email? email, PageSize? page) => customers.List(email, page));              // query parameters, optional
        app.MapPost("/customers", (RegisterCustomer body) => customers.Register(body).ToHttpResult());        // a body, through the generated JSON converter
        // end sample
#pragma warning restore ASP0020
    }

    public static bool Outcome(ICustomers customers)
    {
        // sample: aspnet-core/handler-outcome
        var response = customers.Find(CustomerId.New()).ToHttpResult();

        bool notFound = response.Result is FaultProblem<CustomerFault> { Fault: CustomerFault.NotFound };   // true
        // end sample

        return notFound;
    }

    public static void Customize(WebApplicationBuilder builder)
    {
        // sample: aspnet-core/customize
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["service"] = "customers");
        // end sample
    }
}

/// <summary>One registered customer; a second registration of the same address is a conflict.</summary>
public sealed class InMemoryCustomers : ICustomers
{
    public CustomerDto Ann { get; } = new(CustomerId.New(), Email.FromKnownGood("ann@example.com"), "Ann");

    public Result<CustomerDto, CustomerFault> Find(CustomerId id) => id == Ann.Id ? Ann : Result.Error(CustomerFault.NotFound);

    public Result<CustomerDto, CustomerFault> Register(RegisterCustomer request) =>
        request.Email == Ann.Email ? Result.Error(CustomerFault.EmailTaken) : new CustomerDto(CustomerId.New(), request.Email, request.Name);

    public IReadOnlyList<CustomerDto> List(Email? email, PageSize? page) => email is null || email == Ann.Email ? [Ann] : [];
}

public sealed partial class AspNetCoreTests
{
    private const string Readme = "docs/aspnet-core.md";

    private static async Task<(WebApplication App, InMemoryCustomers Customers)> StartAsync(bool customize = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var customers = new InMemoryCustomers();
        builder.Services.AddSingleton<ICustomers>(customers);
        builder.Services.AddControllers().AddApplicationPart(typeof(CustomersController).Assembly);
        Host.Configure(builder);
        if (customize) Host.Customize(builder);

        var app = builder.Build();
        app.MapOpenApi();
        Host.Map(app, customers);
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return (app, customers);
    }

    /// <summary>The table under "What a refused value answers", row by row, against the running host.</summary>
    [Fact]
    public async Task A_refused_value_answers_as_the_table_says()
    {
        var (app, customers) = await StartAsync();
        await using var _ = app;
        var client = app.GetTestClient();
        var token = TestContext.Current.CancellationToken;

        (await client.GetAsync($"/customers/{customers.Ann.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/customers/not-an-id", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/customers?page=500", token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/customers?page=50&email=ann@example.com", token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refused = await client.PostAsync("/customers", Body("""{"email":"nobody","name":"Bo"}"""), token);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(token)).ShouldBeEmpty("a minimal API logs the JsonException and answers an empty 400");

        using var missing = await client.PostAsync("/customers", Body("""{"name":"Bo"}"""), token);
        missing.StatusCode.ShouldBe(HttpStatusCode.OK, "without RespectRequiredConstructorParameters the handler sees a default Email");

        using var mvc = await client.PostAsync("/mvc/customers", Body("""{"email":"nobody","name":"Bo"}"""), token);
        mvc.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await mvc.Content.ReadAsStringAsync(token);
        problem.ShouldContain("NoAt");
        problem.ShouldContain("Email");
        problem.ShouldNotContain("nobody");
        (await client.GetAsync($"/mvc/customers/{customers.Ann.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The problem the page shows, read from its first JSON block, against what the host answers for an unknown id.</summary>
    [Fact]
    public async Task A_fault_is_answered_with_the_problem_the_page_shows()
    {
        var (app, _) = await StartAsync();
        await using var _ = app;
        var token = TestContext.Current.CancellationToken;

        using var response = await app.GetTestClient().GetAsync($"/customers/{Guid.Empty}", token);
        var answered = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
        var shown = JsonBlocks()[0].AsObject();

        ((int)response.StatusCode).ShouldBe(shown["status"]!.GetValue<int>());
        answered.Select(member => member.Key).Order().ShouldBe(shown.Select(member => member.Key).Order());
        foreach (var (name, value) in shown.Where(member => member.Key != "traceId"))
            JsonNode.DeepEquals(answered[name], value).ShouldBeTrue($"{name}: {answered[name]?.ToJsonString()} where the page shows {value?.ToJsonString()}");

        using var conflict = await app.GetTestClient().PostAsync("/customers", Body("""{"email":"ann@example.com","name":"Ann"}"""), token);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_handler_s_outcome_is_read_without_a_host() =>
        Host.Outcome(new InMemoryCustomers()).ShouldBeShownIn(Readme, found => found ? "true" : "false", "response.Result is FaultProblem<CustomerFault> { Fault: CustomerFault.NotFound }");

    [Fact]
    public async Task The_host_s_problem_details_customisation_reaches_a_fault()
    {
        var (app, _) = await StartAsync(customize: true);
        await using var _ = app;
        var token = TestContext.Current.CancellationToken;

        using var response = await app.GetTestClient().GetAsync($"/customers/{Guid.Empty}", token);
        var answered = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();

        answered["service"]!.GetValue<string>().ShouldBe("customers");
        answered["fault"]!.GetValue<string>().ShouldBe("NotFound");
    }

    /// <summary>The operation the page shows, read from its second JSON block: every keyword it shows, the document has.</summary>
    [Fact]
    public async Task The_document_says_what_the_page_shows()
    {
        var (app, _) = await StartAsync();
        await using var _ = app;

        var json = await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var document = JsonNode.Parse(json)!;
        var shown = JsonBlocks()[1].AsObject();

        foreach (var (path, item) in shown) document["paths"]![path].ShouldContainSchema(item!, path);
        document.Component("CustomerId").ShouldContainSchema(JsonNode.Parse("""{"type":"string","format":"uuid"}""")!, "CustomerId");
        document.Component("ProblemDetails")["properties"].ShouldContainSchema(JsonNode.Parse("""{"fault":{"type":"string"}}""")!, "ProblemDetails, which the page says describes fault");
        var page = document["paths"]!["/customers"]!["get"]!["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "page");
        page!["schema"]!["type"]!.ToJsonString().ShouldContain("integer", Case.Sensitive, "a query parameter is described as a parameter of the wrapped type, inline");
    }

    private static List<JsonNode> JsonBlocks() =>
        [.. JsonBlock().Matches(SampleOutputs.Readme(Readme)).Select(block => JsonNode.Parse(block.Groups["body"].Value.TrimStart().StartsWith('{') ? block.Groups["body"].Value : $"{{{block.Groups["body"].Value}}}")!)];

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    [GeneratedRegex(@"^```json\n(?<body>.*?)\n```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex JsonBlock();
}
