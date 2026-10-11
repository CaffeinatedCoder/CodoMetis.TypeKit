using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// A fault of a <see cref="Result{T, TError}"/> as an HTTP response: RFC 9457 problem details with
/// the status the host mapped the fault to, and the fault's name as the <c>fault</c> member.
/// </summary>
/// <remarks>
/// <para>
/// Made by <c>ToHttpResult()</c>, as the second case of the endpoint's
/// <see cref="Microsoft.AspNetCore.Http.HttpResults.Results{TResult1, TResult2}"/>. The status comes
/// from <c>services.AddFaultResponses&lt;TFault&gt;(…)</c>, so the domain that declares the fault
/// never references ASP.NET.
/// </para>
/// <para>
/// The endpoint's OpenAPI operation lists a problem response for every status the fault type is
/// mapped to (<c>AddTypeKit()</c>), and a host with an endpoint whose fault type is not mapped does
/// not start. The problem names the fault, never anything of the input.
/// </para>
/// </remarks>
/// <typeparam name="TFault">The fault type, an enum.</typeparam>
public sealed class FaultProblem<TFault> : IResult, IEndpointMetadataProvider
    where TFault : struct, Enum
{
    internal FaultProblem(TFault fault) => Fault = fault;

    /// <summary>The fault this response reports.</summary>
    public TFault Fault { get; }

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var responses = httpContext.RequestServices.GetService<FaultResponses<TFault>>() ?? throw FaultResponses<TFault>.Unmapped();

        return TypedResults.Problem(
                statusCode: responses.StatusOf(Fault),
                extensions: new Dictionary<string, object?> { ["fault"] = Fault.ToString() })
            .ExecuteAsync(httpContext);
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
}
