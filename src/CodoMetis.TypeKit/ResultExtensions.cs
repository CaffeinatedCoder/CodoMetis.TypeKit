namespace CodoMetis.TypeKit;

/// <summary>
/// The <c>Ok</c>/<c>Error</c> markers that convert into a <see cref="Result{TError}"/> or
/// <see cref="Result{T,TError}"/>, and the LINQ and sequence vocabulary for results.
/// </summary>
public static class Result
{
    /// <summary>The success marker for a method typed <see cref="Result{TError}"/>.</summary>
    /// <returns>A marker that converts implicitly to a successful result.</returns>
    public static Success Ok() => new();

    /// <summary>The success marker for a method typed <see cref="Result{T,TError}"/>.</summary>
    /// <param name="value">The value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A marker that converts implicitly to a successful result holding <paramref name="value"/>.</returns>
    public static Success<T> Ok<T>(T value) => new(value);

    /// <summary>The error marker for a method typed <see cref="Result{T,TError}"/>, whatever its value type, or <see cref="Result{TError}"/>.</summary>
    /// <param name="error">The error.</param>
    /// <typeparam name="T">The type of the error.</typeparam>
    /// <returns>A marker that converts implicitly to a failed result holding <paramref name="error"/>.</returns>
    public static Error<T> Error<T>(T error) => new(error);

    /// <param name="instance">The result.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(Result<T, TError> instance) where T : notnull where TError : notnull
    {
        /// <summary>Transforms the value on success. Enables <c>select</c> in query syntax.</summary>
        /// <param name="fn">Called with the value on success.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>A success with the transformed value, or the error.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public Result<TResult, TError> Select<TResult>(Func<T, TResult> fn) =>
            instance.Map(fn);

        /// <summary>Keeps the value and drops the error.</summary>
        /// <returns>The value, or <c>None</c> for an error.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public Option<T> ToOption() =>
            instance.Match(Option.Some, Option.None<T>);
    }

    /// <param name="source">The sequence.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(IEnumerable<T> source) where T : notnull
    {
        /// <summary>The first element that matches, or the given error.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <param name="error">The error if nothing matches.</param>
        /// <returns>The first matching element, or <paramref name="error"/>.</returns>
        public Result<T, TError> FirstOrError(Func<T, bool> predicate, TError error) =>
            source.FirstOrNone(predicate)
                  .Match(Result<T, TError>.Success, () => Result<T, TError>.Error(error));

        /// <summary>The last element that matches, or the given error.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <param name="error">The error if nothing matches.</param>
        /// <returns>The last matching element, or <paramref name="error"/>.</returns>
        public Result<T, TError> LastOrError(Func<T, bool> predicate, TError error) =>
            source.LastOrNone(predicate)
                  .Match(Result<T, TError>.Success, () => Result<T, TError>.Error(error));
    }
}
