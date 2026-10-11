using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace ValidationFaults;

/// <summary>
/// The prototype seam for minimal APIs: with ThrowOnBadRequest, every bad request reaches the exception
/// handler. A refused value object becomes a validation problem keyed by its JSON path; every other bad
/// request keeps its own status, so turning ThrowOnBadRequest on changes nothing else.
/// </summary>
public sealed class BadRequestBridge(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad) return false;

        httpContext.Response.StatusCode = bad.StatusCode;
        var details = bad.InnerException is JsonException { Path: { } path } json && json.Data[ProbeConverter.Marker] is string fault
            ? new HttpValidationProblemDetails(new Dictionary<string, string[]> { [path.StartsWith("$.") ? path[2..] : path] = [fault] }) { Status = bad.StatusCode }
            : new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = bad.StatusCode };

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = details, Exception = exception });
    }
}
