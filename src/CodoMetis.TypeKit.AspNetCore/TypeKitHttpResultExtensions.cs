using System.Diagnostics.CodeAnalysis;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ClosedSets;
using Microsoft.AspNetCore.Http.HttpResults;

// Beside TypedResults, in a namespace every web project imports implicitly.
namespace Microsoft.AspNetCore.Http;

/// <summary>Turns a <c>Result</c> whose fault is an enum, or a <see cref="Fault{TFault}"/>, into the HTTP response an endpoint returns.</summary>
/// <remarks>
/// <para>
/// Matching a result to a response is not serializing it: the success's value is written as the
/// endpoint's body, and the <c>Result</c> itself still refuses System.Text.Json. A handler that
/// returns the <c>Result</c> instead would document a <c>Result</c>, and under Native AOT need one in
/// the host's JSON context (spikes/ResultHttp, findings 9 and 10).
/// </para>
/// <para>
/// A result whose fault is a closed set takes the same calls from
/// <see cref="TypeKitClosedSetHttpResultExtensions"/>: C# tells the two apart by their constraints, so
/// they live in two classes and resolve without type arguments.
/// </para>
/// </remarks>
public static class TypeKitHttpResultExtensions
{
    // What ASP.NET's Results<TResult1, TResult2> asks of its first type argument, carried to the
    // success type the endpoint chooses, so the trimmer keeps the members it describes the response by.
    internal const DynamicallyAccessedMemberTypes SuccessMembers = DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods;

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

    /// <summary>
    /// A success as the response <paramref name="success"/> makes of its value, such as
    /// <c>TypedResults.Created(uri, order)</c>; a fault as problem details.
    /// </summary>
    /// <remarks>The success is ASP.NET's own typed result, so the operation describes its status and body, a <c>201</c> with its value.</remarks>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <param name="success">Makes the response for the value.</param>
    /// <returns>The response.</returns>
    public static Results<TSuccess, FaultProblem<TError>> ToHttpResult<T, TError, [DynamicallyAccessedMembers(SuccessMembers)] TSuccess>(this Result<T, TError> result, Func<T, TSuccess> success)
        where T : notnull
        where TError : struct, Enum
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(success);
        return result.Match<Results<TSuccess, FaultProblem<TError>>>(value => success(value), static fault => new FaultProblem<TError>(fault));
    }

    /// <summary>A success as the response <paramref name="success"/> makes, such as <c>TypedResults.Accepted(uri)</c>; a fault as problem details.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <param name="success">Makes the response for a success.</param>
    /// <returns>The response.</returns>
    public static Results<TSuccess, FaultProblem<TError>> ToHttpResult<TError, [DynamicallyAccessedMembers(SuccessMembers)] TSuccess>(this Result<TError> result, Func<TSuccess> success)
        where TError : struct, Enum
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(success);
        return result.Match<Results<TSuccess, FaultProblem<TError>>>(() => success(), static fault => new FaultProblem<TError>(fault));
    }

    /// <summary>
    /// A success as <c>200 OK</c> with its value; a fault as problem details for its reason, with its
    /// detail as the problem's <c>detail</c>. The mapping and the document are the reason's type's.
    /// </summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <returns>The response.</returns>
    public static Results<Ok<T>, FaultProblem<TFault>> ToHttpResult<T, TFault>(this Result<T, Fault<TFault>> result)
        where T : notnull
        where TFault : notnull =>
        result.Match<Results<Ok<T>, FaultProblem<TFault>>>(
            static value => TypedResults.Ok(value),
            static fault => new FaultProblem<TFault>(fault.Reason, fault.Detail));

    /// <summary>A success as <c>204 No Content</c>; a fault as problem details for its reason, with its detail.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <returns>The response.</returns>
    public static Results<NoContent, FaultProblem<TFault>> ToHttpResult<TFault>(this Result<Fault<TFault>> result)
        where TFault : notnull =>
        result.Match<Results<NoContent, FaultProblem<TFault>>>(
            static () => TypedResults.NoContent(),
            static fault => new FaultProblem<TFault>(fault.Reason, fault.Detail));

    /// <summary>A success as the response <paramref name="success"/> makes of its value; a fault as problem details for its reason, with its detail.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <param name="success">Makes the response for the value.</param>
    /// <returns>The response.</returns>
    public static Results<TSuccess, FaultProblem<TFault>> ToHttpResult<T, TFault, [DynamicallyAccessedMembers(SuccessMembers)] TSuccess>(this Result<T, Fault<TFault>> result, Func<T, TSuccess> success)
        where T : notnull
        where TFault : notnull
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(success);
        return result.Match<Results<TSuccess, FaultProblem<TFault>>>(value => success(value), static fault => new FaultProblem<TFault>(fault.Reason, fault.Detail));
    }

    /// <summary>A success as the response <paramref name="success"/> makes; a fault as problem details for its reason, with its detail.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <param name="success">Makes the response for a success.</param>
    /// <returns>The response.</returns>
    public static Results<TSuccess, FaultProblem<TFault>> ToHttpResult<TFault, [DynamicallyAccessedMembers(SuccessMembers)] TSuccess>(this Result<Fault<TFault>> result, Func<TSuccess> success)
        where TFault : notnull
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(success);
        return result.Match<Results<TSuccess, FaultProblem<TFault>>>(() => success(), static fault => new FaultProblem<TFault>(fault.Reason, fault.Detail));
    }
}

/// <summary>Turns a <c>Result</c> whose fault is a closed set into the HTTP response an endpoint returns, as <see cref="TypeKitHttpResultExtensions"/> does for an enum.</summary>
public static class TypeKitClosedSetHttpResultExtensions
{
    private const DynamicallyAccessedMemberTypes SuccessMembers = TypeKitHttpResultExtensions.SuccessMembers;

    /// <summary>A success as <c>200 OK</c> with its value, a fault as problem details with the status the host answers it with.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <returns>The response.</returns>
    public static Results<Ok<T>, FaultProblem<TError>> ToHttpResult<T, TError>(this Result<T, TError> result)
        where T : notnull
        where TError : class, IClosedSet<TError> =>
        result.Match<Results<Ok<T>, FaultProblem<TError>>>(
            static value => TypedResults.Ok(value),
            static fault => new FaultProblem<TError>(fault));

    /// <summary>A success as <c>204 No Content</c>, a fault as problem details.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <returns>The response.</returns>
    public static Results<NoContent, FaultProblem<TError>> ToHttpResult<TError>(this Result<TError> result)
        where TError : class, IClosedSet<TError> =>
        result.Match<Results<NoContent, FaultProblem<TError>>>(
            static () => TypedResults.NoContent(),
            static fault => new FaultProblem<TError>(fault));

    /// <summary>A success as the response <paramref name="success"/> makes of its value; a fault as problem details.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <param name="success">Makes the response for the value.</param>
    /// <returns>The response.</returns>
    public static Results<TSuccess, FaultProblem<TError>> ToHttpResult<T, TError, [DynamicallyAccessedMembers(SuccessMembers)] TSuccess>(this Result<T, TError> result, Func<T, TSuccess> success)
        where T : notnull
        where TError : class, IClosedSet<TError>
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(success);
        return result.Match<Results<TSuccess, FaultProblem<TError>>>(value => success(value), static fault => new FaultProblem<TError>(fault));
    }

    /// <summary>A success as the response <paramref name="success"/> makes; a fault as problem details.</summary>
    /// <param name="result">The outcome of the operation the endpoint ran.</param>
    /// <param name="success">Makes the response for a success.</param>
    /// <returns>The response.</returns>
    public static Results<TSuccess, FaultProblem<TError>> ToHttpResult<TError, [DynamicallyAccessedMembers(SuccessMembers)] TSuccess>(this Result<TError> result, Func<TSuccess> success)
        where TError : class, IClosedSet<TError>
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(success);
        return result.Match<Results<TSuccess, FaultProblem<TError>>>(() => success(), static fault => new FaultProblem<TError>(fault));
    }
}
