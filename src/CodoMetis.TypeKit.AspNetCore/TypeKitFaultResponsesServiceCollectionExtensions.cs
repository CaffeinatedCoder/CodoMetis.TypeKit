using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Beside AddProblemDetails, in a namespace every web project imports implicitly.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Maps the faults of a <c>Result</c> to the HTTP statuses a host answers them with.</summary>
public static class TypeKitFaultResponsesServiceCollectionExtensions
{
    /// <summary>
    /// Answers every fault of <typeparamref name="TFault"/> that <c>ToHttpResult()</c> reports with the
    /// status <paramref name="statusOf"/> gives it, as RFC 9457 problem details.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every member is mapped here, once: a member the mapping throws for, or maps to a status that is
    /// not an error (400 to 599), fails this call. The statuses are what the OpenAPI document lists for
    /// an endpoint that reports the fault type.
    /// </para>
    /// <para>
    /// It also adds a startup check: a host with an endpoint whose fault type is not mapped does not
    /// start, and the exception names each such endpoint. And it calls <c>AddProblemDetails()</c>,
    /// without which a host on source-generated JSON, as under Native AOT, cannot write a problem.
    /// </para>
    /// </remarks>
    /// <typeparam name="TFault">The fault type, an enum.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <param name="statusOf">The status of each fault, typically a <c>switch</c> expression.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TFault"/> is mapped already.</exception>
    /// <exception cref="ArgumentException">A member is mapped to a status that is not an error.</exception>
    public static IServiceCollection AddFaultResponses<TFault>(this IServiceCollection services, Func<TFault, int> statusOf)
        where TFault : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(statusOf);

        // Two mappings for one fault type: whichever was registered last would answer, silently.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FaultResponses<TFault>)))
            throw new InvalidOperationException(
                $"The fault type {typeof(TFault).Name} is mapped already. Map it once, with a status for each member.");

        services.AddSingleton(FaultResponses<TFault>.Map(statusOf));
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, FaultResponsesStartupCheck>());
        services.AddProblemDetails();

        return services;
    }
}
