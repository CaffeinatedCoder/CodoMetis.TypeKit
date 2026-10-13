using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Refuses to start a host with an endpoint whose fault type has no mapping, a fault status whose
/// scope has no endpoint that answers with its fault type, or a fault status naming a member its type
/// does not have, naming each endpoint.
/// </summary>
/// <remarks>
/// <para>
/// A startup filter, because the endpoints are known only once the pipeline is built: a hosted service
/// starts before that and sees none (spikes/ResultHttp, finding 8). Its exception fails the host's
/// start before the server listens. Without it a missing mapping shows only when that fault first
/// occurs, in production (finding 7).
/// </para>
/// <para>
/// A status's scope is where it was declared (decision 61): one endpoint or action, which must answer
/// with the fault type; or a route group, a convention builder or a controller, of which one endpoint
/// must. Each declaration is one instance that every endpoint of its scope carries in its metadata, so
/// the scope is read from the endpoints that hold that instance.
/// </para>
/// </remarks>
internal sealed class FaultResponsesStartupCheck : IStartupFilter
{
    /// <summary>
    /// One declaration is one instance: an attribute's own <c>Equals</c> compares its values, so two
    /// controllers' equal attributes would read as one scope, and it reads them by reflection.
    /// </summary>
    private static readonly IEqualityComparer<IFaultStatus> DeclarationComparer = ReferenceEqualityComparer.Instance;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        var services = app.ApplicationServices;
        if (services.GetService<EndpointDataSource>() is not { } endpoints) return;

        var unmapped = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<FaultResponsesMetadata>().Select(metadata => (Endpoint: endpoint, Metadata: metadata)))
            .Where(pair => pair.Metadata.Statuses(services, pair.Endpoint.Metadata) is null)
            // By type, not by name: two nested types of one simple name are two refusals.
            .GroupBy(pair => pair.Metadata.FaultType, pair => (pair.Metadata.FaultName, Endpoint: Name(pair.Endpoint)))
            .Select(fault =>
                $"No HTTP status is mapped for the fault type {fault.First().FaultName}, which these endpoints answer with: " +
                $"{string.Join(", ", fault.Select(pair => pair.Endpoint).Distinct())}. Map each of its members with services.AddFaultResponses<{fault.First().FaultName}>(fault => …).");

        // A status is one instance, which every endpoint of the scope it was declared on carries: a group's
        // or a convention builder's endpoints, a controller's actions, or the one endpoint or action. It
        // concerns the endpoints of its scope that answer with its fault type, and is stale, or set on the
        // wrong scope, where none does. An endpoint of another scope does not count: it answers to its own.
        var stray = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<IFaultStatus>().Select(status => (Status: status, Endpoint: endpoint)))
            .GroupBy(pair => pair.Status, pair => pair.Endpoint, DeclarationComparer)
            .Where(scope => !scope.Any(endpoint => Answers(endpoint, scope.Key.FaultType)))
            .Select(scope => Stray(scope.Key, [.. scope.Distinct()]));

        var nameless = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<FaultResponsesMetadata>().Distinct()
                .SelectMany(metadata => metadata.Problems(services, endpoint.Metadata))
                .Select(problem => $"{problem}, on {Name(endpoint)}."));

        var refusals = unmapped.Concat(stray).Concat(nameless).ToList();
        if (refusals.Count == 0) return;

        throw new InvalidOperationException(string.Join(" ", refusals) + FaultRefusals.Mapping);
    };

    private static bool Answers(Endpoint endpoint, Type faultType) =>
        endpoint.Metadata.OfType<FaultResponsesMetadata>().Any(answered => answered.FaultType == faultType);

    private static string Stray(IFaultStatus status, IReadOnlyList<Endpoint> scope) =>
        scope.Count == 1
            ? $"{status.Describe()} is set on {Name(scope[0])}, which answers with no {FaultTypes.NameOf(status.FaultType)}."
            : $"{status.Describe()} is set on {scope.Count} endpoints, none of which answers with {FaultTypes.NameOf(status.FaultType)}: " +
              $"{string.Join(", ", scope.Select(Name))}.";

    private static string Name(Endpoint endpoint) => endpoint.DisplayName ?? endpoint.ToString() ?? "an endpoint";
}
