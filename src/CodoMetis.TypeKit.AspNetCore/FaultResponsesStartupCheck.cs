using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// Refuses to start a host with an endpoint whose fault type has no mapping, a fault status or a member
/// left out whose scope has no endpoint that answers with its fault type, or one naming a member its type does
/// not have, naming each endpoint. An endpoint two of whose fault types write one name for two members is
/// a warning in the log until 3.0.0 (decision 90).
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
    private static readonly IEqualityComparer<IFaultDeclaration> DeclarationComparer = ReferenceEqualityComparer.Instance;

    private static readonly Action<ILogger, string, Exception?> FaultsAlike = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, "FaultsAlike"),
        "{Clash} The host starts, but a client cannot tell these faults apart; 3.0.0 refuses it.");

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
            .SelectMany(endpoint => endpoint.Metadata.OfType<IFaultDeclaration>().Select(status => (Status: status, Endpoint: endpoint)))
            .GroupBy(pair => pair.Status, pair => pair.Endpoint, DeclarationComparer)
            .Where(scope => !scope.Any(endpoint => Answers(endpoint, scope.Key.FaultType)))
            .Select(scope => Stray(scope.Key, [.. scope.Distinct()]));

        var nameless = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<FaultResponsesMetadata>().Distinct()
                .SelectMany(metadata => metadata.Problems(services, endpoint.Metadata))
                .Select(problem => $"{problem}, on {Name(endpoint)}."));

        // A status for a member left out is a contradiction only where it is left out on every endpoint of the
        // status's scope that answers the fault type: there the status serves nothing. Where some endpoint still
        // answers the member, the ones that leave it out narrow a group's or a controller's status, as they narrow
        // the host's mapping. Judged per endpoint, a group's 410 for NotFound stopped the host as soon as one
        // endpoint inside left NotFound out (reported 2026-10-14; decision 91).
        var scopes = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<IFaultDeclaration>().Select(status => (Status: status, Endpoint: endpoint)))
            .Where(pair => Answers(pair.Endpoint, pair.Status.FaultType))
            .GroupBy(pair => pair.Status, pair => pair.Endpoint, DeclarationComparer)
            .ToDictionary(scope => scope.Key, scope => scope.Distinct().ToList(), DeclarationComparer);
        var contradicted = endpoints.Endpoints
            .SelectMany(endpoint => endpoint.Metadata.OfType<FaultResponsesMetadata>().Distinct()
                .SelectMany(metadata => metadata.StatusesLeftOut(services, endpoint.Metadata))
                .Select(left => (left.Status, left.Member, left.Message, Endpoint: endpoint)))
            .GroupBy(left => left.Status, DeclarationComparer)
            .SelectMany(status => status.GroupBy(left => left.Member, StringComparer.Ordinal)
                .Where(member => scopes[status.Key].All(endpoint => member.Any(left => left.Endpoint == endpoint)))
                .Select(member => $"{member.First().Message}, on {string.Join(", ", member.Select(left => Name(left.Endpoint)).Distinct())}."));

        // Two fault types an endpoint answers with should not write one name: the client reads two faults
        // as one. Bare names meet wherever two types share a member's name; qualified ones where two types of
        // one nesting chain live in two namespaces. A warning, not a refusal: an endpoint answering
        // Results<…, FaultProblem<A>, FaultProblem<B>> started under 2.0.0 and answered, and refusing it
        // stopped a host that only upgraded (reported 2026-10-14); 3.0.0 refuses it (decision 90).
        var qualified = services.GetService<IOptions<FaultResponseOptions>>()?.Value.QualifiedFaultNames == true;
        var alike = endpoints.Endpoints.SelectMany(endpoint => Alike(services, endpoint, qualified)).ToList();
        if (alike.Count > 0 && services.GetService<ILoggerFactory>()?.CreateLogger("CodoMetis.TypeKit.AspNetCore.FaultResponses") is { } logger)
        {
            foreach (var clash in alike) FaultsAlike(logger, clash, null);
        }

        var refusals = unmapped.Concat(stray).Concat(nameless).Concat(contradicted).ToList();
        if (refusals.Count == 0) return;

        throw new InvalidOperationException(string.Join(" ", refusals) + FaultRefusals.Mapping);
    };

    private static IEnumerable<string> Alike(IServiceProvider services, Endpoint endpoint, bool qualified)
    {
        var answered = endpoint.Metadata.OfType<FaultResponsesMetadata>().Distinct().ToList();
        if (answered.Count < 2) return [];

        return answered.SelectMany(metadata => metadata.Names(services, endpoint.Metadata)
                                                       .Select(name => (metadata.FaultType, Written: qualified ? name.QualifiedName : name.Name,
                                                                        Described: qualified ? $"{metadata.FaultType.Namespace}.{name.QualifiedName}" : name.QualifiedName)))
                       .GroupBy(name => name.Written, StringComparer.Ordinal)
                       .Where(name => name.Select(member => member.FaultType).Distinct().Count() > 1)
                       .Select(name =>
                           $"{Name(endpoint)} answers {string.Join(" and ", name.Select(member => member.Described))} alike, as the fault \"{name.Key}\": " +
                           (qualified
                               ? "rename one type, or leave out the one the endpoint never answers with."
                               : "set FaultResponseOptions.QualifiedFaultNames, rename one, or leave out the one the endpoint never answers with."));
    }

    private static bool Answers(Endpoint endpoint, Type faultType) =>
        endpoint.Metadata.OfType<FaultResponsesMetadata>().Any(answered => answered.FaultType == faultType);

    private static string Stray(IFaultDeclaration status, IReadOnlyList<Endpoint> scope) =>
        scope.Count == 1
            ? $"{status.Describe()} is set on {Name(scope[0])}, which answers with no {FaultTypes.NameOf(status.FaultType)}."
            : $"{status.Describe()} is set on {scope.Count} endpoints, none of which answers with {FaultTypes.NameOf(status.FaultType)}: " +
              $"{string.Join(", ", scope.Select(Name))}.";

    private static string Name(Endpoint endpoint) => endpoint.DisplayName ?? endpoint.ToString() ?? "an endpoint";
}
