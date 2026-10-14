using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;

// Beside AddProblemDetails, in a namespace every web project imports implicitly.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Maps the faults of a <c>Result</c>, an enum's members, to the HTTP responses a host answers them with.</summary>
/// <remarks>A closed set's members take the same calls from <see cref="TypeKitClosedSetFaultResponsesServiceCollectionExtensions"/>.</remarks>
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

        return FaultRegistration.Add(services, FaultTypes.Enum<TFault>(), fault => FaultResponse.Status(statusOf(fault)), nameof(statusOf));
    }

    /// <summary>
    /// Answers every fault of <typeparamref name="TFault"/> with the response <paramref name="responseOf"/>
    /// gives it: a status, and optionally the problem's <c>type</c> and <c>title</c>.
    /// </summary>
    /// <remarks>
    /// As the overload that takes a status: every member is mapped here, the document lists each
    /// status, and the host does not start with an endpoint whose fault type is not mapped. A bare
    /// status converts to a <see cref="FaultResponse"/>, so a mapping returns <c>404</c> for one member
    /// and <c>FaultResponse.Status(409).WithType(uri)</c> for another.
    /// </remarks>
    /// <typeparam name="TFault">The fault type, an enum.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <param name="responseOf">The response of each fault, typically a <c>switch</c> expression.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TFault"/> is mapped already.</exception>
    /// <exception cref="ArgumentException">A member is mapped to a status that is not an error.</exception>
    public static IServiceCollection AddFaultResponses<TFault>(this IServiceCollection services, Func<TFault, FaultResponse> responseOf)
        where TFault : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(responseOf);

        return FaultRegistration.Add(services, FaultTypes.Enum<TFault>(), responseOf, nameof(responseOf));
    }
}

/// <summary>Maps the faults of a <c>Result</c>, a closed set's members, to the HTTP responses a host answers them with.</summary>
/// <remarks>Separate from the enum's overloads, which take the same parameters, since C# tells the two apart by their constraints alone.</remarks>
public static class TypeKitClosedSetFaultResponsesServiceCollectionExtensions
{
    /// <summary>
    /// Answers every member of the fault set <typeparamref name="TFault"/> by its <see cref="FaultKind"/>,
    /// with no mapping written: <c>NotFound</c> 404, <c>Conflict</c> 409, <c>Invalid</c> 422,
    /// <c>Forbidden</c> 403, <c>Unavailable</c> 503, <c>PreconditionFailed</c> 412 and <c>Malformed</c> 400,
    /// and its description as the problem's <c>detail</c>.
    /// </summary>
    /// <remarks>
    /// A member whose kind is no <see cref="FaultKind"/>, a <c>default</c> one, fails this call. To change
    /// some members, map them all: <c>fault =&gt; fault == OrderFault.Gone ? 410 : FaultResponse.For(fault.Kind)</c>.
    /// </remarks>
    /// <typeparam name="TFault">The fault set.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TFault"/> is mapped already.</exception>
    /// <exception cref="ArgumentException">A member's kind is no <see cref="FaultKind"/>.</exception>
    public static IServiceCollection AddFaultResponses<TFault>(this IServiceCollection services)
        where TFault : class, IFaultSet<TFault>
    {
        ArgumentNullException.ThrowIfNull(services);

        return FaultRegistration.Add(services, FaultTypes.Set<TFault>(), static fault =>
        {
            try { return FaultResponse.For(fault.Kind); }
            catch (ArgumentOutOfRangeException refused) { throw new ArgumentException($"{fault.QualifiedName}: {refused.Message}", nameof(TFault), refused); }
        }, nameof(TFault));
    }

    /// <summary>
    /// Answers every member of the closed set <typeparamref name="TFault"/> with the response
    /// <paramref name="responseOf"/> gives it, as the enum's overload does.
    /// </summary>
    /// <remarks>Every member is mapped here; a bare status converts to a <see cref="FaultResponse"/>.</remarks>
    /// <typeparam name="TFault">The fault type, a closed set.</typeparam>
    /// <param name="services">The host's services.</param>
    /// <param name="responseOf">The response of each member.</param>
    /// <returns>The same services.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TFault"/> is mapped already.</exception>
    /// <exception cref="ArgumentException">A member is mapped to a status that is not an error.</exception>
    public static IServiceCollection AddFaultResponses<TFault>(this IServiceCollection services, Func<TFault, FaultResponse> responseOf)
        where TFault : class, IClosedSet<TFault>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(responseOf);

        return FaultRegistration.Add(services, FaultTypes.Set<TFault>(), responseOf, nameof(responseOf));
    }
}
