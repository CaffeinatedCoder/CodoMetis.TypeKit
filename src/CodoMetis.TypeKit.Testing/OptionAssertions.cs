namespace CodoMetis.TypeKit.Testing;

/// <summary>Assertions on an <see cref="Option{T}"/>.</summary>
/// <remarks>
/// <see cref="ShouldBeSome{T}(Option{T})"/> hands the value back, so the test compares it with the
/// assertions it already uses. A failure never prints the value, as <see cref="Option{T}"/> never does.
/// </remarks>
public static class OptionAssertions
{
    /// <summary>The value of an option that must hold one.</summary>
    /// <param name="option">The option under test.</param>
    /// <returns>The value.</returns>
    /// <exception cref="TypeKitAssertionException">The option is None.</exception>
    public static T ShouldBeSome<T>(this Option<T> option) where T : notnull =>
        option.TryGetValue(out var value)
            ? value
            : throw new TypeKitAssertionException($"Expected Some, but the {Outcomes.Name<Option<T>>()} is None.");

    /// <summary>Fails unless the option is None.</summary>
    /// <param name="option">The option under test.</param>
    /// <exception cref="TypeKitAssertionException">The option holds a value, which is not printed.</exception>
    public static void ShouldBeNone<T>(this Option<T> option) where T : notnull
    {
        if (option.IsSome())
            throw new TypeKitAssertionException($"Expected None, but the {Outcomes.Name<Option<T>>()} holds a value (not printed).");
    }
}
