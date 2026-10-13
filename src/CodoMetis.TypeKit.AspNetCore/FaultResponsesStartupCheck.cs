using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Refuses to start a host with an endpoint whose fault type has no mapping, or whose
/// <c>WithFaultStatus</c> names a fault type it does not answer with or a member its type does not
/// have, naming each endpoint.
/// </summary>
/// <remarks>
/// A startup filter, because the endpoints are known only once the pipeline is built: a hosted service
/// starts before that and sees none (spikes/ResultHttp, finding 8). Its exception fails the host's
/// start before the server listens. Without it a missing mapping shows only when that fault first
/// occurs, in production (finding 7).
/// </remarks>
internal sealed class FaultResponsesStartupCheck : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        var services = app.ApplicationServices;
        if (services.GetService<EndpointDataSource>() is not { } endpoints) return;

        var unmapped = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<FaultResponsesMetadata>().Select(metadata => (Endpoint: endpoint, Metadata: metadata)))
            .Where(pair => pair.Metadata.Statuses(services, pair.Endpoint.Metadata) is null)
            .GroupBy(pair => pair.Metadata.FaultName, pair => Name(pair.Endpoint))
            .Select(fault =>
                $"No HTTP status is mapped for the fault type {fault.Key}, which these endpoints answer with: " +
                $"{string.Join(", ", fault.Distinct())}. Map each of its members with services.AddFaultResponses<{fault.Key}>(fault => …).");

        // A status for a fault type the endpoint never answers with is stale, or set on the wrong endpoint.
        var stray = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<IFaultStatus>()
                .Where(status => !endpoint.Metadata.OfType<FaultResponsesMetadata>().Any(answered => answered.FaultType == status.FaultType))
                .Select(status => $"{status.Describe()} is set on {Name(endpoint)}, which answers with no {status.FaultType.Name}."));

        var nameless = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<FaultResponsesMetadata>().Distinct()
                .SelectMany(metadata => metadata.Problems(services, endpoint.Metadata))
                .Select(problem => $"{problem}, on {Name(endpoint)}."));

        var refusals = unmapped.Concat(stray).Concat(nameless).ToList();
        if (refusals.Count == 0) return;

        throw new InvalidOperationException(string.Join(" ", refusals) + FaultRefusals.Mapping);
    };

    private static string Name(Endpoint endpoint) => endpoint.DisplayName ?? endpoint.ToString() ?? "an endpoint";
}
