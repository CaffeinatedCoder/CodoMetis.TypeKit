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
/// <see cref="Fault.Of{TFault}"/> makes one and infers the type, with a detail or without:
/// <c>Result.Error(Fault.Of(OrderFault.NotAmendable, "The order shipped on 3 May."))</c>. A bare reason
/// does not convert to a fault, since a fault compared with it would then be false whenever it carries
/// a detail; compare <see cref="Reason"/> instead. Not a wire type:
/// it is matched to a response, as the <see cref="Result{T, TError}"/> holding it is.
/// </para>
/// </remarks>
/// <typeparam name="TFault">The reason's type: an enum or a closed set.</typeparam>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("Use Fault.Of(reason) or Fault.Of(reason, detail).")]
public readonly record struct Fault<TFault> where TFault : notnull
{
    /// <summary>Creates the fault.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is empty or white space: say <see langword="null"/> for no detail.</exception>
    public Fault(TFault reason, string? detail = null)
    {
        Reason = reason is null ? throw new ArgumentNullException(nameof(reason), "A fault's reason is one of its type's values; it is never null.") : reason;
        Detail = detail is not null && string.IsNullOrWhiteSpace(detail)
            ? throw new ArgumentException("A fault's detail is a sentence, or null for none; it is never blank.", nameof(detail))
            : detail;
    }

    /// <summary>Which fault.</summary>
    public TFault Reason { get; }

    /// <summary>A sentence about this occurrence, or <see langword="null"/>.</summary>
    public string? Detail { get; }

    // A bare reason does not convert to a fault: with the conversion, `fault == OrderFault.NotFound`,
    // `faults.Contains(OrderFault.NotFound)` and `ShouldBeError(OrderFault.NotFound)` compiled and were
    // false whenever the fault carried a detail. Without it the operators below would be CS0019, which
    // does not name Reason, and `fault.Equals(OrderFault.NotFound)` would bind to Equals(object) and
    // compile, always false; so the mixed forms stay declared, and refused where they are written.
    private const string MixedComparison =
        "A Fault<TFault> is never compared with a bare reason: a fault carries a detail beside its reason, and the comparison that is meant is fault.Reason with the reason.";

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
