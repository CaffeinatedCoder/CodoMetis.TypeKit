using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Refuses a document that would describe JSON the host's controllers do not write: ASP.NET builds the
/// document from the minimal-API JSON options (<c>ConfigureHttpJsonOptions</c>), and MVC writes with its
/// own (<c>AddControllers().AddJsonOptions</c>).
/// </summary>
/// <remarks>
/// A described converter or a <see cref="JsonSchemaDescriptions"/> on one of the two and not the other made
/// the document say <c>"string"</c> where a controller wrote a number, or the reverse, without a word
/// (measured: a controller wrote <c>{"amount":"7.8"}</c> under a document of numbers). So where the host has a
/// controller, both must carry the same kinds of each, compared by type; a host of minimal APIs alone is never
/// checked, since MVC's options write nothing there. The comparison is by type: two description sets of one
/// type with different entries are not told apart.
/// </remarks>
internal static class DescribedOptionsAgreement
{
    private static readonly ConditionalWeakTable<IServiceProvider, string> Disagreements = new();

    public static void ThrowIfDisagreeing(IServiceProvider services)
    {
        var disagreement = Disagreements.GetValue(services, Disagreement);
        if (disagreement.Length > 0) throw new InvalidOperationException(disagreement);
    }

    private static string Disagreement(IServiceProvider services)
    {
        var actions = services.GetService<IActionDescriptorCollectionProvider>();
        if (actions is null || !actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Any()) return "";

        var http = Described(services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions);
        var mvc = Described(services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);

        var mvcOnly = mvc.Except(http).Order(StringComparer.Ordinal).ToArray();
        var httpOnly = http.Except(mvc).Order(StringComparer.Ordinal).ToArray();
        if (mvcOnly.Length == 0 && httpOnly.Length == 0) return "";

        return "The OpenAPI document is built from the minimal-API JSON options (ConfigureHttpJsonOptions), and the controllers write with MVC's "
             + "(AddControllers().AddJsonOptions), and the two describe their JSON differently"
             + (mvcOnly.Length > 0 ? $"; on MVC's alone: {string.Join(", ", mvcOnly)}" : "")
             + (httpOnly.Length > 0 ? $"; on the minimal-API options alone: {string.Join(", ", httpOnly)}" : "")
             + ". Register each on both, or the document describes JSON the controllers do not write.";
    }

    private static HashSet<string> Described(JsonSerializerOptions options) =>
        [.. options.Converters.Where(converter => converter is IDescribedJsonConverter or JsonSchemaDescriptions).Select(converter => converter.GetType().Name)];
}
