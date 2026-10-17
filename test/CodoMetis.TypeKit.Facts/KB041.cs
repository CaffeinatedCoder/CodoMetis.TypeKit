using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.Facts;

/// <summary>A source-generated context that lists <c>int</c> and nothing of MVC's: no <c>ValidationProblemDetails</c>.</summary>
[JsonSerializable(typeof(int))]
internal sealed partial class KB041Context : JsonSerializerContext;

/// <summary>
/// design/knowledge/KB-041: an <c>[ApiController]</c> action whose parameter MVC cannot bind answers its automatic 400 as a
/// <c>ValidationProblemDetails</c>, which MVC writes with its JSON options; where their resolver is a source-generated context that does
/// not list that type, the 400 is not written and the request fails with the serializer's exception, for any parameter type.
/// </summary>
public sealed class KB041
{
    [ApiController]
    public sealed class BindController : ControllerBase
    {
        [HttpGet("kb041/{v}")]
        public IActionResult Route([FromRoute(Name = "v")] int v) => Ok(v);
    }

    private static readonly FactFixture Fixture = FactFixture.Beside("KB-041");

    public static TheoryData<string> Answers() => [.. Fixture.Answers.Select(answer => answer.Id)];

    private static async Task<HttpResponseMessage> SendAsync(bool sourceGenerated)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers()
            .AddJsonOptions(options => { if (sourceGenerated) options.JsonSerializerOptions.TypeInfoResolver = KB041Context.Default; })
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.FeatureProviders.Add(new Only());
            });

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return await app.GetTestClient().GetAsync(new Uri("/kb041/abc", UriKind.Relative), TestContext.Current.CancellationToken);
    }

    /// <summary>Each answer of KB041.fixture.json: <c>abc</c> sent to the action's <c>int</c>, under the resolver the answer names.</summary>
    [Theory, MemberData(nameof(Answers))]
    public async Task MVC_answers_a_refused_binding_as_the_fixture_holds(string id)
    {
        var answer = Fixture[id];
        var sourceGenerated = answer.When["json.TypeInfoResolver"] == "source-generated-context";

        if (answer.Kind == FactAnswer.Exception)
        {
            var thrown = await Should.ThrowAsync<Exception>(() => SendAsync(sourceGenerated));
            answer.Mismatch(thrown).ShouldBeNull();
        }
        else
        {
            using var response = await SendAsync(sourceGenerated);
            answer.Mismatch((int)response.StatusCode, "").ShouldBeNull();
        }
    }

    private sealed class Only : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(BindController).GetTypeInfo());
    }
}
