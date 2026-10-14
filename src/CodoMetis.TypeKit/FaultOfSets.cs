using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>
/// A fault of one of two fault types: <c>Result&lt;Fault&lt;OrderFault, ConcurrencyFault&gt;&gt;</c>, so a
/// boundary answers with its own faults and with those a shared translation decides for every boundary,
/// without copying them into its own type.
/// </summary>
/// <remarks>
/// <para>
/// It holds one <see cref="Fault{TFault}"/>, reason, detail and rule intact, and converts from a fault of
/// either type: a shared translation returns <c>Fault&lt;ConcurrencyFault&gt;</c>, and the boundary's
/// result holds it as it is. C# applies one user-defined conversion where a value becomes a result, so
/// where a fault is returned as a <c>Result</c> of this type, name the type, through
/// <see cref="Of(T1, string?)"/> or a cast: <c>using Refusal = Fault&lt;OrderFault, ConcurrencyFault&gt;;</c>
/// then <c>return Refusal.Of(OrderFault.NotFound);</c>.
/// </para>
/// <para>
/// <see cref="Match{TResult}"/> takes a delegate per type, each given the <see cref="Fault{TFault}"/>.
/// In CodoMetis.TypeKit.AspNetCore, <c>ToHttpResult()</c> answers it with one <c>FaultProblem</c> per type,
/// each mapped once for the host, and the document lists what both types are mapped to. Not a wire type,
/// and a <c>default</c> one holds no fault: <see cref="Match{TResult}"/> throws for it.
/// </para>
/// </remarks>
/// <typeparam name="T1">The first fault type: an enum or a closed set.</typeparam>
/// <typeparam name="T2">The second fault type.</typeparam>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("Use Fault.Of(reason), which converts, or Fault<T1, T2>.Of(reason).")]
public readonly record struct Fault<T1, T2> where T1 : notnull where T2 : notnull
{
    private readonly Fault<T1> _first;
    private readonly Fault<T2> _second;
    private readonly byte _case;

    private Fault(Fault<T1> fault) => (_first, _case) = (Checked(fault), 1);

    private Fault(Fault<T2> fault) => (_second, _case) = (Checked(fault), 2);

    /// <summary>A fault of the first type.</summary>
    /// <param name="fault">The fault.</param>
    /// <exception cref="InvalidOperationException"><paramref name="fault"/> is a <c>default</c> one of a class reason, a closed set's, which holds none.</exception>
    public static implicit operator Fault<T1, T2>(Fault<T1> fault) => new(fault);

    /// <summary>A fault of the second type.</summary>
    /// <param name="fault">The fault.</param>
    /// <exception cref="InvalidOperationException"><paramref name="fault"/> is a <c>default</c> one of a class reason, a closed set's, which holds none.</exception>
    public static implicit operator Fault<T1, T2>(Fault<T2> fault) => new(fault);

    /// <summary>A fault of the first type, with a sentence about this occurrence or none.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2> Of(T1 reason, string? detail = null) => Fault.Of(reason, detail);

    /// <summary>A fault of the second type, with a sentence about this occurrence or none.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2> Of(T2 reason, string? detail = null) => Fault.Of(reason, detail);

    /// <summary>The fault <paramref name="fault"/> is, of the first type: what the conversion does, by name.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2> Of(Fault<T1> fault) => fault;

    /// <summary>The fault <paramref name="fault"/> is, of the second type: what the conversion does, by name.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2> Of(Fault<T2> fault) => fault;

    /// <summary>Picks the delegate for the type the fault is of.</summary>
    /// <param name="first">Called with a fault of the first type.</param>
    /// <param name="second">Called with a fault of the second type.</param>
    /// <typeparam name="TResult">What both return.</typeparam>
    /// <returns>What the picked delegate returns.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null, whichever is picked.</exception>
    /// <exception cref="InvalidOperationException">The fault is a <c>default</c> one, which holds no fault.</exception>
    public TResult Match<TResult>(Func<Fault<T1>, TResult> first, Func<Fault<T2>, TResult> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return _case switch
        {
            1 => first(_first),
            2 => second(_second),
            _ => ComposedFaults.ThrowUninitialized<TResult>(typeof(Fault<T1, T2>)),
        };
    }

    /// <summary>The held fault's reason, detail and rule, as <see cref="Fault{TFault}"/> prints them.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder) =>
        _case switch
        {
            1 => ComposedFaults.Print(builder, _first),
            2 => ComposedFaults.Print(builder, _second),
            _ => ComposedFaults.PrintUninitialized(builder),
        };

    private static Fault<T> Checked<T>(Fault<T> fault) where T : notnull =>
        fault.Reason is null ? ComposedFaults.ThrowUninitialized<Fault<T>>(typeof(Fault<T>)) : fault;

    // A bare reason converts to nothing here, as to Fault<TFault>: fault.Equals(OrderFault.NotFound) would
    // bind to Equals(object) and be false whenever it compiled. Refused where it is written.

    /// <summary>Refused: match the fault and compare its <c>Reason</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(ComposedFaults.MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(T1 reason) => throw new NotSupportedException();

    /// <summary>Refused: match the fault and compare its <c>Reason</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(ComposedFaults.MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(T2 reason) => throw new NotSupportedException();
}

/// <summary>
/// A fault of one of three fault types: <c>Result&lt;Fault&lt;OrderFault, ConcurrencyFault, RemovalFault&gt;&gt;</c>.
/// </summary>
/// <remarks>As <see cref="Fault{T1, T2}"/>, with a third type.</remarks>
/// <typeparam name="T1">The first fault type: an enum or a closed set.</typeparam>
/// <typeparam name="T2">The second fault type.</typeparam>
/// <typeparam name="T3">The third fault type.</typeparam>
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[RequireCustomInitialization("Use Fault.Of(reason), which converts, or Fault<T1, T2, T3>.Of(reason).")]
public readonly record struct Fault<T1, T2, T3> where T1 : notnull where T2 : notnull where T3 : notnull
{
    private readonly Fault<T1> _first;
    private readonly Fault<T2> _second;
    private readonly Fault<T3> _third;
    private readonly byte _case;

    private Fault(Fault<T1> fault) => (_first, _case) = (Checked(fault), 1);

    private Fault(Fault<T2> fault) => (_second, _case) = (Checked(fault), 2);

    private Fault(Fault<T3> fault) => (_third, _case) = (Checked(fault), 3);

    /// <summary>A fault of the first type.</summary>
    /// <param name="fault">The fault.</param>
    /// <exception cref="InvalidOperationException"><paramref name="fault"/> is a <c>default</c> one of a class reason, a closed set's, which holds none.</exception>
    public static implicit operator Fault<T1, T2, T3>(Fault<T1> fault) => new(fault);

    /// <summary>A fault of the second type.</summary>
    /// <param name="fault">The fault.</param>
    /// <exception cref="InvalidOperationException"><paramref name="fault"/> is a <c>default</c> one of a class reason, a closed set's, which holds none.</exception>
    public static implicit operator Fault<T1, T2, T3>(Fault<T2> fault) => new(fault);

    /// <summary>A fault of the third type.</summary>
    /// <param name="fault">The fault.</param>
    /// <exception cref="InvalidOperationException"><paramref name="fault"/> is a <c>default</c> one of a class reason, a closed set's, which holds none.</exception>
    public static implicit operator Fault<T1, T2, T3>(Fault<T3> fault) => new(fault);

    /// <summary>A fault of the first type, with a sentence about this occurrence or none.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2, T3> Of(T1 reason, string? detail = null) => Fault.Of(reason, detail);

    /// <summary>A fault of the second type, with a sentence about this occurrence or none.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2, T3> Of(T2 reason, string? detail = null) => Fault.Of(reason, detail);

    /// <summary>A fault of the third type, with a sentence about this occurrence or none.</summary>
    /// <param name="reason">Which fault. Never null.</param>
    /// <param name="detail">A sentence about this occurrence, or <see langword="null"/> for none. Never blank.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2, T3> Of(T3 reason, string? detail = null) => Fault.Of(reason, detail);

    /// <summary>The fault <paramref name="fault"/> is, of the first type: what the conversion does, by name.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2, T3> Of(Fault<T1> fault) => fault;

    /// <summary>The fault <paramref name="fault"/> is, of the second type: what the conversion does, by name.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2, T3> Of(Fault<T2> fault) => fault;

    /// <summary>The fault <paramref name="fault"/> is, of the third type: what the conversion does, by name.</summary>
    /// <param name="fault">The fault.</param>
    /// <returns>The fault.</returns>
    public static Fault<T1, T2, T3> Of(Fault<T3> fault) => fault;

    /// <summary>Picks the delegate for the type the fault is of.</summary>
    /// <param name="first">Called with a fault of the first type.</param>
    /// <param name="second">Called with a fault of the second type.</param>
    /// <param name="third">Called with a fault of the third type.</param>
    /// <typeparam name="TResult">What all three return.</typeparam>
    /// <returns>What the picked delegate returns.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null, whichever is picked.</exception>
    /// <exception cref="InvalidOperationException">The fault is a <c>default</c> one, which holds no fault.</exception>
    public TResult Match<TResult>(Func<Fault<T1>, TResult> first, Func<Fault<T2>, TResult> second, Func<Fault<T3>, TResult> third)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);

        return _case switch
        {
            1 => first(_first),
            2 => second(_second),
            3 => third(_third),
            _ => ComposedFaults.ThrowUninitialized<TResult>(typeof(Fault<T1, T2, T3>)),
        };
    }

    /// <summary>The held fault's reason, detail and rule, as <see cref="Fault{TFault}"/> prints them.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder) =>
        _case switch
        {
            1 => ComposedFaults.Print(builder, _first),
            2 => ComposedFaults.Print(builder, _second),
            3 => ComposedFaults.Print(builder, _third),
            _ => ComposedFaults.PrintUninitialized(builder),
        };

    private static Fault<T> Checked<T>(Fault<T> fault) where T : notnull =>
        fault.Reason is null ? ComposedFaults.ThrowUninitialized<Fault<T>>(typeof(Fault<T>)) : fault;

    /// <summary>Refused: match the fault and compare its <c>Reason</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(ComposedFaults.MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(T1 reason) => throw new NotSupportedException();

    /// <summary>Refused: match the fault and compare its <c>Reason</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(ComposedFaults.MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(T2 reason) => throw new NotSupportedException();

    /// <summary>Refused: match the fault and compare its <c>Reason</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Never: the method does not compile.</returns>
    [Obsolete(ComposedFaults.MixedComparison, error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Equals(T3 reason) => throw new NotSupportedException();
}

/// <summary>What <see cref="Fault{T1, T2}"/> and <see cref="Fault{T1, T2, T3}"/> share.</summary>
internal static class ComposedFaults
{
    public const string MixedComparison =
        "A fault of several types is never compared with a bare reason: it may hold a fault of another type, and one with a detail. "
      + "Match it and compare the fault's Reason, or compare it with a whole fault: composed == Fault.Of(reason).";

    [DoesNotReturn]
    public static T ThrowUninitialized<T>(Type type) =>
        throw new InvalidOperationException(
            $"This {NotWireTypeJsonConverterFactory.Name(type)} holds no fault: it is a default instance, such as an array element or an "
          + "unassigned field. Make it from a fault, Fault.Of(reason), which converts." + Refusals.UninitializedResult);

    public static bool Print<T>(System.Text.StringBuilder builder, Fault<T> fault) where T : notnull
    {
        builder.Append("Reason = ").Append(fault.Reason).Append(", Detail = ").Append(fault.Detail);
        if (fault.Rule is not null) builder.Append(", Rule = ").Append(fault.Rule);
        return true;
    }

    public static bool PrintUninitialized(System.Text.StringBuilder builder)
    {
        builder.Append("Uninitialized");
        return true;
    }
}
