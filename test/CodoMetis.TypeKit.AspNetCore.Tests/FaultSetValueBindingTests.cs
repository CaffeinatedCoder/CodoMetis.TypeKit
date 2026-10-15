using System.Reflection;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A validated value whose fault type is a fault set, <see cref="ProbeArticle"/>, bound from a route, a
/// query and a JSON body: refused with a 400 and accepted as <c>Create</c> made it, as a value over an
/// enum fault is.
/// </summary>
public sealed class FaultSetValueBindingTests
{
    private const string Refused = "lower-secret";

    /// <summary>
    /// What a 400 says is ASP.NET's, as for a value over an enum fault (the AspNetCore README's Binding
    /// section): a minimal API writes no detail, MVC's query binding quotes the value in its own sentence,
    /// and MVC's body reading passes the converter's message on, which names the member. A fault set's
    /// description, a sentence for a client, is in none of them.
    /// </summary>
    [Theory]
    [InlineData("GET", "/minimal/route/" + Refused, null, false, false)]
    [InlineData("GET", "/minimal/query?sku=" + Refused, null, false, false)]
    [InlineData("POST", "/minimal/body", $$"""{"sku":"{{Refused}}"}""", false, false)]
    [InlineData("GET", "/mvc/query?sku=" + Refused, null, false, true)]
    [InlineData("POST", "/mvc/body", $$"""{"sku":"{{Refused}}"}""", true, false)]
    public async Task A_value_its_fault_set_refuses_is_a_400_that_says_what_ASP_NET_says_for_an_enum_fault(
        string method, string path, string? json, bool namesTheMember, bool quotesTheInput)
    {
        var (status, text) = await SendAsync(method, path, json);

        status.ShouldBe(HttpStatusCode.BadRequest, $"{method} {path}: {text}");
        text.Contains(nameof(ProbeArticleFault.NotUpperCase), StringComparison.Ordinal).ShouldBe(namesTheMember, text);
        text.Contains(Refused, StringComparison.Ordinal).ShouldBe(quotesTheInput, text);
        text.ShouldNotContain(ProbeArticleFault.NotUpperCase.Description!);
    }

    [Theory]
    [InlineData("GET", "/minimal/route/%20ABC%20", null)]
    [InlineData("GET", "/minimal/query?sku=%20ABC%20", null)]
    [InlineData("POST", "/minimal/body", """{"sku":" ABC "}""")]
    [InlineData("GET", "/mvc/query?sku=%20ABC%20", null)]
    [InlineData("POST", "/mvc/body", """{"sku":" ABC "}""")]
    public async Task A_value_its_fault_set_accepts_is_bound_as_Create_made_it(string method, string path, string? json)
    {
        var (status, text) = await SendAsync(method, path, json);

        status.ShouldBe(HttpStatusCode.OK, $"{method} {path}: {text}");
        text.ShouldBe("ABC");
    }

    private static async Task<(HttpStatusCode Status, string Text)> SendAsync(string method, string path, string? json)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.FeatureProviders.Add(new OnlyControllers(typeof(ArticleController)));
        });

        await using var app = builder.Build();
        app.UseStatusCodePages();
        app.MapControllers();
        app.MapGet("/minimal/route/{sku}", (ProbeArticle sku) => sku.Value);
        app.MapGet("/minimal/query", ([FromQuery] ProbeArticle sku) => sku.Value);
        app.MapPost("/minimal/body", (ArticleHolder holder) => holder.Sku.Value);
        await app.StartAsync(cancellation);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await app.GetTestClient().SendAsync(request, cancellation);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellation));
    }

    public sealed record ArticleHolder(ProbeArticle Sku);

    private sealed class OnlyControllers(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    [ApiController]
    public sealed class ArticleController : ControllerBase
    {
        [HttpGet("/mvc/query")]
        public string Query([FromQuery] ProbeArticle sku) => sku.Value;

        [HttpPost("/mvc/body")]
        public string Body(ArticleHolder holder) => holder.Sku.Value;
    }
}
