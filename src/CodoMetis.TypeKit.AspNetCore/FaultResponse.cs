using CodoMetis.TypeKit.ClosedSets;

namespace CodoMetis.TypeKit.AspNetCore;

/// <summary>
/// How a host answers one fault: its status, and optionally the problem's <c>type</c> and <c>title</c>.
/// </summary>
/// <remarks>
/// <para>
/// Returned for each member by the mapping given to <c>AddFaultResponses</c>:
/// <c>fault => FaultResponse.Status(409).WithType(new Uri("https://example.com/problems/name-taken"))</c>.
/// A bare status converts, so <c>fault => 404</c> is a response too. RFC 9457 makes <c>type</c> the
/// problem's primary identifier, which a client matches on, and <c>title</c> its summary; both name
/// the problem type and nothing of the request. Without them, ASP.NET writes the status's own.
/// </para>
/// <para>
/// The status is checked where the faults are mapped, with the member it belongs to named: it must
/// be an error status, 400 to 599.
/// </para>
/// </remarks>
public sealed class FaultResponse
{
    private FaultResponse(int statusCode, Uri? type, string? title)
    {
        StatusCode = statusCode;
        Type = type;
        Title = title;
    }

    /// <summary>The HTTP status.</summary>
    public int StatusCode { get; }

    /// <summary>The problem's <c>type</c>, or <see langword="null"/> for the status's own.</summary>
    public Uri? Type { get; }

    /// <summary>The problem's <c>title</c>, or <see langword="null"/> for the status's own.</summary>
    public string? Title { get; }

    /// <summary>A response with <paramref name="statusCode"/>, and the status's own <c>type</c> and <c>title</c>.</summary>
    /// <param name="statusCode">The HTTP status, 400 to 599, checked where the faults are mapped.</param>
    /// <returns>The response.</returns>
    public static FaultResponse Status(int statusCode) => new(statusCode, null, null);

    /// <summary>
    /// The response a <see cref="FaultKind"/> is answered with when no mapping says otherwise:
    /// <c>NotFound</c> 404, <c>Conflict</c> 409, <c>Invalid</c> 422, <c>Forbidden</c> 403 and
    /// <c>Unavailable</c> 503.
    /// </summary>
    /// <remarks>
    /// For a mapping that changes some members and keeps the rest:
    /// <c>fault => fault == OrderFault.Gone ? 410 : FaultResponse.For(fault.Kind)</c>.
    /// </remarks>
    /// <param name="kind">The kind.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is no member of <see cref="FaultKind"/>, as a <c>default</c> one is not.</exception>
    public static FaultResponse For(FaultKind kind) => Status(kind switch
    {
        FaultKind.NotFound    => 404,
        FaultKind.Conflict    => 409,
        FaultKind.Invalid     => 422,
        FaultKind.Forbidden   => 403,
        FaultKind.Unavailable => 503,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"{(int)kind} is no FaultKind: a fault set's member declares one of its members, and a default kind is none." + FaultRefusals.Mapping),
    });

    /// <summary>The same response with the problem's <c>type</c>, a URI a client matches on.</summary>
    /// <param name="type">The problem type's URI.</param>
    /// <returns>A new response.</returns>
    public FaultResponse WithType(Uri type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new(StatusCode, type, Title);
    }

    /// <summary>The same response with the problem's <c>title</c>, a summary of the problem type a person reads.</summary>
    /// <param name="title">The title. Never blank.</param>
    /// <returns>A new response.</returns>
    public FaultResponse WithTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new(StatusCode, Type, title);
    }

    /// <summary>A bare status as a response, so a mapping can return <c>404</c> where it needs nothing else.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    public static implicit operator FaultResponse(int statusCode) => Status(statusCode);
}
