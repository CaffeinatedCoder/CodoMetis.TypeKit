namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// The throw behind the generated <c>FromKnownGood</c>. It lives in ordinary C#, so the generated
/// method stays a single call and the message can be tested without a generator.
/// </summary>
public static class KnownGood
{
    /// <summary>
    /// Unwraps a result whose input the call site has declared it owns, and throws if the value
    /// object refused it anyway.
    /// </summary>
    /// <remarks>
    /// The refused value never appears in the message. <paramref name="source"/> carries the
    /// caller's argument <i>expression</i> instead, so an argument that holds a secret cannot reach
    /// the message.
    /// </remarks>
    /// <param name="result">What the value object's <c>Create</c> returned.</param>
    /// <param name="source">The caller's argument expression, if the compiler supplied it.</param>
    /// <typeparam name="TValue">The value object type.</typeparam>
    /// <typeparam name="TFault">The fault type.</typeparam>
    /// <returns>The value object.</returns>
    /// <exception cref="InvalidOperationException">The value object refused the input.</exception>
    public static TValue OrThrow<TValue, TFault>(Result<TValue, TFault> result, string? source)
        where TValue : notnull
        where TFault : notnull
    {
        if (result.TryGetValue(out var value, out var fault)) return value;

        throw new InvalidOperationException(
            source is null
                ? $"{typeof(TValue).Name} refused a value the call site declared known-good ({fault})."
                : $"{typeof(TValue).Name} refused {source}, which the call site declared known-good ({fault})."
        );
    }
}
