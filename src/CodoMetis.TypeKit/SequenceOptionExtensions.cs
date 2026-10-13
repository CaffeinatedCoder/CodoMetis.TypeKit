namespace CodoMetis.TypeKit;

/// <summary>
/// <c>Choose(selector)</c> over a sequence whose elements may be null: only what the selector finds
/// must be non-null, so a sequence of <c>string?</c> or <c>int?</c> is chosen from without a warning,
/// its nulls the selector's to answer. The other sequence members, <c>Choose()</c> over options among
/// them, are on <see cref="Option"/>.
/// </summary>
/// <remarks>
/// A class of its own. <see cref="Option"/>'s sequence members take a <c>notnull</c> element, since it
/// becomes an option's value, and a second block over <see cref="IEnumerable{T}"/> beside them, without
/// the constraint, regroups them in the assembly's metadata, which package validation reads as
/// <c>FirstOrNone</c> and <c>LastOrNone</c> removed from 1.5.1 (CP0002, CP0021; measured). The receiver is
/// a sequence, never an unconstrained <c>T</c> itself (decision 31).
/// </remarks>
public static class SequenceOptionExtensions
{
    /// <param name="source">The sequence, whose elements may be null.</param>
    /// <typeparam name="T">The type of the elements, nullable or not.</typeparam>
    extension<T>(IEnumerable<T> source)
    {
        /// <summary>
        /// The values <paramref name="selector"/> finds: for each element it answers with <c>Some</c>,
        /// that value, in order, and nothing for a <c>None</c>. A filter and a map in one, as F#'s
        /// <c>choose</c>, and lazy, as a LINQ operator is.
        /// </summary>
        /// <param name="selector">Called with each element as the sequence is read.</param>
        /// <typeparam name="TResult">The type of the values.</typeparam>
        /// <returns>The values found, in order.</returns>
        public IEnumerable<TResult> Choose<TResult>(Func<T, Option<TResult>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            return Option.Chosen(source, selector);
        }
    }
}
