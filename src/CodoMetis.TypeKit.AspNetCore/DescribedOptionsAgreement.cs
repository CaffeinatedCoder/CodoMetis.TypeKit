using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Refuses a document that would describe JSON the host's controllers do not write: ASP.NET builds the
/// document from the minimal-API JSON options (<c>ConfigureHttpJsonOptions</c>), and MVC writes with its
/// own (<c>AddControllers().AddJsonOptions</c>).
/// </summary>
/// <remarks>
/// <para>
/// A described converter or a <see cref="JsonSchemaDescriptions"/> on one of the two and not the other made
/// the document say <c>"string"</c> where a controller wrote a number, or the reverse, without a word
/// (measured: a controller wrote <c>{"amount":"7.8"}</c> under a document of numbers). So where the host has a
/// controller, every type the document describes is described under both, and the two must say the same;
/// a host of minimal APIs alone is never checked, since MVC's options write nothing there.
/// </para>
/// <para>
/// What is compared is what each set of options says of the type, as <see cref="JsonSchemaDescription.For"/>
/// answers it: the converter the serializer picks, in the order the options list them, and the descriptions'
/// entries, not the converters' names. Two sets that list the same converters in another order, or carry
/// description sets of one type with different entries, are told apart; two that differ only where neither
/// says anything are not this check's to judge.
/// </para>
/// </remarks>
internal static class DescribedOptionsAgreement
{
    private static readonly ConditionalWeakTable<IServiceProvider, StrongBox<JsonSerializerOptions?>> Controllers = new();

    /// <summary>Throws where MVC's options describe <paramref name="type"/> otherwise than the document's.</summary>
    public static void ThrowIfDisagreeing(IServiceProvider services, Type type, JsonSerializerOptions document)
    {
        if (Controllers.GetValue(services, MvcOptions).Value is not { } mvc || ReferenceEquals(mvc, document)) return;

        var http = JsonSchemaDescription.For(type, document);
        var controllers = JsonSchemaDescription.For(type, mvc);
        if (Same(http, controllers)) return;

        throw new InvalidOperationException(
            $"The OpenAPI document is built from the minimal-API JSON options (ConfigureHttpJsonOptions), and the controllers write with MVC's "
          + $"(AddControllers().AddJsonOptions), and the two describe '{type}' differently: the minimal-API options as {Say(http)}, MVC's as {Say(controllers)}. "
          + "Register the same converters and descriptions on both, in the same order, or the document describes JSON the controllers do not write.");
    }

    /// <summary>MVC's options where the host has a controller, otherwise none.</summary>
    private static StrongBox<JsonSerializerOptions?> MvcOptions(IServiceProvider services)
    {
        var actions = services.GetService<IActionDescriptorCollectionProvider>();
        if (actions is null || !actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Any()) return new(null);

        return new(services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);
    }

    /// <summary>Whether two descriptions say the same: the same type's schema, or equal schemas.</summary>
    internal static bool Same(JsonSchemaDescription? left, JsonSchemaDescription? right) => (left, right) switch
    {
        (null, null) => true,
        ({ SameAsType: { } a }, { SameAsType: { } b }) => a == b,
        ({ Schema: { } a }, { Schema: { } b }) => JsonNode.DeepEquals(a, b),
        _ => false,
    };

    private static string Say(JsonSchemaDescription? description) => description switch
    {
        null => "nothing (no converter in charge describes it)",
        { SameAsType: { } type } => $"the schema of '{type}'",
        _ => description.Schema!.ToJsonString(),
    };
}
