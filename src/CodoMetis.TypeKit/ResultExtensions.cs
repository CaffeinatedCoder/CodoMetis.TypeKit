namespace CodoMetis.TypeKit;

/// <summary>
/// The <c>Success</c>/<c>Error</c> markers that convert into a <see cref="Result{TError}"/> or
/// <see cref="Result{T,TError}"/>; the LINQ and sequence vocabulary for results; the zips; and the
/// continuations of a result that is still being produced (<c>Task&lt;Result&lt;…&gt;&gt;</c>).
/// </summary>
/// <remarks>
/// Everything here stops at the first error, in argument or sequence order. Nothing collects
/// errors: that is a validation applicative, which this package leaves out on purpose.
/// </remarks>
public static class Result
{
    /// <summary>The success marker for a method typed <see cref="Result{TError}"/>.</summary>
    /// <returns>A marker that converts implicitly to a successful result.</returns>
    public static Success Success() => new();

    /// <summary>The success marker for a method typed <see cref="Result{T,TError}"/>.</summary>
    /// <param name="value">The value. Never null.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A marker that converts implicitly to a successful result holding <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Success<T> Success<T>(T value) where T : notnull =>
        value is null ? throw new ArgumentNullException(nameof(value)) : new(value);

    /// <summary>The error marker for a method typed <see cref="Result{T,TError}"/>, whatever its value type, or <see cref="Result{TError}"/>.</summary>
    /// <param name="error">The error. Never null.</param>
    /// <typeparam name="T">The type of the error.</typeparam>
    /// <returns>A marker that converts implicitly to a failed result holding <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static Error<T> Error<T>(T error) where T : notnull =>
        error is null ? throw new ArgumentNullException(nameof(error)) : new(error);

    /// <param name="instance">The result.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(Result<T, TError> instance) where T : notnull where TError : notnull
    {
        /// <summary>Transforms the value on success. Enables <c>select</c> in query syntax.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>A success with the transformed value, or the error.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public Result<TResult, TError> Select<TResult>(Func<T, TResult> selector) where TResult : notnull =>
            instance.Map(selector);

        /// <summary>Keeps the value and drops the error.</summary>
        /// <returns>The value, or <c>None</c> for an error.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public Option<T> ToOption() =>
            instance.Match(Option.Some, Option.None<T>);
    }

    /// <param name="a">The first result.</param>
    /// <typeparam name="T">The type of the first value.</typeparam>
    /// <typeparam name="TError">The type of the error, shared by every result.</typeparam>
    extension<T, TError>(Result<T, TError> a) where T : notnull where TError : notnull
    {
        /// <summary>Combines two results, if both are successes.</summary>
        /// <param name="b">The second result.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>A success with the combined value, or the first error in argument order.</returns>
        /// <exception cref="InvalidOperationException">Any of the results is uninitialized, even one after an error.</exception>
        public Result<TResult, TError> Zip<T2, TResult>(Result<T2, TError> b, Func<T, T2, TResult> selector)
            where T2 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ThrowIfAnyUninitialized(a.State, b.State);

            if (!a.TryGetValue(out var v1, out var error)) return Result<TResult, TError>.Error(error);
            if (!b.TryGetValue(out var v2, out error)) return Result<TResult, TError>.Error(error);

            return Result<TResult, TError>.Success(selector(v1, v2));
        }

        /// <summary>Combines three results, if all are successes.</summary>
        /// <param name="b">The second result.</param>
        /// <param name="c">The third result.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>A success with the combined value, or the first error in argument order.</returns>
        /// <exception cref="InvalidOperationException">Any of the results is uninitialized, even one after an error.</exception>
        public Result<TResult, TError> Zip<T2, T3, TResult>(Result<T2, TError> b, Result<T3, TError> c, Func<T, T2, T3, TResult> selector)
            where T2 : notnull
            where T3 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ThrowIfAnyUninitialized(a.State, b.State, c.State);

            if (!a.TryGetValue(out var v1, out var error)) return Result<TResult, TError>.Error(error);
            if (!b.TryGetValue(out var v2, out error)) return Result<TResult, TError>.Error(error);
            if (!c.TryGetValue(out var v3, out error)) return Result<TResult, TError>.Error(error);

            return Result<TResult, TError>.Success(selector(v1, v2, v3));
        }

        /// <summary>Combines four results, if all are successes.</summary>
        /// <param name="b">The second result.</param>
        /// <param name="c">The third result.</param>
        /// <param name="d">The fourth result.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>A success with the combined value, or the first error in argument order.</returns>
        /// <exception cref="InvalidOperationException">Any of the results is uninitialized, even one after an error.</exception>
        public Result<TResult, TError> Zip<T2, T3, T4, TResult>(
            Result<T2, TError>           b,
            Result<T3, TError>           c,
            Result<T4, TError>           d,
            Func<T, T2, T3, T4, TResult> selector
        )
            where T2 : notnull
            where T3 : notnull
            where T4 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ThrowIfAnyUninitialized(a.State, b.State, c.State, d.State);

            if (!a.TryGetValue(out var v1, out var error)) return Result<TResult, TError>.Error(error);
            if (!b.TryGetValue(out var v2, out error)) return Result<TResult, TError>.Error(error);
            if (!c.TryGetValue(out var v3, out error)) return Result<TResult, TError>.Error(error);
            if (!d.TryGetValue(out var v4, out error)) return Result<TResult, TError>.Error(error);

            return Result<TResult, TError>.Success(selector(v1, v2, v3, v4));
        }

        /// <summary>Combines five results, if all are successes.</summary>
        /// <param name="b">The second result.</param>
        /// <param name="c">The third result.</param>
        /// <param name="d">The fourth result.</param>
        /// <param name="e">The fifth result.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>A success with the combined value, or the first error in argument order.</returns>
        /// <exception cref="InvalidOperationException">Any of the results is uninitialized, even one after an error.</exception>
        public Result<TResult, TError> Zip<T2, T3, T4, T5, TResult>(
            Result<T2, TError>               b,
            Result<T3, TError>               c,
            Result<T4, TError>               d,
            Result<T5, TError>               e,
            Func<T, T2, T3, T4, T5, TResult> selector
        )
            where T2 : notnull
            where T3 : notnull
            where T4 : notnull
            where T5 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ThrowIfAnyUninitialized(a.State, b.State, c.State, d.State, e.State);

            if (!a.TryGetValue(out var v1, out var error)) return Result<TResult, TError>.Error(error);
            if (!b.TryGetValue(out var v2, out error)) return Result<TResult, TError>.Error(error);
            if (!c.TryGetValue(out var v3, out error)) return Result<TResult, TError>.Error(error);
            if (!d.TryGetValue(out var v4, out error)) return Result<TResult, TError>.Error(error);
            if (!e.TryGetValue(out var v5, out error)) return Result<TResult, TError>.Error(error);

            return Result<TResult, TError>.Success(selector(v1, v2, v3, v4, v5));
        }

        /// <summary>Combines six results, if all are successes.</summary>
        /// <param name="b">The second result.</param>
        /// <param name="c">The third result.</param>
        /// <param name="d">The fourth result.</param>
        /// <param name="e">The fifth result.</param>
        /// <param name="f">The sixth result.</param>
        /// <param name="selector">Combines the values.</param>
        /// <typeparam name="T2">The type of the second value.</typeparam>
        /// <typeparam name="T3">The type of the third value.</typeparam>
        /// <typeparam name="T4">The type of the fourth value.</typeparam>
        /// <typeparam name="T5">The type of the fifth value.</typeparam>
        /// <typeparam name="T6">The type of the sixth value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>A success with the combined value, or the first error in argument order.</returns>
        /// <exception cref="InvalidOperationException">Any of the results is uninitialized, even one after an error.</exception>
        public Result<TResult, TError> Zip<T2, T3, T4, T5, T6, TResult>(
            Result<T2, TError>                   b,
            Result<T3, TError>                   c,
            Result<T4, TError>                   d,
            Result<T5, TError>                   e,
            Result<T6, TError>                   f,
            Func<T, T2, T3, T4, T5, T6, TResult> selector
        )
            where T2 : notnull
            where T3 : notnull
            where T4 : notnull
            where T5 : notnull
            where T6 : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ThrowIfAnyUninitialized(a.State, b.State, c.State, d.State, e.State, f.State);

            if (!a.TryGetValue(out var v1, out var error)) return Result<TResult, TError>.Error(error);
            if (!b.TryGetValue(out var v2, out error)) return Result<TResult, TError>.Error(error);
            if (!c.TryGetValue(out var v3, out error)) return Result<TResult, TError>.Error(error);
            if (!d.TryGetValue(out var v4, out error)) return Result<TResult, TError>.Error(error);
            if (!e.TryGetValue(out var v5, out error)) return Result<TResult, TError>.Error(error);
            if (!f.TryGetValue(out var v6, out error)) return Result<TResult, TError>.Error(error);

            return Result<TResult, TError>.Success(selector(v1, v2, v3, v4, v5, v6));
        }
    }

    /// <param name="task">A result that is still being produced.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(Task<Result<T, TError>> task) where T : notnull where TError : notnull
    {
        /// <summary>Transforms the value once the result is known, keeping the error otherwise.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>A success with the transformed value, or the error without calling <paramref name="selector"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, TResult> selector) where TResult : notnull
        {
            // Every delegate is checked before the result is known, as before a branch is picked.
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Map(selector);
        }

        /// <summary>Transforms the value once the result is known with an asynchronous function, keeping the error otherwise.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <typeparam name="TResult">The type of the transformed value.</typeparam>
        /// <returns>A success with the transformed value, or the error without calling <paramref name="selector"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).MapAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Chains an operation that may itself fail, once the result is known.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <typeparam name="TResult">The type of the chained result's value.</typeparam>
        /// <returns>The result <paramref name="selector"/> returned, or the error without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Result<TResult, TError>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Bind(selector);
        }

        /// <summary>Chains an asynchronous operation that may itself fail, once the result is known.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <typeparam name="TResult">The type of the chained result's value.</typeparam>
        /// <returns>The result <paramref name="selector"/> produced, or the error without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).BindAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Chains a command that may itself fail and produces no value, once the result is known.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <returns>The result <paramref name="selector"/> returned, or the error without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> BindAsync(Func<T, Result<TError>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Bind(selector);
        }

        /// <summary>Chains an asynchronous command that may itself fail and produces no value, once the result is known.</summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <returns>The result <paramref name="selector"/> produced, or the error without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> BindAsync(Func<T, Task<Result<TError>>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).BindAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Transforms the error once the result is known, keeping a success unchanged.</summary>
        /// <param name="selector">Called with the error on error. It must not return null.</param>
        /// <typeparam name="TNewError">The type of the transformed error.</typeparam>
        /// <returns>An error with the transformed error, or the success without calling <paramref name="selector"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<T, TNewError>> MapErrorAsync<TNewError>(Func<TError, TNewError> selector) where TNewError : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).MapError(selector);
        }

        /// <summary>Runs a side effect on the value once the result is known, on success.</summary>
        /// <param name="action">Called with the value on success.</param>
        /// <returns>The result, unchanged.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<T, TError>> TapAsync(Action<T> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return (await task.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Runs an asynchronous side effect on the value once the result is known, on success.</summary>
        /// <param name="action">Called with the value on success.</param>
        /// <returns>The result, unchanged, once <paramref name="action"/> has completed.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<T, TError>> TapAsync(Func<T, Task> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return await (await task.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }
    }

    /// <param name="task">A result that is still being produced.</param>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<TError>(Task<Result<TError>> task) where TError : notnull
    {
        /// <summary>Produces a value once the result is known, on success, keeping the error otherwise.</summary>
        /// <param name="selector">Called on success.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <returns>A success with the produced value, or the error without calling <paramref name="selector"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<TResult> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Map(selector);
        }

        /// <summary>Produces a value once the result is known with an asynchronous function, on success, keeping the error otherwise.</summary>
        /// <param name="selector">Called on success.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <returns>A success with the produced value, or the error without calling <paramref name="selector"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<Task<TResult>> selector) where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).MapAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Chains an operation that may itself fail, once the result is known.</summary>
        /// <param name="selector">Called on success.</param>
        /// <returns>The result <paramref name="selector"/> returned, or the error without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> BindAsync(Func<Result<TError>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Bind(selector);
        }

        /// <summary>Chains an asynchronous operation that may itself fail, once the result is known.</summary>
        /// <param name="selector">Called on success.</param>
        /// <returns>The result <paramref name="selector"/> produced, or the error without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> BindAsync(Func<Task<Result<TError>>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).BindAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Transforms the error once the result is known, keeping a success unchanged.</summary>
        /// <param name="selector">Called with the error on error. It must not return null.</param>
        /// <typeparam name="TNewError">The type of the transformed error.</typeparam>
        /// <returns>An error with the transformed error, or the success without calling <paramref name="selector"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TNewError>> MapErrorAsync<TNewError>(Func<TError, TNewError> selector) where TNewError : notnull
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).MapError(selector);
        }

        /// <summary>Runs a side effect once the result is known, on success.</summary>
        /// <param name="action">Called on success.</param>
        /// <returns>The result, unchanged.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> TapAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return (await task.ConfigureAwait(false)).Tap(action);
        }

        /// <summary>Runs an asynchronous side effect once the result is known, on success.</summary>
        /// <param name="action">Called on success.</param>
        /// <returns>The result, unchanged, once <paramref name="action"/> has completed.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> TapAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return await (await task.ConfigureAwait(false)).TapAsync(action).ConfigureAwait(false);
        }
    }

    /// <param name="source">The sequence.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(IEnumerable<T> source) where T : notnull where TError : notnull
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

    /// <param name="source">The sequence.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    extension<T>(IEnumerable<T> source)
    {
        /// <summary>
        /// Applies an operation that may fail to every element, in order, stopping at the first
        /// error.
        /// </summary>
        /// <param name="selector">Called with each element until one fails.</param>
        /// <typeparam name="TResult">The type of each produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>
        /// A success with every produced value, in order, or the first error. After an error,
        /// <paramref name="selector"/> is not called again and the sequence is not enumerated further.
        /// </returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        public Result<IReadOnlyList<TResult>, TError> Traverse<TResult, TError>(Func<T, Result<TResult, TError>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            return source.Select(selector).Sequence();
        }

        /// <summary>
        /// Applies a command that may fail to every element, in order, stopping at the first error.
        /// </summary>
        /// <param name="selector">Called with each element until one fails.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>
        /// A success if every command succeeded, or the first error. After an error,
        /// <paramref name="selector"/> is not called again and the sequence is not enumerated further.
        /// </returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        public Result<TError> Traverse<TError>(Func<T, Result<TError>> selector) where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            return source.Select(selector).Sequence();
        }
    }

    /// <param name="results">The results.</param>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(IEnumerable<Result<T, TError>> results) where T : notnull where TError : notnull
    {
        /// <summary>Every value, if every result is a success.</summary>
        /// <returns>
        /// A success with every value, in order, or the first error. After an error the sequence is
        /// not enumerated further.
        /// </returns>
        /// <exception cref="InvalidOperationException">A result up to the first error is uninitialized.</exception>
        public Result<IReadOnlyList<T>, TError> Sequence()
        {
            ArgumentNullException.ThrowIfNull(results);

            var values = results.TryGetNonEnumeratedCount(out var count) ? new List<T>(count) : [];

            foreach (var result in results)
            {
                if (!result.TryGetValue(out var value, out var error)) return Result<IReadOnlyList<T>, TError>.Error(error);

                values.Add(value);
            }

            return Result<IReadOnlyList<T>, TError>.Success(values);
        }
    }

    /// <param name="results">The results.</param>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<TError>(IEnumerable<Result<TError>> results) where TError : notnull
    {
        /// <summary>A success if every result is one.</summary>
        /// <returns>
        /// A success, or the first error. After an error the sequence is not enumerated further.
        /// </returns>
        /// <exception cref="InvalidOperationException">A result up to the first error is uninitialized.</exception>
        public Result<TError> Sequence()
        {
            ArgumentNullException.ThrowIfNull(results);

            foreach (var result in results)
            {
                if (result.TryGetError(out var error)) return Result<TError>.Error(error);
            }

            return Result<TError>.Success();
        }
    }

    /// <summary>
    /// A zip inspects every argument before the first error wins: an uninitialized result is a bug
    /// wherever it stands, and short-circuiting would let one after an error pass unnoticed.
    /// </summary>
    private static void ThrowIfAnyUninitialized(params ReadOnlySpan<ResultState> states)
    {
        foreach (var state in states)
        {
            if (state == ResultState.Uninitialized) ThrowHelper.ThrowUninitializedResult<bool>();
        }
    }
}
