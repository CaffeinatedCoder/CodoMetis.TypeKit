using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.OpenApi;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-002: an MVC controller is in the OpenAPI document only with <c>[ApiController]</c>,
/// with <c>[ApiExplorerSettings(IgnoreApi = false)]</c>, or through a convention that sets
/// <c>ApiExplorer.IsVisible</c>.
/// </summary>
public sealed class KB002
{
    [ApiController]
    [Route("kb002/api-controller")]
    public sealed class WithApiControllerController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [ApiExplorerSettings(IgnoreApi = false)]
    [Route("kb002/settings")]
    public sealed class WithSettingsController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [Route("kb002/convention")]
    public sealed class ByConventionController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [Route("kb002/plain")]
    public sealed class PlainController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    /// <summary>Makes one controller visible to the API explorer, as an application could for its own.</summary>
    private sealed class Visible : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers.Where(controller => controller.ControllerType == typeof(ByConventionController)))
                controller.ApiExplorer.IsVisible = true;
        }
    }

    private static readonly Type[] Controllers = [typeof(WithApiControllerController), typeof(WithSettingsController), typeof(ByConventionController), typeof(PlainController)];

    private static async Task<IReadOnlyList<string>> PathsAsync(Action<MvcOptions>? mvc = null)
    {
        var document = await FactHost.DocumentAsync(OpenApiSpecVersion.OpenApi3_1, Controllers, mvc);
        return [.. (document["paths"]?.AsObject() ?? []).Select(path => path.Key).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task Only_a_controller_the_api_explorer_sees_is_in_the_document() =>
        (await PathsAsync(options => options.Conventions.Add(new Visible())))
            .ShouldBe(["/kb002/api-controller", "/kb002/convention", "/kb002/settings"], "the plain controller is left out");

    [Fact]
    public async Task Without_the_convention_a_controller_with_no_attribute_is_left_out() =>
        (await PathsAsync()).ShouldBe(["/kb002/api-controller", "/kb002/settings"]);
}
