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
        value is null ? throw ThrowHelper.NullContent(nameof(value)) : new(value);

    /// <summary>The error marker for a method typed <see cref="Result{T,TError}"/>, whatever its value type, or <see cref="Result{TError}"/>.</summary>
    /// <param name="error">The error. Never null.</param>
    /// <typeparam name="T">The type of the error.</typeparam>
    /// <returns>A marker that converts implicitly to a failed result holding <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static Error<T> Error<T>(T error) where T : notnull =>
        error is null ? throw ThrowHelper.NullContent(nameof(error)) : new(error);

    /// <summary>
    /// Runs <paramref name="action"/> and returns its value as a success, or the exception it threw
    /// as the error. The one place an exception becomes data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <see cref="OperationCanceledException"/> passes through: a cancellation is not an outcome,
    /// and caught it would read as a failure and <c>Recover</c> would run on it. One whose
    /// <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/> is the error: that is
    /// how <see cref="System.Net.Http.HttpClient"/> reports its <c>Timeout</c> elapsing, a failure of
    /// the call and not the caller's cancellation. A delegate that returns a <see cref="Task"/> is
    /// refused at compile time, since the task's exception is thrown after this call returns;
    /// <see cref="CatchAsync{T}(Func{Task{T}})"/> waits for it.
    /// </para>
    /// <para>
    /// A delegate that returns null throws <see cref="ArgumentNullException"/>, as <see cref="Success{T}(T)"/>
    /// does: a success over null is a defect of the delegate, not an outcome, and is not caught here.
    /// </para>
    /// </remarks>
    /// <param name="action">The call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or returned null.</exception>
    public static Result<T, Exception> Catch<T>(Func<T> action) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);

        T value;
        try
        {
            value = action();
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<T, Exception>.Error(exception);
        }

        return Result<T, Exception>.Success(value);
    }

    /// <summary>
    /// Runs <paramref name="action"/> and returns a success, or the exception it threw as the error.
    /// </summary>
    /// <remarks>
    /// An <see cref="OperationCanceledException"/> passes through, and a delegate that returns a
    /// <see cref="Task"/> is refused at compile time: see <see cref="Catch{T}(Func{T})"/>.
    /// </remarks>
    /// <param name="action">The call whose outcome is wanted.</param>
    /// <returns>A success, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public static Result<Exception> Catch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<Exception>.Error(exception);
        }

        return Result<Exception>.Success();
    }

    /// <summary>
    /// Runs <paramref name="action"/> and returns its value as a success, or the
    /// <typeparamref name="TException"/> it threw as the error. Any other exception propagates.
    /// </summary>
    /// <remarks>
    /// Both type arguments are spelled, <c>Result.Catch&lt;int, FormatException&gt;(() =&gt; int.Parse(s))</c>,
    /// since C# infers all of a method's type arguments or none. The error type then says what was
    /// expected to fail. See <see cref="Catch{T}(Func{T})"/> for cancellation, tasks and null.
    /// </remarks>
    /// <param name="action">The call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TException">The exception that is an outcome; any other propagates.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or returned null.</exception>
    public static Result<T, TException> Catch<T, TException>(Func<T> action)
        where T : notnull
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        T value;
        try
        {
            value = action();
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<T, TException>.Error(exception);
        }

        return Result<T, TException>.Success(value);
    }

    /// <summary>
    /// Runs <paramref name="action"/> and returns a success, or the <typeparamref name="TException"/>
    /// it threw as the error. Any other exception propagates.
    /// </summary>
    /// <remarks>
    /// A lambda with an expression body converts to an <see cref="Action"/> and its value is
    /// discarded, so <c>Result.Catch&lt;FormatException&gt;(() =&gt; int.Parse(s))</c> is this overload
    /// and answers a <see cref="Result{TError}"/> without the number; the valued form spells both
    /// type arguments. See <see cref="Catch{T}(Func{T})"/> for cancellation and tasks.
    /// </remarks>
    /// <param name="action">The call whose outcome is wanted.</param>
    /// <typeparam name="TException">The exception that is an outcome; any other propagates.</typeparam>
    /// <returns>A success, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public static Result<TException> Catch<TException>(Action action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<TException>.Error(exception);
        }

        return Result<TException>.Success();
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is a cancellation, which every <c>Catch</c> lets through: an
    /// <see cref="OperationCanceledException"/> that is no timeout. <see cref="System.Net.Http.HttpClient"/>
    /// throws a <see cref="TaskCanceledException"/> over a <see cref="TimeoutException"/> when its
    /// <c>Timeout</c> elapses, which is the call failing, so that one is caught.
    /// </summary>
    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException { InnerException: not TimeoutException };

    /// <summary>Refused: the task's exception is thrown after <c>Catch</c> has returned. Use <see cref="CatchAsync{T}(Func{Task{T}})"/>.</summary>
    /// <remarks>
    /// More specific than <see cref="Catch{T}(Func{T})"/> for a delegate returning a task, so the
    /// compiler picks it and reports the error. A lambda that only throws has no return type and
    /// lands here, or is ambiguous with a type argument; <c>int () =&gt; throw …</c> states it.
    /// </remarks>
    /// <param name="action">A delegate returning a task.</param>
    /// <typeparam name="T">The type of the task's value.</typeparam>
    /// <returns>Never: the overload does not compile.</returns>
    [Obsolete("The delegate returns a Task, whose exception is thrown after Catch has returned. Use CatchAsync.", error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static Result<Task<T>, Exception> Catch<T>(Func<Task<T>> action) => throw new NotSupportedException();

    /// <summary>Refused: the task's exception is thrown after <c>Catch</c> has returned. Use <see cref="CatchAsync(Func{Task})"/>.</summary>
    /// <param name="action">A delegate returning a task.</param>
    /// <returns>Never: the overload does not compile.</returns>
    [Obsolete("The delegate returns a Task, whose exception is thrown after Catch has returned. Use CatchAsync.", error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static Result<Exception> Catch(Func<Task> action) => throw new NotSupportedException();

    /// <summary>
    /// Awaits <paramref name="action"/> and returns its value as a success, or the exception the
    /// task threw as the error, whether before or after its first <c>await</c>.
    /// </summary>
    /// <remarks>
    /// An <see cref="OperationCanceledException"/> passes through, so a cancelled token surfaces as
    /// cancellation; a timeout, which <see cref="System.Net.Http.HttpClient"/> throws as a cancellation
    /// over a <see cref="TimeoutException"/>, is the error. A delegate returning a <c>ValueTask</c>
    /// needs <c>.AsTask()</c> or an <c>async</c> lambda: a <c>ValueTask</c> overload beside this one is
    /// ambiguous for every <c>async</c> lambda.
    /// See <see cref="Catch{T}(Func{T})"/> for null.
    /// </remarks>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or its task produced null.</exception>
    public static async Task<Result<T, Exception>> CatchAsync<T>(Func<Task<T>> action) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);

        T value;
        try
        {
            value = await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<T, Exception>.Error(exception);
        }

        return Result<T, Exception>.Success(value);
    }

    /// <summary>
    /// Awaits <paramref name="action"/> and returns a success, or the exception the task threw as
    /// the error. See <see cref="CatchAsync{T}(Func{Task{T}})"/>.
    /// </summary>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <returns>A success, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public static async Task<Result<Exception>> CatchAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<Exception>.Error(exception);
        }

        return Result<Exception>.Success();
    }

    /// <summary>
    /// Awaits <paramref name="action"/> and returns its value as a success, or the
    /// <typeparamref name="TException"/> the task threw as the error. Any other exception propagates.
    /// See <see cref="CatchAsync{T}(Func{Task{T}})"/> and <see cref="Catch{T, TException}(Func{T})"/>.
    /// </summary>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TException">The exception that is an outcome; any other propagates.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or its task produced null.</exception>
    public static async Task<Result<T, TException>> CatchAsync<T, TException>(Func<Task<T>> action)
        where T : notnull
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        T value;
        try
        {
            value = await action().ConfigureAwait(false);
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<T, TException>.Error(exception);
        }

        return Result<T, TException>.Success(value);
    }

    /// <summary>
    /// Awaits <paramref name="action"/> and returns a success, or the <typeparamref name="TException"/>
    /// the task threw as the error. Any other exception propagates. See <see cref="CatchAsync(Func{Task})"/>.
    /// </summary>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="TException">The exception that is an outcome; any other propagates.</typeparam>
    /// <returns>A success, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public static async Task<Result<TException>> CatchAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<TException>.Error(exception);
        }

        return Result<TException>.Success();
    }

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

        /// <summary>
        /// Chains an operation that may itself fail, and combines both values. Enables a second
        /// <c>from</c> in query syntax, where the later steps can use every earlier value.
        /// </summary>
        /// <param name="selector">Called with the value on success.</param>
        /// <param name="resultSelector">Called with both values if the chained operation succeeds.</param>
        /// <typeparam name="TNext">The type of the chained result's value.</typeparam>
        /// <typeparam name="TResult">The type of the combined value.</typeparam>
        /// <returns>A success with the combined value, or the first error.</returns>
        /// <exception cref="InvalidOperationException">The result, or the one <paramref name="selector"/> returned, is uninitialized.</exception>
        public Result<TResult, TError> SelectMany<TNext, TResult>(Func<T, Result<TNext, TError>> selector, Func<T, TNext, TResult> resultSelector)
            where TNext : notnull
            where TResult : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(resultSelector);

            return instance.Bind(value => selector(value).Map(next => resultSelector(value, next)));
        }

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

        /// <summary>Gives an error a second chance once the result is known. A success is kept unchanged.</summary>
        /// <param name="selector">Called with the error on error.</param>
        /// <returns>The result <paramref name="selector"/> returned, or the success without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<T, TError>> RecoverAsync(Func<TError, Result<T, TError>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Recover(selector);
        }

        /// <summary>Gives an error a second chance with an asynchronous function once the result is known.</summary>
        /// <param name="selector">Called with the error on error.</param>
        /// <returns>The result <paramref name="selector"/> produced, or the success without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<T, TError>> RecoverAsync(Func<TError, Task<Result<T, TError>>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).RecoverAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Keeps a success once the result is known only if its value satisfies <paramref name="predicate"/>.</summary>
        /// <param name="predicate">Called with the value on success.</param>
        /// <param name="error">The error for a value that fails <paramref name="predicate"/>. Never null.</param>
        /// <returns>The result if it is an error or its value passes, otherwise an error with <paramref name="error"/>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        public async Task<Result<T, TError>> EnsureAsync(Func<T, bool> predicate, TError error)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(predicate);
            if (error is null) throw ThrowHelper.NullContent(nameof(error));

            return (await task.ConfigureAwait(false)).Ensure(predicate, error);
        }

        /// <summary>Runs a side effect on the error once the result is known, on error.</summary>
        /// <param name="action">Called with the error on error.</param>
        /// <returns>The result, unchanged.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<T, TError>> TapErrorAsync(Action<TError> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return (await task.ConfigureAwait(false)).TapError(action);
        }

        /// <summary>Runs an asynchronous side effect on the error once the result is known, on error.</summary>
        /// <param name="action">Called with the error on error.</param>
        /// <returns>The result, unchanged, once <paramref name="action"/> has completed.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        /// <remarks>
        /// An <c>async</c> lambda binds here rather than to the <c>Action&lt;TError&gt;</c> overload, so
        /// it is awaited, and what it throws reaches the caller, instead of running as <c>async void</c>.
        /// </remarks>
        public async Task<Result<T, TError>> TapErrorAsync(Func<TError, Task> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return await (await task.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);
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

        /// <summary>Gives an error a second chance once the result is known. A success is kept unchanged.</summary>
        /// <param name="selector">Called with the error on error.</param>
        /// <returns>The result <paramref name="selector"/> returned, or the success without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> RecoverAsync(Func<TError, Result<TError>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return (await task.ConfigureAwait(false)).Recover(selector);
        }

        /// <summary>Gives an error a second chance with an asynchronous function once the result is known.</summary>
        /// <param name="selector">Called with the error on error.</param>
        /// <returns>The result <paramref name="selector"/> produced, or the success without calling it.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> RecoverAsync(Func<TError, Task<Result<TError>>> selector)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(selector);

            return await (await task.ConfigureAwait(false)).RecoverAsync(selector).ConfigureAwait(false);
        }

        /// <summary>Runs a side effect on the error once the result is known, on error.</summary>
        /// <param name="action">Called with the error on error.</param>
        /// <returns>The result, unchanged.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public async Task<Result<TError>> TapErrorAsync(Action<TError> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return (await task.ConfigureAwait(false)).TapError(action);
        }

        /// <summary>Runs an asynchronous side effect on the error once the result is known, on error.</summary>
        /// <param name="action">Called with the error on error.</param>
        /// <returns>The result, unchanged, once <paramref name="action"/> has completed.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        /// <remarks>
        /// An <c>async</c> lambda binds here rather than to the <c>Action&lt;TError&gt;</c> overload, so
        /// it is awaited, and what it throws reaches the caller, instead of running as <c>async void</c>.
        /// </remarks>
        public async Task<Result<TError>> TapErrorAsync(Func<TError, Task> action)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(action);

            return await (await task.ConfigureAwait(false)).TapErrorAsync(action).ConfigureAwait(false);
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
        /// <param name="error">The error if nothing matches. Never null.</param>
        /// <returns>The first matching element, or <paramref name="error"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        public Result<T, TError> FirstOrError(Func<T, bool> predicate, TError error) =>
            // The error is checked whether or not an element matches, as Option.ToResult checks it:
            // a null passed for every search that found something until the first that did not.
            error is null
                ? throw ThrowHelper.NullContent(nameof(error))
                : source.FirstOrNone(predicate).ToResult(error);

        /// <summary>The last element that matches, or the given error.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <param name="error">The error if nothing matches. Never null.</param>
        /// <returns>The last matching element, or <paramref name="error"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        public Result<T, TError> LastOrError(Func<T, bool> predicate, TError error) =>
            error is null
                ? throw ThrowHelper.NullContent(nameof(error))
                : source.LastOrNone(predicate).ToResult(error);
    }

    /// <param name="source">The query.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <remarks>The provider runs the condition and is asked for one element, as <c>Option.FirstOrNone</c> on a query is.</remarks>
    extension<T, TError>(IQueryable<T> source) where T : notnull where TError : notnull
    {
        /// <summary>The first element that matches, or the given error: the provider runs the condition, and is asked for one.</summary>
        /// <param name="predicate">The condition to match, translated by the query provider.</param>
        /// <param name="error">The error if nothing matches. Never null.</param>
        /// <returns>The first matching element, or <paramref name="error"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
        public Result<T, TError> FirstOrError(System.Linq.Expressions.Expression<Func<T, bool>> predicate, TError error) =>
            error is null
                ? throw ThrowHelper.NullContent(nameof(error))
                : source.FirstOrNone(predicate).ToResult(error);

        /// <summary>Refused: a query has no last element until it is ordered, and this would read every row to find one.</summary>
        /// <param name="predicate">The condition to match.</param>
        /// <param name="error">The error if nothing matches.</param>
        /// <returns>Never: the overload does not compile.</returns>
        [Obsolete("A query has no last element until it is ordered, and LastOrError would read every row to find one. Order it the other way and use FirstOrError, or call AsEnumerable() to read it all on purpose.", error: true)]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public Result<T, TError> LastOrError(System.Linq.Expressions.Expression<Func<T, bool>> predicate, TError error) => throw new NotSupportedException();
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
