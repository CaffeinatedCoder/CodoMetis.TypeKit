using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A fault of a <see cref="Result{T, TError}"/> as an HTTP response: RFC 9457 problem details with
/// the status the host mapped the fault to, and the fault's name as the <c>fault</c> member.
/// </summary>
/// <remarks>
/// <para>
/// Made by <c>ToHttpResult()</c>, as the second case of the endpoint's
/// <see cref="Microsoft.AspNetCore.Http.HttpResults.Results{TResult1, TResult2}"/>. The status comes
/// from <c>services.AddFaultResponses&lt;TFault&gt;(…)</c>, or from the endpoint's
/// <c>WithFaultStatus</c>, so the domain that declares the fault never references ASP.NET.
/// </para>
/// <para>
/// The endpoint's OpenAPI operation lists a problem response for every status the fault type is
/// answered with (<c>AddTypeKit()</c>), whose schema describes <c>fault</c> as an optional string,
/// and a host with an endpoint whose fault type is not mapped does not start. The problem names the fault, and a <c>type</c> and <c>title</c> where the mapping gives
/// them; its <c>detail</c> is the occurrence's (<see cref="Fault{TFault}.Detail"/>), or else a fault set
/// member's description, and nothing else of the request reaches it.
/// </para>
/// </remarks>
/// <typeparam name="TFault">The fault type, an enum or a closed set.</typeparam>
public sealed class FaultProblem<TFault> : IResult, IEndpointMetadataProvider
    where TFault : notnull
{
    internal FaultProblem(TFault fault, string? detail = null)
    {
        Fault = fault;
        Detail = detail;
    }

    /// <summary>The fault this response reports.</summary>
    public TFault Fault { get; }

    /// <summary>The sentence about this occurrence, where the result carried a <see cref="Fault{TFault}"/> with one.</summary>
    public string? Detail { get; }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var responses = httpContext.RequestServices.GetService<FaultResponses<TFault>>() ?? throw FaultResponses<TFault>.Unmapped();
        var answer = responses.Of(Fault);
        var status = httpContext.GetEndpoint()?.Metadata is { } metadata ? FaultStatuses.Override(metadata, Fault, responses.Type) ?? answer.Status : answer.Status;
        var name = httpContext.RequestServices.GetService<IOptions<FaultResponseOptions>>()?.Value.QualifiedFaultNames == true ? answer.QualifiedName : answer.Name;
        var detail = Detail ?? answer.Description;

        // Present only while this problem is written, so a CustomizeProblemDetails that reads it sees it
        // for this problem and for no other the request writes.
        var features = httpContext.Features;
        var previous = features.Get<IFaultFeature>();
        features.Set<IFaultFeature>(new FaultFeature(typeof(TFault), Fault, name, detail));
        try
        {
            await TypedResults.Problem(
                    statusCode: status,
                    type: answer.Type,
                    title: answer.Title,
                    detail: detail,
                    extensions: new Dictionary<string, object?> { [FaultResponsesOperationTransformer.FaultMember] = name })
                .ExecuteAsync(httpContext);
        }
        finally
        {
            features.Set(previous);
        }
    }

    /// <summary>Records which fault type the endpoint answers with; the statuses are read where the services are.</summary>
    /// <remarks>
    /// Not the statuses themselves: MVC builds its application model with an
    /// <see cref="EndpointBuilder"/> whose <see cref="EndpointBuilder.ApplicationServices"/> is empty
    /// (spikes/ResultHttp, finding 4), so the mapping is read by the document's operation transformer
    /// and the startup check instead.
    /// </remarks>
    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(FaultResponsesMetadata<TFault>.Instance);
    }

    private sealed record FaultFeature(Type FaultType, object Fault, string Name, string? Detail) : IFaultFeature;
}

/// <summary>
/// The fault a problem reports, on the request while <c>ToHttpResult()</c> writes it, so a host's
/// <c>CustomizeProblemDetails</c> can add what it needs, an error code, a link, without this package
/// naming any: <c>context.HttpContext.Features.Get&lt;IFaultFeature&gt;()</c>.
/// </summary>
/// <remarks>Absent for every other problem the request writes, such as an exception handler's.</remarks>
public interface IFaultFeature
{
    /// <summary>The fault type, an enum or a closed set.</summary>
    Type FaultType { get; }

    /// <summary>The fault: the enum member or the closed set's member.</summary>
    object Fault { get; }

    /// <summary>The name the problem's <c>fault</c> member carries, qualified where the host asks for it.</summary>
    string Name { get; }

    /// <summary>The problem's <c>detail</c>, or <see langword="null"/>.</summary>
    string? Detail { get; }
}

/// <summary>How <c>ToHttpResult()</c> writes a problem: <c>services.Configure&lt;FaultResponseOptions&gt;(options =&gt; …)</c>.</summary>
public sealed class FaultResponseOptions
{
    /// <summary>
    /// Whether the problem's <c>fault</c> member is qualified with its type, <c>OrderFault.NotFound</c>,
    /// so two fault types that share a member name stay apart in one client. A nested type is named
    /// after its nesting chain, <c>Billing.Fault.NotFound</c>, an enum as a closed set's
    /// <c>QualifiedName</c> is. Off by default: the bare member name, <c>NotFound</c>.
    /// </summary>
    public bool QualifiedFaultNames { get; set; }
}
