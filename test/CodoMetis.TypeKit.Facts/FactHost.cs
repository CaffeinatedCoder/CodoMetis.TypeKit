using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// A bare ASP.NET host, with nothing of TypeKit's, serving the named controllers and the OpenAPI document
/// <c>Microsoft.AspNetCore.OpenApi</c> writes for them.
/// </summary>
internal static class FactHost
{
    /// <summary>
    /// The document of a host whose only controllers are <paramref name="controllers"/>, and whose only minimal
    /// endpoints are those <paramref name="minimal"/> maps.
    /// </summary>
    public static async Task<JsonNode> DocumentAsync(
        OpenApiSpecVersion version, Type[] controllers, Action<MvcOptions>? mvc = null, Action<IEndpointRouteBuilder>? minimal = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddControllers(options => mvc?.Invoke(options)).ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.FeatureProviders.Add(new Only(controllers));
        });
        builder.Services.AddOpenApi(options => options.OpenApiVersion = version);

        await using var app = builder.Build();
        app.MapControllers();
        minimal?.Invoke(app);
        app.MapOpenApi();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var text = await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        return JsonNode.Parse(text)!;
    }

    /// <summary>MVC discovers the top-level public controllers of its parts; this names the ones a fact needs, nested ones included, and only those.</summary>
    private sealed class Only(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }
}
