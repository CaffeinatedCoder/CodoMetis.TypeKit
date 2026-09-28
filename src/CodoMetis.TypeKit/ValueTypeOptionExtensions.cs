namespace CodoMetis.TypeKit;

/// <summary>
/// <c>OrNull()</c> for an <see cref="Option{T}"/> of a value type, which unwraps an
/// <c>Option&lt;int&gt;</c> to an <c>int?</c>. The one for a reference type is on <see cref="Option"/>.
/// </summary>
/// <remarks>
/// A class of its own: both lower to <c>OrNull(in Option&lt;T&gt;)</c> and differ only in their
/// return type and constraint, which one class cannot declare twice (CS0111). The constraint picks
/// between them, so <c>option.OrNull()</c> reads the same for either kind, and inside generic code
/// constrained to <c>struct</c> or <c>class</c> too.
/// </remarks>
public static class ValueTypeOptionExtensions
{
    /// <param name="option">The option.</param>
    /// <typeparam name="T">The value type the option holds.</typeparam>
    extension<T>(in Option<T> option) where T : struct
    {
        /// <summary>Unwraps the value into a nullable, <see langword="null"/> for <c>None</c>: the inverse of <c>ToOption()</c>.</summary>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public T? OrNull() => option.TryGetValue(out var value) ? value : null;
    }
}
