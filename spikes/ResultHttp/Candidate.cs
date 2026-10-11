using System.Reflection;
using CodoMetis.TypeKit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ResultHttp;

/// <summary>How the host answers each fault of one type: a status per fault, and every status it can give.</summary>
public sealed class FaultResponses<TFault> where TFault : notnull
{
    private readonly Func<TFault, int> _status;

    internal FaultResponses(Func<TFault, int> status, IReadOnlyList<int> statuses)
    {
        _status = status;
        Statuses = statuses;
    }

    public IReadOnlyList<int> Statuses { get; }

    public int StatusOf(TFault fault) => _status(fault);
}

public static class FaultResponseServiceCollectionExtensions
{
    /// <summary>An enum fault: its statuses are read from its members, so the document lists every one.</summary>
    public static IServiceCollection AddFaultResponses<TFault>(this IServiceCollection services, Func<TFault, int> status)
        where TFault : struct, Enum
    {
        var statuses = Enum.GetValues<TFault>().Select(status).Distinct().Order().ToArray();
        // Finding: under Native AOT a problem is written only with ASP.NET's problem-details JSON
        // context, which AddProblemDetails() puts into the host's resolver chain.
        if (Environment.GetEnvironmentVariable("SPIKE_NO_PROBLEM_DETAILS") is null) services.AddProblemDetails();
        return services.AddSingleton(new FaultResponses<TFault>(status, statuses));
    }
}

/// <summary>A fault as RFC 9457 problem details, with the status the host mapped it to.</summary>
public sealed class FaultProblem<TFault> : IResult, IEndpointMetadataProvider where TFault : notnull
{
    private readonly TFault _fault;

    internal FaultProblem(TFault fault) => _fault = fault;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var responses = httpContext.RequestServices.GetService<FaultResponses<TFault>>() ?? throw Unmapped();
        return TypedResults.Problem(
                statusCode: responses.StatusOf(_fault),
                extensions: new Dictionary<string, object?> { ["fault"] = _fault.ToString() })
            .ExecuteAsync(httpContext);
    }

    // Candidate 2: no services here. MVC builds its application model with an EndpointBuilder whose
    // ApplicationServices is empty, so the endpoint records only which fault type it answers with.
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        // SPIKE_CANDIDATE1 restores the first candidate: the statuses read from the endpoint's services.
        if (Environment.GetEnvironmentVariable("SPIKE_CANDIDATE1") is not null)
        {
            Console.WriteLine($"  PopulateMetadata<{typeof(TFault).Name}> for {method.Name}: {builder.GetType().Name}, services {builder.ApplicationServices.GetType().FullName}");
            var responses = builder.ApplicationServices.GetService<FaultResponses<TFault>>() ?? throw Unmapped();
            foreach (var status in responses.Statuses)
                builder.Metadata.Add(new ProducesResponseTypeMetadata(status, typeof(ProblemDetails), ["application/problem+json"]));
            return;
        }

        builder.Metadata.Add(FaultResponsesMetadata<TFault>.Instance);
    }

    internal static InvalidOperationException Unmapped() =>
        new($"No status is mapped for {typeof(TFault).Name}. Call services.AddFaultResponses<{typeof(TFault).Name}>(…).");
}
/// <summary>Shape A: the handler says which response a result is.</summary>
public static class ResultHttpExtensions
{
    public static Results<Ok<T>, FaultProblem<TError>> ToHttpResult<T, TError>(this Result<T, TError> result)
        where T : notnull where TError : notnull =>
        result.Match<Results<Ok<T>, FaultProblem<TError>>>(value => TypedResults.Ok(value), fault => new FaultProblem<TError>(fault));

    public static Results<NoContent, FaultProblem<TError>> ToHttpResult<TError>(this Result<TError> result)
        where TError : notnull =>
        result.Match<Results<NoContent, FaultProblem<TError>>>(() => TypedResults.NoContent(), fault => new FaultProblem<TError>(fault));
}

/// <summary>Which fault type an endpoint answers with; typed, so reading its mapping needs no reflection.</summary>
public abstract class FaultResponsesMetadata
{
    public abstract string FaultName { get; }

    /// <summary>The statuses the host mapped, or null when it mapped none.</summary>
    public abstract IReadOnlyList<int>? Statuses(IServiceProvider services);
}

public sealed class FaultResponsesMetadata<TFault> : FaultResponsesMetadata where TFault : notnull
{
    public static readonly FaultResponsesMetadata<TFault> Instance = new();

    public override string FaultName => typeof(TFault).Name;

    public override IReadOnlyList<int>? Statuses(IServiceProvider services) => services.GetService<FaultResponses<TFault>>()?.Statuses;
}

/// <summary>Adds a problem-details response per mapped status, from the application's own services.</summary>
public sealed class FaultResponsesOperationTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var metadata in context.Description.ActionDescriptor.EndpointMetadata.OfType<FaultResponsesMetadata>().Distinct())
        {
            var statuses = metadata.Statuses(context.ApplicationServices) ?? throw new InvalidOperationException($"No status is mapped for {metadata.FaultName}.");
            var schema = await ProblemSchemaAsync(context, cancellationToken);
            operation.Responses ??= new OpenApiResponses();
            foreach (var status in statuses)
            {
                var key = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (operation.Responses.ContainsKey(key)) continue;
                operation.Responses[key] = new OpenApiResponse
                {
                    Description = ReasonPhrases(status),
                    Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = schema } },
                };
            }
        }
    }

    // ASP.NET hoists schemas into components before operation transformers run, so one added here
    // stays inline unless the transformer registers it and refers to it.
    private static async Task<IOpenApiSchema> ProblemSchemaAsync(OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        const string id = "ProblemDetails";
        var document = context.Document!;
        if (document.Components?.Schemas?.ContainsKey(id) != true)
        {
            var schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
            if (Environment.GetEnvironmentVariable("SPIKE_TRACE") is not null)
                Console.WriteLine($"  problem schema metadata: {string.Join(", ", schema.Metadata?.Select(m => $"{m.Key}={m.Value}") ?? [])}");
            document.AddComponent(id, schema);
        }
        return new OpenApiSchemaReference(id, document);
    }

    private static string ReasonPhrases(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
}

/// <summary>
/// Refuses to start a host with an endpoint whose fault type has no mapping. A startup filter, because
/// endpoints are known only once the pipeline is built, which happens after hosted services start and
/// before the server listens.
/// </summary>
public sealed class FaultResponsesStartupCheck : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        var services = app.ApplicationServices;
        var endpoints = services.GetRequiredService<EndpointDataSource>();
        if (Environment.GetEnvironmentVariable("SPIKE_TRACE") is not null)
            Console.WriteLine($"  startup check sees {endpoints.Endpoints.Count} endpoints in {endpoints.GetType().Name}");
        var unmapped = endpoints.Endpoints
            .SelectMany(e => e.Metadata.OfType<FaultResponsesMetadata>().Select(m => (Endpoint: e.DisplayName, Metadata: m)))
            .Where(x => x.Metadata.Statuses(services) is null)
            .ToList();
        if (unmapped.Count > 0)
            throw new InvalidOperationException("No status is mapped for " + string.Join("; ", unmapped.Select(x => $"{x.Metadata.FaultName} ({x.Endpoint})")) + ".");
    };
}
