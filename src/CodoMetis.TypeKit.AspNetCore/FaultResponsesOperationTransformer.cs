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
/// Adds a problem-details response for every status an endpoint's fault type is mapped to, as
/// <c>ProducesProblem(status)</c> would.
/// </summary>
/// <remarks>
/// The mapping is read here, from the application's services, and not in the endpoint's metadata,
/// which MVC builds without them (spikes/ResultHttp, finding 4). ASP.NET has already put schemas into
/// components when operation transformers run, so the problem schema is added as the component the
/// host's <see cref="OpenApiOptions.CreateSchemaReferenceId"/> names, and referred to (finding 6). A
/// response the operation already has is kept, which also makes a second <c>AddTypeKit()</c> a no-op.
/// </remarks>
internal sealed class FaultResponsesOperationTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        IOpenApiSchema? problem = null;

        foreach (var metadata in context.Description.ActionDescriptor.EndpointMetadata.OfType<FaultResponsesMetadata>().Distinct())
        {
            var statuses = metadata.Statuses(context.ApplicationServices)
                        ?? throw new InvalidOperationException(
                               $"No HTTP status is mapped for the fault type {metadata.FaultName}. " +
                               $"Map each of its members with services.AddFaultResponses<{metadata.FaultName}>(fault => …)." + FaultRefusals.Mapping);

            foreach (var status in statuses)
            {
                var key = status.ToString(CultureInfo.InvariantCulture);
                operation.Responses ??= [];
                if (operation.Responses.ContainsKey(key)) continue;

                problem ??= await ProblemSchemaAsync(context, cancellationToken);
                operation.Responses[key] = new OpenApiResponse
                {
                    Description = ReasonPhrases.GetReasonPhrase(status),
                    Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = problem } },
                };
            }
        }
    }

    private static async Task<IOpenApiSchema> ProblemSchemaAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), parameterDescription: null, cancellationToken);

        var json = context.ApplicationServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var id = context.ApplicationServices.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName)
                        .CreateSchemaReferenceId(json.GetTypeInfo(typeof(ProblemDetails)));

        // A host that inlines it gets it inline, as ASP.NET writes its own problem responses there.
        if (id is null || context.Document is not { } document) return schema;

        if (document.Components?.Schemas?.ContainsKey(id) != true) document.AddComponent(id, schema);

        return new OpenApiSchemaReference(id, document);
    }
}
