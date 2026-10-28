using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A mapping that withholds a member's name, <c>FaultResponse.Status(401).WithoutFaultName()</c>: the problem
/// has no <c>fault</c>, no <c>rule</c> and no member description, so two causes on one status answer alike
/// but for the detail the application wrote, and the document says that status names no fault (decision 111).
/// </summary>
public sealed class WithheldFaultNameTests
{
    public enum SignInFault { UnknownUser, WrongPassword, Locked }

    public enum PasswordRule { Expired }

    private static Result<string, Fault<SignInFault>> SignIn(string user) => user switch
    {
        "nobody" => Result.Error(Fault.Of(SignInFault.UnknownUser)),
        "ann"    => Result.Error(Fault.Of(SignInFault.WrongPassword).WithRule(PasswordRule.Expired)),
        "bob"    => Result.Error(Fault.Of(SignInFault.WrongPassword, "Try again in a minute.")),
        _        => Result.Error(Fault.Of(SignInFault.Locked)),
    };

    private static Result<Fault<ProbeOrderFault>> Archive(int id) =>
        id == 1 ? Fault.Of(ProbeOrderFault.NotFound) : Fault.Of(ProbeOrderFault.NotFound, "Order 2 was archived.");

    private static FaultResponse Mapping(SignInFault fault) => fault switch
    {
        SignInFault.Locked => StatusCodes.Status403Forbidden,
        _ => FaultResponse.Status(StatusCodes.Status401Unauthorized).WithType(new Uri("https://problems.example/sign-in")).WithoutFaultName(),
    };

    [Fact]
    public async Task Two_withheld_members_on_one_status_answer_alike_with_no_fault_and_no_rule()
    {
        await using var app = await StartAsync();

        var unknownUser = await ProblemAsync(app, "/login/nobody");
        var wrongPassword = await ProblemAsync(app, "/login/ann");

        unknownUser.ContainsKey("fault").ShouldBeFalse(unknownUser.ToJsonString());
        wrongPassword.ContainsKey("rule").ShouldBeFalse("the rule names the cause as surely as the member does");
        JsonNode.DeepEquals(unknownUser, wrongPassword).ShouldBeTrue($"{wrongPassword.ToJsonString()} tells itself apart from {unknownUser.ToJsonString()}");
        unknownUser["status"]!.GetValue<int>().ShouldBe(401);
        unknownUser["type"]!.GetValue<string>().ShouldBe("https://problems.example/sign-in");
    }

    [Fact]
    public async Task The_occurrence_s_own_detail_is_kept_and_a_named_member_still_names_itself()
    {
        await using var app = await StartAsync();

        var withDetail = await ProblemAsync(app, "/login/bob");
        withDetail["detail"]!.GetValue<string>().ShouldBe("Try again in a minute.");
        withDetail.ContainsKey("fault").ShouldBeFalse();

        (await ProblemAsync(app, "/login/carol"))["fault"]!.GetValue<string>().ShouldBe("Locked");
    }

    [Fact]
    public async Task The_host_still_sees_the_fault_and_that_its_name_is_withheld()
    {
        var seen = new List<(string Name, bool Withheld, string? Rule)>();
        await using var app = await StartAsync(services => services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.HttpContext.Features.Get<IFaultFeature>() is { } feature) seen.Add((feature.Name, feature.NameWithheld, feature.Rule));
        }));

        await ProblemAsync(app, "/login/ann");
        await ProblemAsync(app, "/login/carol");

        seen.ShouldBe([("WrongPassword", true, "WithheldFaultNameTests.PasswordRule.Expired"), ("Locked", false, null)]);
    }

    [Fact]
    public async Task The_document_says_a_status_whose_every_fault_is_withheld_names_none()
    {
        await using var app = await StartAsync(endpoints: app =>
            app.MapPost("/admin/login/{user}", (string user) => SignIn(user).ToHttpResult()).WithFaultStatus(SignInFault.Locked, StatusCodes.Status401Unauthorized));

        var paths = (await DocumentAsync(app))["paths"]!;

        Description(paths, "/login/{user}", "401").ShouldBe("Unauthorized. Problem types: https://problems.example/sign-in. The problem names no fault.");
        Description(paths, "/login/{user}", "403").ShouldBe("Forbidden");
        Description(paths, "/admin/login/{user}", "401").ShouldBe("Unauthorized. Problem types: https://problems.example/sign-in", "Locked shares the status and names itself");
    }

    /// <summary>A fault set member's description stands in for a missing detail, and names the member: withheld, it goes too.</summary>
    [Fact]
    public async Task A_whole_fault_set_withheld_writes_no_member_description()
    {
        await using var app = await StartAsync(
            services => services.AddFaultResponses<ProbeOrderFault>(fault => FaultResponse.For(fault.Kind).WithoutFaultName()),
            app => app.MapGet("/orders/{id:int}", (int id) => Archive(id).ToHttpResult()));

        var described = await ProblemAsync(app, "/orders/1");
        described.ContainsKey("detail").ShouldBeFalse(described.ToJsonString());
        described.ContainsKey("fault").ShouldBeFalse();

        (await ProblemAsync(app, "/orders/2"))["detail"]!.GetValue<string>().ShouldBe("Order 2 was archived.");
    }

    [Fact]
    public void Withholding_the_name_survives_a_type_and_a_title_in_either_order()
    {
        FaultResponse.Status(401).NamesFault.ShouldBeTrue();
        FaultResponse.Status(401).WithoutFaultName().WithType(new Uri("https://problems.example/a")).WithTitle("Refused").NamesFault.ShouldBeFalse();

        var response = FaultResponse.Status(401).WithType(new Uri("https://problems.example/a")).WithTitle("Refused").WithoutFaultName();
        response.NamesFault.ShouldBeFalse();
        response.Type.ShouldBe(new Uri("https://problems.example/a"));
        response.Title.ShouldBe("Refused");
        response.StatusCode.ShouldBe(401);
    }

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection>? services = null, Action<WebApplication>? endpoints = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        builder.Services.AddFaultResponses<SignInFault>(Mapping);
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/login/{user}", (string user) => SignIn(user).ToHttpResult());
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

    private static async Task<JsonObject> ProblemAsync(WebApplication app, string path)
    {
        var method = path.StartsWith("/login", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get;
        using var response = await app.GetTestClient().SendAsync(new HttpRequestMessage(method, path), TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        body.Remove("traceId");
        return body;
    }

    private static async Task<JsonNode> DocumentAsync(WebApplication app) =>
        JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

    private static string Description(JsonNode paths, string path, string status) =>
        paths[path]!["post"]?["responses"]?[status]?["description"]?.GetValue<string>()
     ?? paths[path]!["get"]!["responses"]![status]!["description"]!.GetValue<string>();
}
