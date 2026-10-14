namespace CodoMetis.TypeKit.ClosedSets;

/// <summary>
/// What kind of refusal a fault is, as a client acts on it: nothing HTTP-specific, so a domain
/// declares it without referencing ASP.NET, and a host derives its status from it.
/// </summary>
/// <remarks>
/// The values start at 1: a <c>default</c> kind is no kind, and a host refuses it where the faults are
/// mapped rather than answer it as a <see cref="NotFound"/>.
/// </remarks>
public enum FaultKind
{
    /// <summary>What the request names does not exist. A host answers <c>404 Not Found</c>.</summary>
    NotFound = 1,

    /// <summary>
    /// The request conflicts with the current state: a name taken, a change that raced it, a step the
    /// state forbids. A host answers <c>409 Conflict</c>. A version the request itself stated is
    /// <see cref="PreconditionFailed"/>.
    /// </summary>
    Conflict,

    /// <summary>The request is well formed and a rule refuses it. A host answers <c>422 Unprocessable Content</c>.</summary>
    Invalid,

    /// <summary>The caller may not do this. A host answers <c>403 Forbidden</c>.</summary>
    Forbidden,

    /// <summary>Something the operation needs is unavailable, and the same request may succeed later. A host answers <c>503 Service Unavailable</c>.</summary>
    Unavailable,

    /// <summary>
    /// A condition the request stated about the current state does not hold, such as the version it
    /// read (<c>If-Match</c>): the caller reads again before deciding again. A host answers
    /// <c>412 Precondition Failed</c>.
    /// </summary>
    PreconditionFailed,

    /// <summary>
    /// The request cannot be read as what it should be: a page token, a cursor or a filter that does not
    /// parse. A host answers <c>400 Bad Request</c>. A well-formed request a rule refuses is
    /// <see cref="Invalid"/>.
    /// </summary>
    Malformed,
}

/// <summary>What a member of a fault set says of itself: the kind of refusal, and a sentence a person reads.</summary>
/// <remarks>
/// Implemented through <see cref="IFaultSet{TSelf}"/>. Non-generic, so code that holds a fault of a
/// type it does not know, such as the ASP.NET Core satellite, reads it with an interface check and no
/// reflection.
/// </remarks>
public interface IFault
{
    /// <summary>What kind of refusal this is, from which a host derives its status.</summary>
    FaultKind Kind { get; }

    /// <summary>
    /// The fault as a person reads it, a sentence a client can show, or <see langword="null"/> for a
    /// refusal that must not explain itself, a credential's or a secret's.
    /// </summary>
    /// <remarks>
    /// The same for every occurrence: what varies per occurrence travels as
    /// <see cref="Fault{TFault}.Detail"/>. Unless a set declares it, it is <see langword="null"/>.
    /// </remarks>
    string? Description => null;
}

/// <summary>
/// Declares a closed set whose members are faults, each with the <see cref="FaultKind"/> a host derives
/// its status from and an optional <see cref="IFault.Description"/>:
/// <c>public static readonly OrderFault NotFound = new(FaultKind.NotFound, "The order does not exist.");</c>
/// </summary>
/// <remarks>
/// <para>
/// A closed set in every other respect (<see cref="IClosedSet{TSelf}"/>): the generators add its name,
/// <c>All</c>, <c>Match</c>, parsing and JSON. The set declares <see cref="IFault.Kind"/>, and
/// <see cref="IFault.Description"/> where its members carry one, through its private constructor, so a
/// member's kind is read where it is declared, without reflection.
/// </para>
/// <para>
/// In CodoMetis.TypeKit.AspNetCore, <c>AddFaultResponses&lt;TFault&gt;()</c> maps every member by its
/// kind, and the description is the problem's <c>detail</c> unless the occurrence carries one of its own.
/// A smart enum names a fault; data that varies per occurrence travels as <see cref="Fault{TFault}.Detail"/>.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The set itself.</typeparam>
public interface IFaultSet<TSelf> : IClosedSet<TSelf>, IFault where TSelf : class, IFaultSet<TSelf>;
