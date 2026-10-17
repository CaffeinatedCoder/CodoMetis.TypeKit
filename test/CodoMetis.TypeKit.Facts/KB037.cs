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

    private static readonly FactFixture Fixture = FactFixture.Beside("KB-037");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

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

    /// <summary>
    /// Each answer of KB037.fixture.json, sent as its settings say: the binding source (<c>as-parameters</c> is a class
    /// bound from the query), the text, and <c>MaxValidationDepth</c> 1 or the default.
    /// </summary>
    [Theory, MemberData(nameof(Answers))]
    public async Task MVC_answers_as_the_fixture_holds(string id)
    {
        var answer = Fixture[id];
        var depth  = answer.When["mvc.MaxValidationDepth"] == "default" ? (int?)null : int.Parse(answer.When["mvc.MaxValidationDepth"], System.Globalization.CultureInfo.InvariantCulture);
        var source = answer.When["http.binding"] == "as-parameters" ? "params" : answer.When["http.binding"];

        if (answer.Kind == FactAnswer.Exception)
        {
            var thrown = await Should.ThrowAsync<Exception>(() => SendAsync(depth, source, answer.When["text"]));
            answer.Mismatch(thrown).ShouldBeNull();
        }
        else
        {
            var (status, body) = await SendAsync(depth, source, answer.When["text"]);
            answer.Mismatch((int)status, body).ShouldBeNull();
        }
    }

    private sealed class Only : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(BindController).GetTypeInfo());
    }
}
