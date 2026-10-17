using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-037: with <c>MvcOptions.MaxValidationDepth = 1</c>, an <c>[ApiController]</c> action whose simple
/// parameter MVC cannot bind (route, query, header, form, body) runs anyway, with the parameter left at its default, and
/// answers 200; a class bound from the query makes the validator throw its depth refusal at its property, whether the
/// property was bound or not. With the default depth every unbindable request is a 400.
/// </summary>
public sealed class KB037
{
    public sealed class Q { public int V { get; set; } }

    [ApiController]
    public sealed class BindController : ControllerBase
    {
        [HttpGet("kb037/route/{v}")] public IActionResult Route([FromRoute(Name = "v")] int v) => Ok(v);
        [HttpGet("kb037/query")] public IActionResult Query([FromQuery(Name = "v")] int v) => Ok(v);
        [HttpGet("kb037/header")] public IActionResult Header([FromHeader(Name = "v")] int v) => Ok(v);
        [HttpPost("kb037/form")] public IActionResult Form([FromForm(Name = "v")] int v) => Ok(v);
        [HttpPost("kb037/body")] public IActionResult Body([FromBody] int v) => Ok(v);
        [HttpGet("kb037/params")] public IActionResult Params([FromQuery] Q q) => Ok(q.V);
    }

    /// <summary>Each binding source of a simple parameter, sent a text no <c>int</c> reads.</summary>
    public static TheoryData<string> Simple() => ["route", "query", "header", "form", "body"];

    /// <summary>The same, and the property of a class bound from the query.</summary>
    public static TheoryData<string> Sources() => ["route", "query", "header", "form", "body", "params"];

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(int? maxValidationDepth, string source, string text = "abc")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => { if (maxValidationDepth is { } depth) options.MaxValidationDepth = depth; })
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.FeatureProviders.Add(new Only());
            });

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var request = source switch
        {
            "route" => new HttpRequestMessage(HttpMethod.Get, $"/kb037/route/{text}"),
            "query" => new HttpRequestMessage(HttpMethod.Get, $"/kb037/query?v={text}"),
            "header" => new HttpRequestMessage(HttpMethod.Get, "/kb037/header") { Headers = { { "v", text } } },
            "form" => new HttpRequestMessage(HttpMethod.Post, "/kb037/form") { Content = new FormUrlEncodedContent([new("v", text)]) },
            "body" => new HttpRequestMessage(HttpMethod.Post, "/kb037/body") { Content = new StringContent(int.TryParse(text, out _) ? text : $"\"{text}\"", System.Text.Encoding.UTF8, "application/json") },
            _ => new HttpRequestMessage(HttpMethod.Get, $"/kb037/params?V={text}"),
        };
        var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory, MemberData(nameof(Simple))]
    public async Task Under_MaxValidationDepth_1_an_unbindable_parameter_is_its_default_and_the_action_answers_200(string source)
    {
        var (status, body) = await SendAsync(1, source);

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldBe("0", "the int is left at its default");
    }

    [Theory, InlineData("abc"), InlineData("5")]
    public async Task Under_MaxValidationDepth_1_a_class_from_the_query_makes_the_validator_throw_at_its_property(string text)
    {
        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(1, "params", text));

        thrown.Message.ShouldContain("ValidationVisitor exceeded the maximum configured validation depth '1' when validating property 'V'");
    }

    [Theory, MemberData(nameof(Simple))]
    public async Task Under_MaxValidationDepth_1_a_text_the_parameter_reads_binds_as_usual(string source)
    {
        var (status, body) = await SendAsync(1, source, "5");

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldBe("5");
    }

    [Theory, MemberData(nameof(Sources))]
    public async Task With_the_default_depth_the_same_request_is_a_400(string source)
    {
        var (status, body) = await SendAsync(null, source);

        status.ShouldBe(HttpStatusCode.BadRequest, body);
    }

    private sealed class Only : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(BindController).GetTypeInfo());
    }
}
