using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>
/// A fault and a sentence about this occurrence of it: <c>Result&lt;Order, Fault&lt;OrderFault&gt;&gt;</c>,
/// so what varies per occurrence travels beside a fault that is still one of a fixed set.
/// </summary>
/// <remarks>
/// <para>
/// The reason is an enum member or a closed-set member, which a host maps to a response, keyed on the
/// reason's type, so <c>Fault&lt;OrderFault&gt;</c> is answered as <c>OrderFault</c> is. The detail is
/// the application's sentence, written into the problem's <c>detail</c> as it is: unlike a fault's name,
/// it can say anything, so whether it quotes the request is the application's to decide.
/// </para>
/// <para>
/// A reason converts to a fault without a detail, and <see cref="Fault.Of{TFault}"/> infers the type:
/// <c>Result.Error(Fault.Of(OrderFault.NotAmendable, "The order shipped on 3 May."))</c>. Not a wire type:
/// it is matched to a response, as the <see cref="Result{T, TError}"/> holding it is.
/// </para>
/// </remarks>
/// <typeparam name="TFault">The reason's type: an enum or a closed set.</typeparam>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("Use Fault.Of(reason, detail), or the reason itself.")]
public readonly record struct Fault<TFault> where TFault : notnull
{
    /// <summary>Creates the fault.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space: say <see langword="null"/> for no detail.</exception>
    public Fault(TFault reason, string? detail = null)
    {
        Reason = reason is null ? throw ThrowHelper.NullContent(nameof(reason)) : reason;
        Detail = detail is not null && string.IsNullOrWhiteSpace(detail)
            ? throw new ArgumentException("A fault's detail is a sentence, or null for none; it is never blank.", nameof(detail))
            : detail;
    }

    /// <summary>Which fault.</summary>
    public TFault Reason { get; }

    /// <summary>A sentence about this occurrence, or <see langword="null"/>.</summary>
    public string? Detail { get; }

    /// <summary>A reason as a fault without a detail.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    public static implicit operator Fault<TFault>(TFault reason) => new(reason);

    // A bare reason converts to a fault without a detail, so `fault == OrderFault.NotFound` compiled
    // and was false whenever the fault carried one. The mixed forms are refused where they are
    // written; the comparison that is meant is fault.Reason == reason.
    private const string MixedComparison =
        "A Fault<TFault> is never compared with a bare reason: the reason converts to a fault without a detail, so the comparison is false whenever the fault carries one. Compare fault.Reason with the reason.";

    /// <summary>Refused: compare <see cref="Reason"/> with the reason.</summary>
    /// <param name="fault">The fault.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator ==(Fault<TFault> fault, TFault reason) => throw new NotSupportedException();

    /// <summary>Refused: compare <see cref="Reason"/> with the reason.</summary>
    /// <param name="fault">The fault.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator !=(Fault<TFault> fault, TFault reason) => throw new NotSupportedException();

    /// <summary>Refused: compare the reason with <see cref="Reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="fault">The fault.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator ==(TFault reason, Fault<TFault> fault) => throw new NotSupportedException();

    /// <summary>Refused: compare the reason with <see cref="Reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="fault">The fault.</param>
    /// <returns>Never: the operator does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool operator !=(TFault reason, Fault<TFault> fault) => throw new NotSupportedException();

    /// <summary>Refused: compare <see cref="Reason"/> with the reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(TFault reason) => throw new NotSupportedException();
}

/// <summary>Creates a <see cref="Fault{TFault}"/>, inferring its type.</summary>
public static class Fault
{
    /// <summary>A fault with a sentence about this occurrence.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <typeparam name="TFault">The reason's type, inferred.</typeparam>
    /// <returns>The fault.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space.</exception>
    public static Fault<TFault> Of<TFault>(TFault reason, string? detail = null) where TFault : notnull => new(reason, detail);
}
