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
/// member's description, and its <c>rule</c>, where the occurrence names one, the rule that refused
/// (<see cref="Fault{TFault}.Rule"/>). Nothing else of the request reaches it.
/// </para>
/// </remarks>
/// <typeparam name="TFault">The fault type, an enum or a closed set.</typeparam>
public sealed class FaultProblem<TFault> : IResult, IEndpointMetadataProvider
    where TFault : notnull
{
    internal FaultProblem(TFault fault, string? detail = null, string? rule = null, Exception? cause = null)
    {
        Fault = fault;
        Detail = detail;
        Rule = rule;
        Cause = cause;
    }

    /// <summary>The fault this response reports.</summary>
    public TFault Fault { get; }

    /// <summary>The sentence about this occurrence, where the result carried a <see cref="Fault{TFault}"/> with one.</summary>
    public string? Detail { get; }

    /// <summary>The rule that refused, <c>EmailFault.NoAt</c>, where the result carried a <see cref="Fault{TFault}"/> naming one.</summary>
    public string? Rule { get; }

    /// <summary>
    /// The exception that caused the fault, where the result carried a <see cref="Fault{TFault}"/> with one
    /// (<see cref="Fault{TFault}.Cause"/>): for the log, and never written into the problem.
    /// </summary>
    public Exception? Cause { get; }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var responses = FaultResponses<TFault>.Of(httpContext.RequestServices) ?? throw FaultResponses<TFault>.Unmapped();
        var answer = responses.Of(Fault);
        var endpoint = httpContext.GetEndpoint();

        // The document lists no status for a member the endpoint leaves out: answering it would contradict it.
        if (endpoint is not null && FaultExclusions.Excluding(endpoint.Metadata, Fault, responses.Type) is { } exclusion)
            throw new InvalidOperationException(
                $"{endpoint.DisplayName ?? "The endpoint"} reported {answer.QualifiedName}, which {exclusion.Describe()} leaves out of its document, " +
                "so no status the document lists answers it. Remove the member from what the endpoint leaves out, or report another." + FaultRefusals.LeftOut);

        var status = endpoint is not null ? FaultStatuses.Override(endpoint.Metadata, Fault, responses.Type) ?? answer.Status : answer.Status;
        var name = httpContext.RequestServices.GetService<IOptions<FaultResponseOptions>>()?.Value.QualifiedFaultNames == true ? answer.QualifiedName : answer.Name;
        // A withheld name takes the member's description with it, which names the member as surely.
        var detail = answer.Named ? Detail ?? answer.Description : Detail;

        // Present only while this problem is written, so a CustomizeProblemDetails that reads it sees it
        // for this problem and for no other the request writes.
        var features = httpContext.Features;
        var previous = features.Get<IFaultFeature>();
        features.Set<IFaultFeature>(new FaultFeature(typeof(TFault), Fault, name, detail, Rule, !answer.Named, Cause));
        var extensions = new Dictionary<string, object?>();
        if (answer.Named) extensions[FaultResponsesOperationTransformer.FaultMember] = name;
        if (answer.Named && Rule is not null) extensions[FaultResponsesOperationTransformer.RuleMember] = Rule;
        try
        {
            await TypedResults.Problem(
                    statusCode: status,
                    type: answer.Type,
                    title: answer.Title,
                    detail: detail,
                    extensions: extensions)
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

    private sealed record FaultFeature(Type FaultType, object Fault, string Name, string? Detail, string? Rule, bool NameWithheld, Exception? Cause) : IFaultFeature;
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

    /// <summary>
    /// The name the problem's <c>fault</c> member carries, qualified where the host asks for it, or would carry
    /// where the mapping withholds it (<see cref="NameWithheld"/>).
    /// </summary>
    string Name { get; }

    /// <summary>The problem's <c>detail</c>, or <see langword="null"/>.</summary>
    string? Detail { get; }

    /// <summary>The problem's <c>rule</c>, the rule that refused (<see cref="Fault{TFault}.Rule"/>), or <see langword="null"/>.</summary>
    /// <remarks>Implemented by default as <see langword="null"/>, so an implementation of the host's own, a test's, still builds.</remarks>
    string? Rule => null;

    /// <summary>
    /// Whether the mapping withholds the fault's name from the problem (<c>FaultResponse.WithoutFaultName()</c>):
    /// the problem then has no <c>fault</c> and no <c>rule</c>, and a <c>CustomizeProblemDetails</c> that writes
    /// <see cref="Name"/> or <see cref="Rule"/> into it tells the client what the mapping keeps from it.
    /// </summary>
    /// <remarks>Implemented by default as <see langword="false"/>, so an implementation of the host's own still builds.</remarks>
    bool NameWithheld => false;

    /// <summary>
    /// The exception that caused the fault (<see cref="Fault{TFault}.Cause"/>), or <see langword="null"/>: for
    /// the host's log. The problem never carries it, and a <c>CustomizeProblemDetails</c> that copies it into
    /// one tells the client what failed underneath.
    /// </summary>
    /// <remarks>Implemented by default as <see langword="null"/>, so an implementation of the host's own, a test's, still builds.</remarks>
    Exception? Cause => null;
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
