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
/// when one is added. An operation given a fault response lists all its responses in ascending order,
/// the endpoint's own among them; an operation without one, and a document without one, is left as
/// ASP.NET writes it.
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

        var touched = false;
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
                 && DeclaredProblems(context, status, content) is { } schemas)
                {
                    problem ??= await ProblemSchemaAsync(context, cancellationToken);
                    touched = true;
                    foreach (var schema in schemas) WithFault(schema, Qualified(context));
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
            touched = true;
        }

        if (touched) Ascending(operation.Responses!);
    }

    /// <summary>
    /// Puts every response of an operation the transformer answered in ascending order, the ones the endpoint
    /// declares itself among them: added after those, faults at 404 and 412 beside a declared 409 read 200, 409,
    /// 404, 412 (reported 2026-10-15). A range such as <c>4XX</c> follows the codes of its range, and
    /// <c>default</c> comes last; a key that is neither keeps its place among the others, before <c>default</c>.
    /// </summary>
    private static void Ascending(OpenApiResponses responses)
    {
        var ordered = responses.OrderBy(response => Rank(response.Key)).ToList();
        if (ordered.Select(response => response.Key).SequenceEqual(responses.Keys, StringComparer.Ordinal)) return;

        // A dictionary cleared and filled again enumerates in the order it was filled, as the writer reads it.
        responses.Clear();
        foreach (var (key, response) in ordered) responses.Add(key, response);
    }

    private static (int Status, int Range) Rank(string key) =>
        key.Length == 3 && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var status) ? (status, 0)
      : key.Length == 3 && key[0] is >= '1' and <= '5' && key.EndsWith("XX", StringComparison.OrdinalIgnoreCase) ? ((key[0] - '0') * 100 + 99, 1)
      : string.Equals(key, "default", StringComparison.OrdinalIgnoreCase) ? (int.MaxValue, 1)
      : (int.MaxValue, 0);

    /// <summary>
    /// The problem schemas of a response the endpoint declares itself: its <c>application/problem+json</c>
    /// content's, and every content's whose type the endpoint declares as <see cref="ProblemDetails"/> or one
    /// derived from it, under the media type it declares. <c>Produces&lt;ProblemDetails&gt;(404)</c> and
    /// <c>[ProducesResponseType&lt;ProblemDetails&gt;(404)]</c> declare <c>application/json</c>, and read for
    /// <c>application/problem+json</c> alone their 404 described neither <c>fault</c> nor the problem types (reported
    /// 2026-10-16). A schema that refers to a component is that component, so a derived type's own one gets them;
    /// <see cref="ProblemDetails"/>'s gets them from <see cref="ProblemSchemaAsync"/> as well. A <c>$ref</c> under
    /// <c>application/problem+json</c> to a type that is no problem is left alone. Null where no content is a problem.
    /// </summary>
    private static List<IOpenApiSchema>? DeclaredProblems(OpenApiOperationTransformerContext context, int status, IDictionary<string, OpenApiMediaType> content)
    {
        var problems = context.Description.SupportedResponseTypes
                              .Where(declared => declared.StatusCode == status && declared.Type is { } type && typeof(ProblemDetails).IsAssignableFrom(type))
                              .ToList();
        List<IOpenApiSchema>? schemas = null;

        foreach (var (mediaType, media) in content)
        {
            var declaresProblem = problems.Any(declared => declared.ApiResponseFormats.Count == 0
                                                        || declared.ApiResponseFormats.Any(format => string.Equals(format.MediaType, mediaType, StringComparison.OrdinalIgnoreCase)));
            if (!declaresProblem && mediaType != "application/problem+json") continue;

            schemas ??= [];
            if (media.Schema is OpenApiSchema inline) schemas.Add(inline);
            else if (declaresProblem && media.Schema is OpenApiSchemaReference { Reference.Id: { } id }
                  && context.Document?.Components?.Schemas?.TryGetValue(id, out var component) == true && component is not null)
                schemas.Add(component);
        }

        return schemas;
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
