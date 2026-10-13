// sample: testing/usings
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.Testing;
using CodoMetis.TypeKit.ValueObjects;
// end sample
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples.Docs.Testing;

// sample: testing/product-code
public enum CodeFault { Blank, NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);
        if (value != value.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);

        return new ProductCode(value.Trim());
    }
}
// end sample

// sample: testing/code-request
public sealed record CodeRequest(ProductCode Code);
// end sample

// sample: testing/code-patch
public sealed record CodePatch(Patch<ProductCode> Code, Patch<string?> Note, Patch<int> Quantity);
// end sample

/// <summary>The page's tests, run as written.</summary>
public sealed class ProductCodeTests
{
    // sample: testing/assertions
    [Fact]
    public void A_code_is_created_or_refused()
    {
        ProductCode code = ProductCode.Create("ABC").ShouldBeSuccess();   // the value, for the test's own assertions
        code.Value.ShouldBe("ABC");

        ProductCode.Create("abc").ShouldBeError(CodeFault.NotUpperCase);   // this error, or the test fails
        CodeFault fault = ProductCode.Create(" ").ShouldBeError();          // any error, handed back
        fault.ShouldBe(CodeFault.Blank);

        ProductCode.TryFrom("ABC").ShouldBeSome().ShouldBe(code);
        ProductCode.TryFrom("abc").ShouldBeNone();
    }
    // end sample

    // sample: testing/patch
    [Fact]
    public void A_patch_says_what_the_request_mentioned()
    {
        CodePatch patch = JsonSerializer.Deserialize<CodePatch>("""{"note":null,"quantity":2}""", JsonSerializerOptions.Web)!;

        patch.Quantity.ShouldBeValue().ShouldBe(2);   // the value, for the test's own assertions
        patch.Note.ShouldBeNull();                    // a clear
        patch.Code.ShouldBeAbsent();                  // not in the document
    }
    // end sample

    // sample: testing/table
    [Theory]
    [InlineData("ABC", null)]
    [InlineData(" ABC ", null)]
    [InlineData("", CodeFault.Blank)]
    [InlineData("   ", CodeFault.Blank)]
    [InlineData("abc", CodeFault.NotUpperCase)]
    public void Create_applies_each_rule(string input, CodeFault? refused)
    {
        var created = ProductCode.Create(input);

        if (refused is { } fault) created.ShouldBeError(fault);
        else created.ShouldBeSuccess().Value.ShouldBe(input.Trim());
    }
    // end sample

    // sample: testing/ways-in
    [Fact]
    public void Every_way_in_applies_the_rules()
    {
        ProductCode.TryParse("abc", null, out _).ShouldBeFalse();
        Should.Throw<FormatException>(() => ProductCode.Parse("abc", null)).Message.ShouldContain("NotUpperCase");
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProductCode>("\"abc\"")).Message.ShouldContain("NotUpperCase");
        Should.Throw<InvalidOperationException>(() => ProductCode.FromKnownGood("abc")).Message.ShouldContain("NotUpperCase");
        ProductCode.FromKnownGood("ABC").Revalidate().ShouldBeSuccess();
    }
    // end sample

    // sample: testing/endpoint
    [Fact]
    public async Task A_refused_body_is_a_400()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapPost("/codes", (CodeRequest request) => request.Code.Value);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var client = app.GetTestClient();
        var accepted = await client.PostAsJsonAsync("/codes", new { code = "ABC" }, TestContext.Current.CancellationToken);
        var refused = await client.PostAsJsonAsync("/codes", new { code = "abc" }, TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
    // end sample
}

public static class Usage
{
    public static string Failure()
    {
        // sample: testing/failure
        string failure = Result.Catch(() => ProductCode.Create("abc").ShouldBeSuccess()).Match(_ => "", error => error.Message);   // Expected a success, but the Result<ProductCode, CodeFault> is the error NotUpperCase.
        // end sample

        return failure;
    }
}

public sealed class TestingTests
{
    private const string Readme = "docs/testing.md";

    [Fact]
    public void The_failure_says_what_the_outcome_was()
    {
        var failure = Usage.Failure();

        failure.ShouldBe(SampleOutputs.Shown(Readme, "Result.Catch(() => ProductCode.Create(\"abc\").ShouldBeSuccess()).Match(_ => \"\", error => error.Message)"));
        Should.Throw<TypeKitAssertionException>(() => ProductCode.Create("abc").ShouldBeSuccess());
    }
}
