using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Adds a problem-details response for every status an endpoint answers its fault type with, as
/// <c>ProducesProblem(status)</c> would: the mapped ones, with the endpoint's <c>WithFaultStatus</c> in
/// place of the mapped status for the members it names.
/// </summary>
/// <remarks>
/// The mapping is read here, from the application's services, and not in the endpoint's metadata,
/// which MVC builds without them (spikes/ResultHttp, finding 4). ASP.NET has already put schemas into
/// components when operation transformers run, so the problem schema is added as the component the
/// host's <see cref="OpenApiOptions.CreateSchemaReferenceId"/> names, and referred to (finding 6). A
/// response the operation already has is kept, which also makes a second <c>AddTypeKit()</c> a no-op.
/// A response's description is the status's reason phrase, followed by the problem types of the
/// members answered with it where the mapping gives any. The problem schema those responses refer to
/// gains an optional <c>fault</c> string, the member a client of a fault response matches on: on the
/// shared component, which ASP.NET's own problem responses refer to as well, so its description says
/// which problems carry it. A string, since an enum of every fault type would change every operation
/// when one is added. A document without a fault response is left as ASP.NET writes it.
/// </remarks>
internal sealed class FaultResponsesOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The problem's extension member <see cref="FaultProblem{TFault}"/> writes.</summary>
    internal const string FaultMember = "fault";

    /// <summary>What <c>fault</c> holds, as the host writes it: the member's name, or its qualified name.</summary>
    internal static string FaultDescription(bool qualified) =>
        (qualified
            ? "The fault this problem reports: the name of its member, qualified with its type's (Type.Member, a nested type after its nesting chain)."
            : "The fault this problem reports: the name of its member.")
      + " Present only on a problem that answers a fault of the endpoint's Result.";

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        IOpenApiSchema? problem = null;
        IReadOnlyList<object> endpointMetadata = [.. context.Description.ActionDescriptor.EndpointMetadata];

        foreach (var metadata in endpointMetadata.OfType<FaultResponsesMetadata>().Distinct())
        {
            var statuses = metadata.Statuses(context.ApplicationServices, endpointMetadata)
                        ?? throw new InvalidOperationException(
                               $"No HTTP status is mapped for the fault type {metadata.FaultName}. " +
                               $"Map each of its members with services.AddFaultResponses<{metadata.FaultName}>(fault => …)." + FaultRefusals.Mapping);

            foreach (var status in statuses)
            {
                var key = status.ToString(CultureInfo.InvariantCulture);
                operation.Responses ??= [];
                if (operation.Responses.ContainsKey(key)) continue;

                problem ??= await ProblemSchemaAsync(context, cancellationToken);
                var types = metadata.Types(context.ApplicationServices, status, endpointMetadata);
                operation.Responses[key] = new OpenApiResponse
                {
                    Description = types.Count == 0
                        ? ReasonPhrases.GetReasonPhrase(status)
                        : $"{ReasonPhrases.GetReasonPhrase(status)}. Problem types: {string.Join(", ", types)}",
                    Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = problem } },
                };
            }
        }
    }

    private static async Task<IOpenApiSchema> ProblemSchemaAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), parameterDescription: null, cancellationToken);

        var services = context.ApplicationServices;
        var json = services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var id = services.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName)
                         .CreateSchemaReferenceId(json.GetTypeInfo(typeof(ProblemDetails)));
        var qualified = services.GetService<IOptions<FaultResponseOptions>>()?.Value.QualifiedFaultNames == true;

        // A host that inlines it gets it inline, as ASP.NET writes its own problem responses there.
        if (id is null || context.Document is not { } document) return WithFault(schema, qualified);

        if (document.Components?.Schemas?.TryGetValue(id, out var component) != true || component is null)
        {
            document.AddComponent(id, schema);
            component = schema;
        }

        WithFault(component, qualified);
        return new OpenApiSchemaReference(id, document);
    }

    /// <summary>
    /// Describes <c>fault</c> on the problem schema, beside what ASP.NET describes of
    /// <see cref="ProblemDetails"/>, where nothing describes it yet: a <c>fault</c> another transformer
    /// already gave the schema is kept, and a second fault response finds it there.
    /// </summary>
    private static IOpenApiSchema WithFault(IOpenApiSchema schema, bool qualified)
    {
        if (schema is OpenApiSchema problem && problem.Properties?.ContainsKey(FaultMember) != true)
        {
            problem.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            problem.Properties[FaultMember] = new OpenApiSchema { Type = JsonSchemaType.String, Description = FaultDescription(qualified) };
        }

        return schema;
    }
}
