using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;

// Beside TypedResults, in a namespace every web project imports implicitly.
namespace Microsoft.AspNetCore.Http;

/// <summary>Turns a <c>Result</c> into the HTTP response an endpoint returns.</summary>
/// <remarks>
/// Matching a result to a response is not serializing it: the success's value is written as the
/// endpoint's body, and the <c>Result</c> itself still refuses System.Text.Json. A handler that
/// returns the <c>Result</c> instead would document a <c>Result</c>, and under Native AOT need one in
/// the host's JSON context (spikes/ResultHttp, findings 9 and 10).
/// </remarks>
public static class TypeKitHttpResultExtensions
{
    /// <summary>
    /// A success as <c>200 OK</c> with its value, a fault as problem details with the status
    /// <c>AddFaultResponses</c> mapped it to.
    /// </summary>
    /// <remarks>
    /// The return type is ASP.NET's own <see cref="Results{TResult1, TResult2}"/>, so the endpoint's
    /// OpenAPI operation describes the value and, with <c>AddTypeKit()</c>, a problem response for each
    /// status of the fault type, in minimal APIs and in MVC.
    /// </remarks>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <returns>The response.</returns>
    public static Results<Ok<T>, FaultProblem<TError>> ToHttpResult<T, TError>(this Result<T, TError> result)
        where T : notnull
        where TError : struct, Enum =>
        result.Match<Results<Ok<T>, FaultProblem<TError>>>(
            static value => TypedResults.Ok(value),
            static fault => new FaultProblem<TError>(fault));

    /// <summary>
    /// A success as <c>204 No Content</c>, a fault as problem details with the status
    /// <c>AddFaultResponses</c> mapped it to.
    /// </summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <returns>The response.</returns>
    public static Results<NoContent, FaultProblem<TError>> ToHttpResult<TError>(this Result<TError> result)
        where TError : struct, Enum =>
        result.Match<Results<NoContent, FaultProblem<TError>>>(
            static () => TypedResults.NoContent(),
            static fault => new FaultProblem<TError>(fault));
}
