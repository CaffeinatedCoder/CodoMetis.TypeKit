using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
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
/// response the operation already has is kept, which also makes a second <c>AddTypeKit()</c> a no-op;
/// a problem response among them still gets <c>fault</c> and <c>rule</c>, and the problem types where its
/// description is the reason phrase alone.
/// A response's description is the status's reason phrase, followed by the problem types of the
/// members answered with it where the mapping gives any. The problem schema those responses refer to
/// gains an optional <c>fault</c> string, the member a client of a fault response matches on, and an
/// optional <c>rule</c> string, the rule that refused where the fault names one: on the
/// shared component, which ASP.NET's own problem responses refer to as well, so its description says
/// which problems carry it. A string, since an enum of every fault type would change every operation
/// when one is added. A document without a fault response is left as ASP.NET writes it.
/// </remarks>
internal sealed class FaultResponsesOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>The problem's extension member <see cref="FaultProblem{TFault}"/> writes.</summary>
    internal const string FaultMember = "fault";

    /// <summary>The problem's extension member naming the rule that refused, where the fault names one.</summary>
    internal const string RuleMember = "rule";

    /// <summary>What <c>rule</c> holds.</summary>
    internal const string RuleDescription =
        "The rule that refused, where the fault answers for several: its type's name and its member's (Type.Member, a nested type after its nesting chain)."
      + " Present only on a problem whose fault names one.";

    /// <summary>What <c>fault</c> holds, as the host writes it: the member's name, or its qualified name.</summary>
    internal static string FaultDescription(bool qualified) =>
        (qualified
            ? "The fault this problem reports: the name of its member, qualified with its type's (Type.Member, a nested type after its nesting chain)."
            : "The fault this problem reports: the name of its member.")
      + " Present only on a problem that answers a fault of the endpoint's Result.";

    /// <summary>
    /// The metadata of the endpoint the operation describes, as the response and the startup check read it.
    /// </summary>
    /// <remarks>
    /// An MVC action's descriptor holds its attributes alone; what a convention builder adds, such as the
    /// one <c>MapControllers()</c> returns, is on the endpoint only. Read from the descriptor, a
    /// <c>WithFaultStatus</c> there answered 451 where the document said 404, and a <c>WithoutFaults</c>
    /// left in the document a member the endpoint refuses to answer with (reported 2026-10-14). The
    /// endpoint is the one whose metadata holds the operation's action; a minimal API's descriptor is built
    /// from its endpoint's metadata already, and holds no action.
    /// </remarks>
    private static IReadOnlyList<object> EndpointMetadata(OpenApiOperationTransformerContext context)
    {
        var action = context.Description.ActionDescriptor;

        if (context.ApplicationServices.GetService<EndpointDataSource>() is { } endpoints)
        {
            foreach (var endpoint in endpoints.Endpoints)
            {
                if (ReferenceEquals(endpoint.Metadata.GetMetadata<ActionDescriptor>(), action)) return [.. endpoint.Metadata];
            }
        }

        return [.. action.EndpointMetadata];
    }

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        IOpenApiSchema? problem = null;
        var endpointMetadata = EndpointMetadata(context);

        // Every fault type the endpoint answers with, a Fault<T1, T2>'s each: a status two of them share is one
        // response, which lists the problem types of both, and the statuses come in ascending order.
        var answered = new SortedDictionary<int, List<string>>();
        foreach (var metadata in endpointMetadata.OfType<FaultResponsesMetadata>().Distinct())
        {
            var statuses = metadata.Statuses(context.ApplicationServices, endpointMetadata)
                        ?? throw new InvalidOperationException(
                               $"No HTTP status is mapped for the fault type {metadata.FaultName}. " +
                               $"Map each of its members with services.AddFaultResponses<{metadata.FaultName}>(fault => …)." + FaultRefusals.Mapping);

            foreach (var status in statuses)
            {
                if (!answered.TryGetValue(status, out var types)) answered[status] = types = [];
                types.AddRange(metadata.Types(context.ApplicationServices, status, endpointMetadata).Except(types, StringComparer.Ordinal).ToList());
            }
        }

        foreach (var (status, types) in answered)
        {
            var key = status.ToString(CultureInfo.InvariantCulture);
            operation.Responses ??= [];

            if (operation.Responses.TryGetValue(key, out var declared))
            {
                // A problem response the endpoint declares itself (ProducesProblem, [ProducesResponseType]) is
                // kept, but it answers the fault too: its problem schema gets fault and rule, and a description
                // nobody wrote gets the problem types. Skipped whole, a host declaring every status described
                // neither, though the problems carried fault (reported 2026-10-14).
                if (declared is OpenApiResponse { Content: { } content } response
                 && content.TryGetValue("application/problem+json", out var media))
                {
                    problem ??= await ProblemSchemaAsync(context, cancellationToken);
                    if (media.Schema is OpenApiSchema inline) WithFault(inline, Qualified(context));
                    if (string.IsNullOrEmpty(response.Description) || response.Description == ReasonPhrases.GetReasonPhrase(status))
                        response.Description = Description(status, types);
                }

                continue;
            }

            problem ??= await ProblemSchemaAsync(context, cancellationToken);
            operation.Responses[key] = new OpenApiResponse
            {
                Description = Description(status, types),
                Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = problem } },
            };
        }
    }

    private static string Description(int status, List<string> types) =>
        types.Count == 0
            ? ReasonPhrases.GetReasonPhrase(status)
            : $"{ReasonPhrases.GetReasonPhrase(status)}. Problem types: {string.Join(", ", types)}";

    private static bool Qualified(OpenApiOperationTransformerContext context) =>
        context.ApplicationServices.GetService<IOptions<FaultResponseOptions>>()?.Value.QualifiedFaultNames == true;

    private static async Task<IOpenApiSchema> ProblemSchemaAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), parameterDescription: null, cancellationToken);

        var services = context.ApplicationServices;
        var json = services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var id = services.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(context.DocumentName)
                         .CreateSchemaReferenceId(json.GetTypeInfo(typeof(ProblemDetails)));
        var qualified = Qualified(context);

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
    /// Describes <c>fault</c> and <c>rule</c> on the problem schema, beside what ASP.NET describes of
    /// <see cref="ProblemDetails"/>, where nothing describes them yet: one another transformer already
    /// gave the schema is kept, and a second fault response finds them there.
    /// </summary>
    private static IOpenApiSchema WithFault(IOpenApiSchema schema, bool qualified)
    {
        if (schema is OpenApiSchema problem)
        {
            problem.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            problem.Properties.TryAdd(FaultMember, new OpenApiSchema { Type = JsonSchemaType.String, Description = FaultDescription(qualified) });
            problem.Properties.TryAdd(RuleMember, new OpenApiSchema { Type = JsonSchemaType.String, Description = RuleDescription });
        }

        return schema;
    }
}
