using System.Text.Json;

namespace CodoMetis.TypeKit.ValueObjects;

/// <summary>
/// The refusals behind the generated JSON converter and parsing of a validated value object. Both
/// apply <c>Create</c>, like every other way in, and a refusal becomes the exception each of them is
/// expected to throw.
/// </summary>
/// <remarks>
/// <para>
/// The refused value never appears in a message: it is input, and input can be a secret. The fault
/// does, so the caller learns which rule refused it.
/// </para>
/// <para>
/// It lives in ordinary C#, like <see cref="KnownGood"/>, so the generated code stays a single call
/// and the messages can be tested without a generator.
/// </para>
/// </remarks>
public static class Accepted
{
    /// <summary>Unwraps what <c>Create</c> accepted from JSON, or throws the exception a JSON read reports.</summary>
    /// <param name="result">What the value object's <c>Create</c> returned.</param>
    /// <typeparam name="TValue">The value object type.</typeparam>
    /// <typeparam name="TFault">The fault type.</typeparam>
    /// <returns>The value object.</returns>
    /// <exception cref="JsonException">The value object refused the value.</exception>
    public static TValue OrJsonException<TValue, TFault>(Result<TValue, TFault> result)
        where TValue : notnull
        where TFault : notnull
    {
        if (result.TryGetValue(out var value, out var fault)) return value;

        throw new JsonException($"{typeof(TValue).Name} refused the JSON value ({fault}).");
    }

    /// <summary>Unwraps what <c>Create</c> accepted from text, or throws the exception parsing reports.</summary>
    /// <param name="result">What the value object's <c>Create</c> returned.</param>
    /// <typeparam name="TValue">The value object type.</typeparam>
    /// <typeparam name="TFault">The fault type.</typeparam>
    /// <returns>The value object.</returns>
    /// <exception cref="FormatException">The value object refused the value.</exception>
    public static TValue OrFormatException<TValue, TFault>(Result<TValue, TFault> result)
        where TValue : notnull
        where TFault : notnull
    {
        if (result.TryGetValue(out var value, out var fault)) return value;

        throw new FormatException($"{typeof(TValue).Name} refused the input ({fault}).");
    }
}
