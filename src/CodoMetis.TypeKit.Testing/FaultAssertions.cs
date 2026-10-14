namespace CodoMetis.TypeKit.Testing;

/// <summary>Assertions on a fault of several types, <see cref="Fault{T1, T2}"/> and <see cref="Fault{T1, T2, T3}"/>.</summary>
/// <remarks>
/// A composed fault has no public content: <c>ShouldHold(reason)</c> fails unless it holds a fault with
/// that reason, and hands that <see cref="Fault{TFault}"/> back, so the test reads its <c>Detail</c> and
/// <c>Rule</c>. The reason's type picks the overload, so a reason of no type the fault composes does not
/// compile.
/// </remarks>
public static class FaultAssertions
{
    /// <summary>The fault a composed fault must hold, of its first type.</summary>
    /// <param name="fault">The fault under test.</param>
    /// <param name="reason">The reason the held fault must have.</param>
    /// <returns>The held fault, its detail and rule intact.</returns>
    /// <exception cref="TypeKitAssertionException">The fault is another, of either type, or uninitialized.</exception>
    public static Fault<T1> ShouldHold<T1, T2>(this Fault<T1, T2> fault, T1 reason) where T1 : notnull where T2 : notnull =>
        HeldFaults.Expect<Fault<T1, T2>, T1>(reason, Uninitialized(fault), () => fault.Match<object>(held => held, held => held));

    /// <summary>The fault a composed fault must hold, of its second type.</summary>
    /// <param name="fault">The fault under test.</param>
    /// <param name="reason">The reason the held fault must have.</param>
    /// <returns>The held fault, its detail and rule intact.</returns>
    /// <exception cref="TypeKitAssertionException">The fault is another, of either type, or uninitialized.</exception>
    public static Fault<T2> ShouldHold<T1, T2>(this Fault<T1, T2> fault, T2 reason) where T1 : notnull where T2 : notnull =>
        HeldFaults.Expect<Fault<T1, T2>, T2>(reason, Uninitialized(fault), () => fault.Match<object>(held => held, held => held));

    /// <summary>The fault a composed fault must hold, of its first type.</summary>
    /// <param name="fault">The fault under test.</param>
    /// <param name="reason">The reason the held fault must have.</param>
    /// <returns>The held fault, its detail and rule intact.</returns>
    /// <exception cref="TypeKitAssertionException">The fault is another, of any of the types, or uninitialized.</exception>
    public static Fault<T1> ShouldHold<T1, T2, T3>(this Fault<T1, T2, T3> fault, T1 reason) where T1 : notnull where T2 : notnull where T3 : notnull =>
        HeldFaults.Expect<Fault<T1, T2, T3>, T1>(reason, Uninitialized(fault), () => fault.Match<object>(held => held, held => held, held => held));

    /// <summary>The fault a composed fault must hold, of its second type.</summary>
    /// <param name="fault">The fault under test.</param>
    /// <param name="reason">The reason the held fault must have.</param>
    /// <returns>The held fault, its detail and rule intact.</returns>
    /// <exception cref="TypeKitAssertionException">The fault is another, of any of the types, or uninitialized.</exception>
    public static Fault<T2> ShouldHold<T1, T2, T3>(this Fault<T1, T2, T3> fault, T2 reason) where T1 : notnull where T2 : notnull where T3 : notnull =>
        HeldFaults.Expect<Fault<T1, T2, T3>, T2>(reason, Uninitialized(fault), () => fault.Match<object>(held => held, held => held, held => held));

    /// <summary>The fault a composed fault must hold, of its third type.</summary>
    /// <param name="fault">The fault under test.</param>
    /// <param name="reason">The reason the held fault must have.</param>
    /// <returns>The held fault, its detail and rule intact.</returns>
    /// <exception cref="TypeKitAssertionException">The fault is another, of any of the types, or uninitialized.</exception>
    public static Fault<T3> ShouldHold<T1, T2, T3>(this Fault<T1, T2, T3> fault, T3 reason) where T1 : notnull where T2 : notnull where T3 : notnull =>
        HeldFaults.Expect<Fault<T1, T2, T3>, T3>(reason, Uninitialized(fault), () => fault.Match<object>(held => held, held => held, held => held));

    private static bool Uninitialized<TComposed>(TComposed fault) where TComposed : struct =>
        EqualityComparer<TComposed>.Default.Equals(fault, default);
}

/// <summary>What the composed faults' assertions share.</summary>
internal static class HeldFaults
{
    /// <param name="reason">The reason expected.</param>
    /// <param name="uninitialized">Whether the composed fault is a <c>default</c> one, which holds none.</param>
    /// <param name="read">The held fault, boxed: the composed fault's types are told apart only at run time.</param>
    public static Fault<TFault> Expect<TComposed, TFault>(TFault reason, bool uninitialized, Func<object> read) where TFault : notnull
    {
        if (uninitialized)
            throw new TypeKitAssertionException(
                $"Expected the fault {reason}, but the {Outcomes.Name<TComposed>()} is uninitialized: a default instance, which holds no fault.");

        var held = read();
        return held is Fault<TFault> asked && EqualityComparer<TFault>.Default.Equals(asked.Reason, reason)
            ? asked
            : throw new TypeKitAssertionException($"Expected the fault {reason}, but the {Outcomes.Name<TComposed>()} is the fault {held}.");
    }
}
