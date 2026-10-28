using System.Net;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Web.Faults;

public enum PaymentFault { Declined, Duplicate }

// sample: CodoMetis.TypeKit.AspNetCore/fault-set
public sealed partial class AccountFault : IFaultSet<AccountFault>
{
    public static readonly AccountFault NotFound = new(FaultKind.NotFound, "The account does not exist.");
    public static readonly AccountFault EmailTaken = new(FaultKind.Conflict, "Another account uses that email address.");
    public static readonly AccountFault Locked = new(FaultKind.Forbidden, null);   // a refusal that must not explain itself

    private AccountFault(FaultKind kind, string? description) { Kind = kind; Description = description; }

    public FaultKind Kind { get; }
    public string? Description { get; }
}
// end sample

// sample: CodoMetis.TypeKit.AspNetCore/fault-rule
public enum EmailRule { InUse, Reserved }   // which check refused the address
// end sample

public sealed record Account(int Id, string Email);

public sealed record NewAccount(string Email);

/// <summary>What a store throws when a unique index rejects a row.</summary>
public sealed class UniqueViolationException(string message) : Exception(message);

public interface IAccountStore
{
    Account Save(Account account);
}

public interface IAccounts
{
    Result<Account, AccountFault> Open(NewAccount body);

    Result<Account, Fault<AccountFault>> Rename(int id, string email);
}

// sample: CodoMetis.TypeKit.AspNetCore/one-fault-for-two-causes
public enum SignInFault { UnknownUser, WrongPassword }   // the domain's: which check refused
public enum LoginFault { InvalidCredentials }            // what the client is told

// end sample
public sealed record Credentials(string User, string Password);

public sealed record Session(string Token);

public interface ISignIn
{
    Result<Session, SignInFault> SignIn(Credentials credentials);
}

/// <summary>The README's sign-in: the domain tells its two refusals apart, the response does not.</summary>
public static class SignIns
{
    public static void Responses(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/one-fault-for-two-causes
        builder.Services.AddFaultResponses<LoginFault>(_ => StatusCodes.Status401Unauthorized);

        // end sample
    }

    public static void Endpoints(WebApplication app, ISignIn accounts)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/one-fault-for-two-causes
        app.MapPost("/login", (Credentials credentials) => accounts.SignIn(credentials).MapError(_ => LoginFault.InvalidCredentials).ToHttpResult());
        // end sample
    }
}

public static class Faults
{
    public static void Responses(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-response
        builder.Services.AddFaultResponses<PaymentFault>(fault => fault switch
        {
            PaymentFault.Declined  => FaultResponse.Status(StatusCodes.Status402PaymentRequired).WithType(new Uri("https://example.com/problems/declined")),
            PaymentFault.Duplicate => StatusCodes.Status409Conflict,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        });
        // end sample

        // sample: CodoMetis.TypeKit.AspNetCore/fault-set-mapping
        builder.Services.AddFaultResponses<AccountFault>();   // every member by its kind, its description as the detail
        // end sample
    }

    public static void Endpoints(WebApplication app, IAccounts accounts)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-status
        app.MapPost("/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult())
           .WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
        // end sample

        // sample: CodoMetis.TypeKit.AspNetCore/fault-status-group
        var v3 = app.MapGroup("/v3").WithFaultStatus(AccountFault.EmailTaken, StatusCodes.Status422UnprocessableEntity);
        v3.MapPost("/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult());   // answers with AccountFault: the 422 applies
        v3.MapGet("/health", () => "ok");                                                  // answers with none: left alone
        // end sample

        // sample: CodoMetis.TypeKit.AspNetCore/fault-success
        app.MapPost("/v2/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult(account => TypedResults.Created($"/accounts/{account.Id}", account)));
        // end sample

        // sample: CodoMetis.TypeKit.AspNetCore/fault-detail
        app.MapPut("/accounts/{id:int}/email", (int id, NewAccount body) => accounts.Rename(id, body.Email).ToHttpResult());
        // end sample

        // sample: CodoMetis.TypeKit.AspNetCore/fault-left-out
        app.MapPost("/v4/accounts", (NewAccount body) => accounts.Open(body).ToHttpResult())
           .WithoutFaults(AccountFault.NotFound);   // opening an account never reports a missing one: no 404 here
        // end sample
    }

    public static Result<Account, Fault<AccountFault>> Taken()
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-detail-domain
        Result<Account, Fault<AccountFault>> taken = Result.Error(Fault.Of(AccountFault.EmailTaken, "The address has been in use since 3 May."));
        // end sample

        return taken;
    }

    public static Result<Account, Fault<AccountFault>> Reserved()
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-rule-domain
        Result<Account, Fault<AccountFault>> reserved = Result.Error(Fault.Of(AccountFault.EmailTaken).WithRule(EmailRule.Reserved));
        // end sample

        return reserved;
    }

    public static Result<Account, Fault<AccountFault>> Saved(IAccountStore store, Account account)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-cause-domain
        Result<Account, Fault<AccountFault>> saved = Result.Catch<Account, UniqueViolationException>(() => store.Save(account))
            .MapError(e => Fault.Of(AccountFault.EmailTaken, "Another account took the address a moment ago.", e));
        // end sample

        return saved;
    }

    public static void Logged(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-cause-log
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.HttpContext.Features.Get<IFaultFeature>() is { Cause: { } cause } fault)
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Faults")
                    .LogWarning(cause, "Answered {Fault} for an exception", fault.Name);   // your log gets the exception, the client doesn't
        });
        // end sample
    }

    public static void Shaped(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-feature
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.HttpContext.Features.Get<IFaultFeature>() is { } fault)
                context.ProblemDetails.Extensions["code"] = $"ACC-{fault.Name}";   // the host's own extension, for this fault only
        });
        builder.Services.Configure<FaultResponseOptions>(options => options.QualifiedFaultNames = true);   // "fault": "AccountFault.EmailTaken"
        // end sample
    }

    public static void EveryFaultSet(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-sets-default
        builder.Services.AddFaultResponses();   // every fault set an endpoint answers with, by its kind
        // end sample
    }

    public static void EveryFaultSetTyped(WebApplicationBuilder builder)
    {
        // sample: CodoMetis.TypeKit.AspNetCore/fault-sets-default-mapping
        builder.Services.AddFaultResponses(fault => FaultResponse.For(fault.Kind).WithType(new Uri($"https://example.com/problems/{fault.Kind}")));
        // end sample
    }
}

// Internal, so no host of this assembly discovers it: the sample is the action's declaration.
[ApiController]
internal sealed class AccountsController(IAccounts accounts) : ControllerBase
{
    // sample: CodoMetis.TypeKit.AspNetCore/fault-status-mvc
    [HttpPost("/mvc/accounts")]
    [FaultStatus<AccountFault>(nameof(AccountFault.EmailTaken), StatusCodes.Status422UnprocessableEntity)]
    public Results<Ok<Account>, FaultProblem<AccountFault>> Open(NewAccount body) => accounts.Open(body).ToHttpResult();
    // end sample
}

/// <summary>A domain that answers every call with one outcome, for the tests below.</summary>
public sealed class FixedAccounts(Result<Account, AccountFault> open) : IAccounts
{
    public Result<Account, AccountFault> Open(NewAccount body) => open;

    public Result<Account, Fault<AccountFault>> Rename(int id, string email) => Faults.Taken();
}

public sealed class FaultResponseSampleTests
{
    private static async Task<WebApplication> StartAsync(Result<Account, AccountFault> open, bool shaped = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        Faults.Responses(builder);
        if (shaped) Faults.Shaped(builder);

        var app = builder.Build();
        app.MapOpenApi();
        Faults.Endpoints(app, new FixedAccounts(open));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    /// <summary>A host with the endpoints above and only the services <paramref name="services"/> adds.</summary>
    private static async Task<WebApplication> StartWithAsync(Action<WebApplicationBuilder> services)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        services(builder);

        var app = builder.Build();
        app.MapOpenApi();
        Faults.Endpoints(app, new FixedAccounts(Result.Error(AccountFault.EmailTaken)));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class TakenStore(Exception thrown) : IAccountStore
    {
        public Account Save(Account account) => throw thrown;
    }

    /// <summary>The README's cause: the store's exception is kept on the fault, and the fault is still the one the client sees.</summary>
    [Fact]
    public void A_caught_exception_is_kept_as_the_cause()
    {
        var thrown = new UniqueViolationException("duplicate key value violates unique constraint \"ix_accounts_email\"");

        var saved = Faults.Saved(new TakenStore(thrown), new Account(7, "ada@example.com"));

        saved.TryGetValue(out _, out var fault).ShouldBeFalse();
        fault.Reason.ShouldBe(AccountFault.EmailTaken);
        fault.Cause.ShouldBeSameAs(thrown);
        fault.ShouldBe(Fault.Of(AccountFault.EmailTaken, "Another account took the address a moment ago."));
    }

    /// <summary>The README's one call for every fault set: AccountFault answered by its kind with no mapping of its own.</summary>
    [Fact]
    public async Task Every_fault_set_is_answered_by_its_kind_with_one_call()
    {
        await using var app = await StartWithAsync(Faults.EveryFaultSet);

        using var response = await app.GetTestClient().PostAsync("/v2/accounts", Body(), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["detail"]!.GetValue<string>()
                .ShouldBe("Another account uses that email address.");
    }

    [Fact]
    public async Task The_host_s_own_mapping_for_every_fault_set_gives_each_kind_its_type()
    {
        await using var app = await StartWithAsync(Faults.EveryFaultSetTyped);

        using var response = await app.GetTestClient().PostAsync("/v2/accounts", Body(), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["type"]!.GetValue<string>()
                .ShouldBe("https://example.com/problems/Conflict");
    }

    private static StringContent Body() => new("""{"email":"ann@example.com"}""", System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_fault_set_is_answered_by_kind_and_the_endpoint_s_status_where_it_sets_one()
    {
        await using var app = await StartAsync(Result.Error(AccountFault.EmailTaken));

        using var strict = await app.GetTestClient().PostAsync("/accounts", Body(), TestContext.Current.CancellationToken);
        strict.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "WithFaultStatus");

        using var byKind = await app.GetTestClient().PostAsync("/v2/accounts", Body(), TestContext.Current.CancellationToken);
        byKind.StatusCode.ShouldBe(HttpStatusCode.Conflict, "the member's kind");
        var problem = JsonNode.Parse(await byKind.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        problem["detail"]!.GetValue<string>().ShouldBe("Another account uses that email address.");
    }

    /// <summary>The README's group: its status applies to the endpoint that answers with the fault type, and leaves the health check alone.</summary>
    [Fact]
    public async Task A_group_s_status_applies_to_its_fault_endpoint_and_leaves_its_health_check_alone()
    {
        await using var app = await StartAsync(Result.Error(AccountFault.EmailTaken));
        var client = app.GetTestClient();

        using var refused = await client.PostAsync("/v3/accounts", Body(), TestContext.Current.CancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.GetStringAsync("/v3/health", TestContext.Current.CancellationToken)).ShouldBe("ok");

        var paths = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!["paths"]!;
        paths["/v3/accounts"]!["post"]!["responses"]!.AsObject().Select(response => response.Key).Order().ShouldBe(["200", "403", "404", "422"]);
        paths["/v3/health"]!["get"]!["responses"]!.AsObject().Select(response => response.Key).ShouldBe(["200"]);
    }

    /// <summary>The README's route that leaves a member out: no 404 in its operation, the other statuses kept.</summary>
    [Fact]
    public async Task A_route_leaves_out_of_its_document_what_it_cannot_answer()
    {
        await using var app = await StartAsync(Result.Error(AccountFault.EmailTaken));

        using var refused = await app.GetTestClient().PostAsync("/v4/accounts", Body(), TestContext.Current.CancellationToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var paths = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!["paths"]!;
        paths["/v4/accounts"]!["post"]!["responses"]!.AsObject().Select(response => response.Key).ShouldBe(["200", "403", "409"]);
    }

    [Fact]
    public async Task The_endpoint_s_own_success_is_a_201_with_its_location()
    {
        await using var app = await StartAsync(new Account(7, "ann@example.com"));

        using var created = await app.GetTestClient().PostAsync("/v2/accounts", Body(), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location!.ToString().ShouldBe("/accounts/7");
    }

    [Fact]
    public async Task An_occurrence_s_detail_replaces_the_member_s_description()
    {
        await using var app = await StartAsync(new Account(7, "ann@example.com"));

        using var response = await app.GetTestClient().PutAsync("/accounts/7/email", Body(), TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem["detail"]!.GetValue<string>().ShouldBe("The address has been in use since 3 May.");
    }

    /// <summary>What the README says the problem reads: the fault's name and the rule's, qualified, at the fault's status.</summary>
    [Fact]
    public async Task An_occurrence_s_rule_reaches_the_problem_beside_the_fault()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        Faults.Responses(builder);
        await using var app = builder.Build();
        app.MapPut("/reserved", () => Faults.Reserved().ToHttpResult());
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient().PutAsync("/reserved", content: null, TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, "the fault's status");
        $"\"fault\": \"{problem["fault"]!.GetValue<string>()}\", \"rule\": \"{problem["rule"]!.GetValue<string>()}\""
            .ShouldBe("\"fault\": \"EmailTaken\", \"rule\": \"EmailRule.Reserved\"");
        problem["detail"]!.GetValue<string>().ShouldBe("Another account uses that email address.", "the member's description, as without a rule");
    }

    [Fact]
    public async Task The_host_shapes_the_problem_and_qualifies_the_fault_name()
    {
        await using var app = await StartAsync(Result.Error(AccountFault.EmailTaken), shaped: true);

        using var response = await app.GetTestClient().PostAsync("/v2/accounts", Body(), TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        problem["fault"]!.GetValue<string>().ShouldBe("AccountFault.EmailTaken");
        problem["code"]!.GetValue<string>().ShouldBe("ACC-AccountFault.EmailTaken");
    }

    [Fact]
    public async Task A_type_URI_reaches_the_problem_and_the_document()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(options => options.AddTypeKit());
        Faults.Responses(builder);
        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/payments", () => Result<int, PaymentFault>.Error(PaymentFault.Declined).ToHttpResult());
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient().PostAsync("/payments", content: null, TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        problem["type"]!.GetValue<string>().ShouldBe("https://example.com/problems/declined");

        var document = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        document["paths"]!["/payments"]!["post"]!["responses"]!["402"]!["description"]!.GetValue<string>()
            .ShouldBe("Payment Required. Problem types: https://example.com/problems/declined");
    }
}

/// <summary>
/// What the README's "Faults a client must not tell apart" says: one status for two causes still names
/// which it was, and mapped to one member at the boundary, the two answer alike, the trace id aside.
/// </summary>
public sealed class SignInSampleTests
{
    private sealed class Refusing : ISignIn
    {
        public Result<Session, SignInFault> SignIn(Credentials credentials) =>
            Result.Error(credentials.User == "ann" ? SignInFault.WrongPassword : SignInFault.UnknownUser);
    }

    [Fact]
    public async Task Two_causes_mapped_to_one_fault_answer_alike_where_one_status_alone_tells_them_apart()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        SignIns.Responses(builder);
        builder.Services.AddFaultResponses<SignInFault>(_ => StatusCodes.Status401Unauthorized);

        await using var app = builder.Build();
        var accounts = new Refusing();
        SignIns.Endpoints(app, accounts);
        app.MapPost("/login-telling", (Credentials credentials) => accounts.SignIn(credentials).ToHttpResult());   // one status, the domain's faults
        await app.StartAsync(TestContext.Current.CancellationToken);

        var unknownUser = await AnswerAsync(app, "/login", "nobody");
        var wrongPassword = await AnswerAsync(app, "/login", "ann");
        unknownUser.Status.ShouldBe(HttpStatusCode.Unauthorized);
        unknownUser.Body["fault"]!.GetValue<string>().ShouldBe("InvalidCredentials");
        wrongPassword.Status.ShouldBe(unknownUser.Status);
        JsonNode.DeepEquals(wrongPassword.Body, unknownUser.Body)
            .ShouldBeTrue($"{wrongPassword.Body.ToJsonString()} tells itself apart from {unknownUser.Body.ToJsonString()}");

        (await AnswerAsync(app, "/login-telling", "nobody")).Body["fault"]!.GetValue<string>().ShouldBe("UnknownUser");
        (await AnswerAsync(app, "/login-telling", "ann")).Body["fault"]!.GetValue<string>().ShouldBe("WrongPassword");
    }

    private static async Task<(HttpStatusCode Status, JsonObject Body)> AnswerAsync(WebApplication app, string path, string user)
    {
        using var content = new StringContent($$"""{"user":"{{user}}","password":"secret"}""", System.Text.Encoding.UTF8, "application/json");
        using var response = await app.GetTestClient().PostAsync(path, content, TestContext.Current.CancellationToken);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        body.Remove("traceId");

        return (response.StatusCode, body);
    }
}
