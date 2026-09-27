using System.ComponentModel;
using System.Text.Json;

namespace CodoMetis.TypeKit.CompilerServices;

/// <summary>
/// The refusals behind the generated factories of a validated value object. Each applies
/// <c>Create</c>, like every other way in, and turns a refusal into the exception its entry point is
/// expected to throw: a JSON read a <see cref="JsonException"/>, parsing a
/// <see cref="FormatException"/>, <c>FromKnownGood</c> an <see cref="InvalidOperationException"/>.
/// </summary>
/// <remarks>
/// <para>
/// The refused value never appears in a message: it is input, and input can be a secret. The fault
/// does, so the caller learns which rule refused it.
/// </para>
/// <para>
/// It lives in ordinary C#, so the generated code stays a single call and the messages can be tested
/// without a generator. Nothing to call by hand.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedFactories
{
    /// <summary>Unwraps what <c>Create</c> accepted from JSON, or throws the exception a JSON read reports.</summary>
    /// <param name="result">What the value object's <c>Create</c> returned.</param>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <typeparam name="TFault">The fault type.</typeparam>
    /// <returns>The value object.</returns>
    /// <exception cref="JsonException">The value object refused the value.</exception>
    public static TValueObject OrJsonException<TValueObject, TFault>(Result<TValueObject, TFault> result)
        where TValueObject : notnull
        where TFault : notnull
    {
        if (result.TryGetValue(out var value, out var fault)) return value;

        throw new JsonException($"{typeof(TValueObject).Name} refused the JSON value ({fault}).");
    }

    /// <summary>Unwraps what <c>Create</c> accepted from text, or throws the exception parsing reports.</summary>
    /// <param name="result">What the value object's <c>Create</c> returned.</param>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <typeparam name="TFault">The fault type.</typeparam>
    /// <returns>The value object.</returns>
    /// <exception cref="FormatException">The value object refused the value.</exception>
    public static TValueObject OrFormatException<TValueObject, TFault>(Result<TValueObject, TFault> result)
        where TValueObject : notnull
        where TFault : notnull
    {
        if (result.TryGetValue(out var value, out var fault)) return value;

        throw new FormatException($"{typeof(TValueObject).Name} refused the input ({fault}).");
    }

    /// <summary>
    /// Unwraps a result whose input the call site has declared it owns, and throws if the value
    /// object refused it anyway: the generated <c>FromKnownGood</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="source"/> carries the caller's argument <i>expression</i> in place of the
    /// value, so an argument that holds a secret cannot reach the message.
    /// </remarks>
    /// <param name="result">What the value object's <c>Create</c> returned.</param>
    /// <param name="source">The caller's argument expression, if the compiler supplied it.</param>
    /// <typeparam name="TValueObject">The value object type.</typeparam>
    /// <typeparam name="TFault">The fault type.</typeparam>
    /// <returns>The value object.</returns>
    /// <exception cref="InvalidOperationException">The value object refused the input.</exception>
    public static TValueObject OrInvalidOperationException<TValueObject, TFault>(Result<TValueObject, TFault> result, string? source)
        where TValueObject : notnull
        where TFault : notnull
    {
        if (result.TryGetValue(out var value, out var fault)) return value;

        throw new InvalidOperationException(
            source is null
                ? $"{typeof(TValueObject).Name} refused a value the call site declared known-good ({fault})."
                : $"{typeof(TValueObject).Name} refused {source}, which the call site declared known-good ({fault})."
        );
    }
}
