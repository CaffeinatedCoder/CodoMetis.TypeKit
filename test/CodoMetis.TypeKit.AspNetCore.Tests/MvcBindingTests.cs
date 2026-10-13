using System.Globalization;
using System.Reflection;
using CodoMetis.TypeKit.Generators.Probes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodoMetis.TypeKit.AspNetCore.Tests;

/// <summary>
/// A query, header or form value missing from an MVC request reaches the action as <c>default</c> for a
/// struct value object, an instance no factory made, with the model state valid: MVC's rule for any
/// non-nullable struct, as for a <see cref="Guid"/> or an <see cref="int"/>. A minimal API answers 400,
/// and MVC already requires a class value object or a closed set, as any non-nullable reference type.
/// The README's Binding section and docs/aspnet-core.md say so, with the remedy pinned here:
/// <c>[BindRequired]</c> answers 400, and a nullable parameter receives <see langword="null"/>.
/// </summary>
public sealed class MvcBindingTests
{
    private const string Default = "default, model state valid";

    [Theory]
    // The hazard: MVC binds nothing and calls the action.
    [InlineData("GET", "/mvc/query", HttpStatusCode.OK, Default)]
    [InlineData("GET", "/mvc/header", HttpStatusCode.OK, Default)]
    [InlineData("POST", "/mvc/form", HttpStatusCode.OK, Default)]
    [InlineData("GET", "/mvc/wrapped", HttpStatusCode.OK, Default)]   // the wrapped types alike: MVC's rule, not the value object's
    // Already refused: a class value object and a closed set in MVC, non-nullable reference types, and a minimal API.
    [InlineData("GET", "/mvc/class", HttpStatusCode.BadRequest, null)]
    [InlineData("GET", "/mvc/set", HttpStatusCode.BadRequest, null)]
    [InlineData("GET", "/minimal/query", HttpStatusCode.BadRequest, null)]
    // The remedy: [BindRequired], or a nullable parameter.
    [InlineData("GET", "/mvc/required/query", HttpStatusCode.BadRequest, "A value for the 'count' parameter or property was not provided.")]
    [InlineData("GET", "/mvc/required/header", HttpStatusCode.BadRequest, "A value for the 'X-Id' parameter or property was not provided.")]
    [InlineData("POST", "/mvc/required/form", HttpStatusCode.BadRequest, "A value for the 'count' parameter or property was not provided.")]
    [InlineData("GET", "/mvc/required/query?count=3", HttpStatusCode.OK, "3")]
    [InlineData("GET", "/mvc/nullable", HttpStatusCode.OK, "null")]
    public async Task A_missing_value_reaches_an_MVC_action_as_default_unless_it_is_required_or_nullable(string method, string path, HttpStatusCode status, string? body)
    {
        await using var app = await StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = new FormUrlEncodedContent([]);
        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(status, $"{method} {path}: {text}");
        if (body is not null) text.ShouldContain(body);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
        {
            parts.ApplicationParts.Clear();
            parts.FeatureProviders.Add(new OnlyControllers(typeof(BindingController)));
        });

        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/minimal/query", ([FromQuery] ProbeCount count) => count.Value.ToString(CultureInfo.InvariantCulture));

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

    private sealed class OnlyControllers(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    [ApiController]
    public sealed class BindingController : ControllerBase
    {
        [HttpGet("/mvc/query")]
        public string Query([FromQuery] ProbeCode code, [FromQuery] ProbeCount count) => Seen(code.Value is null && count.Value == 0);

        [HttpGet("/mvc/header")]
        public string Header([FromHeader(Name = "X-Id")] ProbeId id) => Seen(id.Value == Guid.Empty);

        [HttpPost("/mvc/form")]
        public string Form([FromForm] ProbeCount count) => Seen(count.Value == 0);

        [HttpGet("/mvc/wrapped")]
        public string Wrapped([FromQuery] Guid id, [FromQuery] int count) => Seen(id == Guid.Empty && count == 0);

        [HttpGet("/mvc/class")]
        public string Class([FromQuery] ProbeLabel label) => label.Value;

        [HttpGet("/mvc/set")]
        public string Set([FromQuery] ProbeStatus status) => status.ToString();

        [HttpGet("/mvc/required/query")]
        public string RequiredQuery([FromQuery, BindRequired] ProbeCount count) => count.Value.ToString(CultureInfo.InvariantCulture);

        [HttpGet("/mvc/required/header")]
        public string RequiredHeader([FromHeader(Name = "X-Id"), BindRequired] ProbeId id) => id.Value.ToString();

        [HttpPost("/mvc/required/form")]
        public string RequiredForm([FromForm, BindRequired] ProbeCount count) => count.Value.ToString(CultureInfo.InvariantCulture);

        [HttpGet("/mvc/nullable")]
        public string Nullable([FromQuery] ProbeCount? count) => count is { } given ? given.Value.ToString(CultureInfo.InvariantCulture) : "null";

        private string Seen(bool isDefault) => isDefault && ModelState.IsValid ? Default : "bound";
    }
}
