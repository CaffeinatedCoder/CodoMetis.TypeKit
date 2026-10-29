using System.Runtime.CompilerServices;

namespace CodoMetis.TypeKit;

/// <summary>
/// The <c>Success</c>/<c>Error</c> markers that convert into a <see cref="Result{TError}"/> or
/// <see cref="Result{T,TError}"/>; the LINQ and sequence vocabulary for results; the zips; and the
/// continuations of a result that is still being produced (<c>Task&lt;Result&lt;…&gt;&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// Everything here stops at the first error, in argument or sequence order. Nothing collects
/// errors: that is a validation applicative, which this package leaves out on purpose.
/// </para>
/// <para>
/// The asynchronous members never resume on the caller's <see cref="SynchronizationContext"/>: every
/// <c>await</c> in them is <c>ConfigureAwait(false)</c>, so they cannot deadlock a caller that blocks
/// on a chain. A callback therefore runs where the task it waits for completed: at once, on the
/// caller's thread and context, when that task had already completed, and otherwise wherever it
/// completed, usually a thread-pool thread without the caller's context. That covers a continuation of
/// a pending result (<c>MapAsync</c>, <c>BindAsync</c>, <c>TapAsync</c> and the rest), every operation
/// of <c>TraverseAsync</c> after the first, and every step after one that waited. The caller's own
/// <c>await</c> of the chain returns to its context as usual. In a UI or a Blazor component, where a
/// callback touches state bound to the dispatcher or the renderer, await the task and call the
/// synchronous combinator on what it answers, or marshal inside the callback.
/// </para>
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
    /// and caught it would read as a failure and <c>Recover</c> would run on it. So does an
    /// <see cref="AggregateException"/> that holds cancellations and nothing else, as <c>Task.Wait()</c>
    /// and <c>.Result</c> throw for a cancelled task; one that holds anything beside them is the error.
    /// A cancellation whose <see cref="Exception.InnerException"/> is a <see cref="TimeoutException"/> is
    /// the error, wrapped or not: that is how <see cref="System.Net.Http.HttpClient"/> reports its
    /// <c>Timeout</c> elapsing, a failure of the call and not the caller's cancellation, and <c>.Result</c>
    /// on that call is the error too, although what it throws no longer holds the
    /// <see cref="TimeoutException"/>: the cancelled task still does. A delegate that returns a <see cref="Task"/> is
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
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is a task, a <c>Task</c> or a <c>ValueTask</c>; use <c>CatchAsync</c>.</exception>
    public static Result<T, Exception> Catch<T>(Func<T> action) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        RefuseTask<T>(nameof(Catch));

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
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is a task, a <c>Task</c> or a <c>ValueTask</c>; use <c>CatchAsync</c>.</exception>
    public static Result<T, TException> Catch<T, TException>(Func<T> action)
        where T : notnull
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        RefuseTask<T>(nameof(Catch));

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
    /// A delegate that returns a value is refused at compile time, a task's included, since this
    /// overload would discard it: <c>Result.Catch&lt;int, FormatException&gt;(() =&gt; int.Parse(s))</c>
    /// spells both type arguments, and <c>CatchAsync</c> waits for a task. A statement lambda that
    /// drops a value says so, <c>() =&gt; { _ = list.Remove(item); }</c>. See
    /// <see cref="Catch{T}(Func{T})"/> for cancellation.
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
    /// <see cref="OperationCanceledException"/> that is no timeout, or an <see cref="AggregateException"/>
    /// that holds such cancellations and nothing else, as <c>Task.Wait()</c> and <c>.Result</c> throw for
    /// a cancelled task. An aggregate that holds anything else beside them, a timeout included, is the error.
    /// </summary>
    private static bool IsCancellation(Exception exception)
    {
        if (exception is not AggregateException aggregate) return IsUnwrappedCancellation(exception);

        var inner = aggregate.Flatten().InnerExceptions;

        return inner.Count > 0 && inner.All(IsUnwrappedCancellation);
    }

    private static bool IsUnwrappedCancellation(Exception exception) =>
        exception is OperationCanceledException cancellation && !IsTimeout(cancellation);

    /// <summary>
    /// Whether <paramref name="cancellation"/> is a timeout, the call failing rather than the caller
    /// cancelling: <see cref="System.Net.Http.HttpClient"/> throws a <see cref="TaskCanceledException"/> over a
    /// <see cref="TimeoutException"/> when its <c>Timeout</c> elapses. <c>Task.Wait()</c> and <c>.Result</c>
    /// on that cancelled task throw a new one instead, naming the task and holding no inner exception
    /// (measured on .NET 10), so the task's own cancellation is read: the task has completed, and
    /// <c>GetResult</c> rethrows it without waiting.
    /// </summary>
    private static bool IsTimeout(OperationCanceledException cancellation)
    {
        if (cancellation.InnerException is TimeoutException) return true;
        if (cancellation is not TaskCanceledException { InnerException: null, Task: { IsCanceled: true } task }) return false;

        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException own)
        {
            return own.InnerException is TimeoutException;
        }

        return false;
    }

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

    /// <summary>Refused: the task's exception is thrown after <c>Catch</c> has returned. Use <see cref="CatchAsync{TException}(Func{Task})"/>.</summary>
    /// <remarks>
    /// Without it, a delegate returning a task, an <c>async</c> lambda among them, converts to the
    /// command form's <see cref="Action"/>: the task is discarded, and an <c>async</c> lambda becomes
    /// <c>async void</c>, whose exception reaches no caller at all.
    /// </remarks>
    /// <param name="action">A delegate returning a task.</param>
    /// <typeparam name="TException">The exception that would be an outcome.</typeparam>
    /// <returns>Never: the overload does not compile.</returns>
    [Obsolete("The delegate returns a Task, whose exception is thrown after Catch has returned. Use CatchAsync.", error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static Result<TException> Catch<TException>(Func<Task> action) where TException : Exception => throw new NotSupportedException();

    /// <summary>
    /// Refused: the command form would discard the value, and with a <c>ValueTask</c> its exception.
    /// Use <see cref="Catch{T, TException}(Func{T})"/>, or <see cref="CatchAsync{T, TException}(Func{Task{T}})"/> for a task.
    /// </summary>
    /// <remarks>
    /// A lambda with an expression body converts to <see cref="Action"/> and its value is dropped, so
    /// <c>Catch&lt;FormatException&gt;(() =&gt; int.Parse(s))</c> answered without the number, and
    /// <c>Catch&lt;IOException&gt;(() =&gt; stream.ReadAsync(buffer))</c> without the read's exception: a
    /// <c>ValueTask&lt;T&gt;</c> converts to no task type an overload could name. Every value converts to
    /// <see cref="object"/>, so this overload is the better match for any of them; a task is
    /// <see cref="Catch{TException}(Func{Task})"/>'s, the more specific one.
    /// </remarks>
    /// <param name="action">A delegate returning a value.</param>
    /// <typeparam name="TException">The exception that would be an outcome.</typeparam>
    /// <returns>Never: the overload does not compile.</returns>
    [Obsolete("The delegate returns a value, which Catch<TException> would discard, a ValueTask's exception with it. Spell the value's type, Catch<T, TException>, or use CatchAsync for a task.", error: true)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static Result<TException> Catch<TException>(Func<object?> action) where TException : Exception => throw new NotSupportedException();

    /// <summary>
    /// Refuses a value that is itself a task, before the delegate runs: its exception would be thrown
    /// after <c>Catch</c> had returned a success. The overloads over a task-returning delegate refuse
    /// it at compile time; this is the type argument spelled as a task, <c>Catch&lt;Task&lt;int&gt;, …&gt;</c>,
    /// and a <c>ValueTask</c>, whose refusal at compile time would break a build that works today
    /// (decision 110).
    /// </summary>
    private static void RefuseTask<T>(string method)
    {
        var type = typeof(T);
        if (typeof(Task).IsAssignableFrom(type) || type == typeof(ValueTask) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            throw new ArgumentException(
                $"{method} was given a delegate whose value is a task, whose exception would be thrown after {method} had returned a success. "
              + (method == nameof(Catch) ? "Use CatchAsync." : "Await the inner task in the delegate.") + Refusals.TaskGivenToCatch,
                "action");
    }

    /// <summary>
    /// Awaits <paramref name="action"/> and returns its value as a success, or the exception the
    /// task threw as the error, whether before or after its first <c>await</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <see cref="OperationCanceledException"/> passes through, so a cancelled token surfaces as
    /// cancellation, and so does an <see cref="AggregateException"/> of cancellations alone; a timeout,
    /// which <see cref="System.Net.Http.HttpClient"/> throws as a cancellation over a
    /// <see cref="TimeoutException"/>, is the error. A delegate returning a <c>ValueTask</c>
    /// binds to <see cref="CatchAsync{T}(Func{ValueTask{T}})"/>, and an <c>async</c> lambda to this one.
    /// See <see cref="Catch{T}(Func{T})"/> for cancellations, timeouts and null.
    /// </para>
    /// <para>
    /// The error is what <c>await</c> throws, the exception <c>try { await task; } catch (Exception e)</c>
    /// catches, in every overload: for a task that faulted with several exceptions, as
    /// <c>Task.WhenAll</c>'s does when more than one operation fails, the first of them, and a narrowed
    /// overload looks at that one alone. The task's <see cref="Task.Exception"/> holds them all: where
    /// every failure matters, hold the task, and read it once the result is an error.
    /// </para>
    /// </remarks>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, returned no task, or its task produced null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is itself a task; await it in the delegate.</exception>
    public static async Task<Result<T, Exception>> CatchAsync<T>(Func<Task<T>> action) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        RefuseTask<T>(nameof(CatchAsync));

        Task<T> task;
        try
        {
            task = action();
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<T, Exception>.Error(exception);
        }

        if (task is null) throw ThrowHelper.NullTask(nameof(action));

        T value;
        try
        {
            value = await task.ConfigureAwait(false);
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
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or returned no task.</exception>
    public static async Task<Result<Exception>> CatchAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Task task;
        try
        {
            task = action();
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<Exception>.Error(exception);
        }

        if (task is null) throw ThrowHelper.NullTask(nameof(action));

        try
        {
            await task.ConfigureAwait(false);
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
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, returned no task, or its task produced null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is itself a task; await it in the delegate.</exception>
    public static async Task<Result<T, TException>> CatchAsync<T, TException>(Func<Task<T>> action)
        where T : notnull
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        RefuseTask<T>(nameof(CatchAsync));

        Task<T> task;
        try
        {
            task = action();
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<T, TException>.Error(exception);
        }

        if (task is null) throw ThrowHelper.NullTask(nameof(action));

        T value;
        try
        {
            value = await task.ConfigureAwait(false);
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
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or returned no task.</exception>
    public static async Task<Result<TException>> CatchAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        Task task;
        try
        {
            task = action();
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<TException>.Error(exception);
        }

        if (task is null) throw ThrowHelper.NullTask(nameof(action));

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<TException>.Error(exception);
        }

        return Result<TException>.Success();
    }

    /// <summary>
    /// Awaits <paramref name="action"/>, a call that returns a <see cref="ValueTask{TResult}"/>, and
    /// returns its value as a success, or the exception it threw as the error, whether before or after
    /// its first <c>await</c>: <c>Result.CatchAsync(() => stream.ReadAsync(buffer))</c>.
    /// See <see cref="CatchAsync{T}(Func{Task{T}})"/>.
    /// </summary>
    /// <remarks>
    /// An <c>async</c> lambda converts to either delegate, so this overload has the lower priority and such
    /// a lambda binds to the <see cref="Task{TResult}"/> form, as it did before this one existed. A lambda or
    /// a method group that returns a <c>ValueTask</c> converts to this one alone.
    /// </remarks>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or its task produced null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is itself a task; await it in the delegate.</exception>
    [OverloadResolutionPriority(-1)]
    public static async Task<Result<T, Exception>> CatchAsync<T>(Func<ValueTask<T>> action) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(action);
        RefuseTask<T>(nameof(CatchAsync));

        ValueTask<T> task;
        try
        {
            task = action();
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<T, Exception>.Error(exception);
        }

        T value;
        try
        {
            value = await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<T, Exception>.Error(exception);
        }

        return Result<T, Exception>.Success(value);
    }

    /// <summary>
    /// Awaits <paramref name="action"/>, a call that returns a <see cref="ValueTask"/>, and returns a
    /// success, or the exception it threw as the error. See <see cref="CatchAsync{T}(Func{ValueTask{T}})"/>.
    /// </summary>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <returns>A success, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    [OverloadResolutionPriority(-1)]
    public static async Task<Result<Exception>> CatchAsync(Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        ValueTask task;
        try
        {
            task = action();
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<Exception>.Error(exception);
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return Result<Exception>.Error(exception);
        }

        return Result<Exception>.Success();
    }

    /// <summary>
    /// Awaits <paramref name="action"/>, a call that returns a <see cref="ValueTask{TResult}"/>, and
    /// returns its value as a success, or the <typeparamref name="TException"/> it threw as the error. Any
    /// other exception propagates. See <see cref="CatchAsync{T}(Func{ValueTask{T}})"/>.
    /// </summary>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TException">The exception that is an outcome; any other propagates.</typeparam>
    /// <returns>A success holding the value, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null, or its task produced null.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is itself a task; await it in the delegate.</exception>
    [OverloadResolutionPriority(-1)]
    public static async Task<Result<T, TException>> CatchAsync<T, TException>(Func<ValueTask<T>> action)
        where T : notnull
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        RefuseTask<T>(nameof(CatchAsync));

        ValueTask<T> task;
        try
        {
            task = action();
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<T, TException>.Error(exception);
        }

        T value;
        try
        {
            value = await task.ConfigureAwait(false);
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<T, TException>.Error(exception);
        }

        return Result<T, TException>.Success(value);
    }

    /// <summary>
    /// Awaits <paramref name="action"/>, a call that returns a <see cref="ValueTask"/>, and returns a
    /// success, or the <typeparamref name="TException"/> it threw as the error. Any other exception
    /// propagates. See <see cref="CatchAsync{T}(Func{ValueTask{T}})"/>.
    /// </summary>
    /// <param name="action">The asynchronous call whose outcome is wanted.</param>
    /// <typeparam name="TException">The exception that is an outcome; any other propagates.</typeparam>
    /// <returns>A success, or an error holding the exception thrown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    [OverloadResolutionPriority(-1)]
    public static async Task<Result<TException>> CatchAsync<TException>(Func<ValueTask> action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        ValueTask task;
        try
        {
            task = action();
        }
        catch (TException exception) when (!IsCancellation(exception))
        {
            return Result<TException>.Error(exception);
        }

        try
        {
            await task.ConfigureAwait(false);
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
        /// <remarks>
        /// On an <see cref="IQueryable{T}"/>, a <c>Func</c> held in a variable or a method group binds here
        /// and reads every row; a lambda written at the call binds to the query's overload.
        /// </remarks>
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
    /// <remarks>The provider runs the condition and is asked for one element, as <c>Option.FirstOrNone</c> on a query is, and like it, the query is not precompiled under Native AOT and throws there.</remarks>
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

        /// <summary>
        /// Applies an asynchronous operation that may fail to every element, one at a time and in
        /// order, stopping at the first error: the next element's operation is not started after one
        /// failed. Operations that may all run at once are <c>Task.WhenAll</c> over their tasks and
        /// <c>Sequence()</c> over what it answers.
        /// </summary>
        /// <param name="selector">Called with each element and awaited, until one fails.</param>
        /// <typeparam name="TResult">The type of each produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with every produced value, in order, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        public async Task<Result<IReadOnlyList<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, Task<Result<TResult, TError>>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            var values = source.TryGetNonEnumeratedCount(out var count) ? new List<TResult>(count) : [];

            foreach (var element in source)
            {
                var result = await ThrowHelper.Awaitable(selector(element), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (!result.TryGetValue(out var value, out var error)) return Result<IReadOnlyList<TResult>, TError>.Error(error);

                values.Add(value);
            }

            return Result<IReadOnlyList<TResult>, TError>.Success(values);
        }

        /// <summary>
        /// Applies an asynchronous operation that may fail to every element, one at a time and in
        /// order, stopping at the first error, with a cancellation token handed to each operation and
        /// checked before each: once cancelled, the next operation is not started and the
        /// <see cref="OperationCanceledException"/> propagates, since a cancellation is not an error.
        /// </summary>
        /// <param name="selector">Called with each element and the token, and awaited, until one fails.</param>
        /// <param name="cancellationToken">Checked before each operation, and handed to it.</param>
        /// <typeparam name="TResult">The type of each produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with every produced value, in order, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before an operation started.</exception>
        public async Task<Result<IReadOnlyList<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, CancellationToken, Task<Result<TResult, TError>>> selector, CancellationToken cancellationToken)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            var values = source.TryGetNonEnumeratedCount(out var count) ? new List<TResult>(count) : [];

            foreach (var element in source)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ThrowHelper.Awaitable(selector(element, cancellationToken), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (!result.TryGetValue(out var value, out var error)) return Result<IReadOnlyList<TResult>, TError>.Error(error);

                values.Add(value);
            }

            return Result<IReadOnlyList<TResult>, TError>.Success(values);
        }

        /// <summary>
        /// Applies an asynchronous command that may fail to every element, one at a time and in
        /// order, stopping at the first error: the next element's command is not started after one
        /// failed.
        /// </summary>
        /// <param name="selector">Called with each element and awaited, until one fails.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success if every command succeeded, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        public async Task<Result<TError>> TraverseAsync<TError>(Func<T, Task<Result<TError>>> selector) where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            foreach (var element in source)
            {
                var result = await ThrowHelper.Awaitable(selector(element), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (result.TryGetError(out var error)) return Result<TError>.Error(error);
            }

            return Result<TError>.Success();
        }

        /// <summary>
        /// Applies an asynchronous command that may fail to every element, one at a time and in
        /// order, stopping at the first error, with a cancellation token handed to each command and
        /// checked before each: once cancelled, the next command is not started and the
        /// <see cref="OperationCanceledException"/> propagates, since a cancellation is not an error.
        /// </summary>
        /// <param name="selector">Called with each element and the token, and awaited, until one fails.</param>
        /// <param name="cancellationToken">Checked before each command, and handed to it.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success if every command succeeded, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before a command started.</exception>
        public async Task<Result<TError>> TraverseAsync<TError>(Func<T, CancellationToken, Task<Result<TError>>> selector, CancellationToken cancellationToken) where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            foreach (var element in source)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ThrowHelper.Awaitable(selector(element, cancellationToken), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (result.TryGetError(out var error)) return Result<TError>.Error(error);
            }

            return Result<TError>.Success();
        }
    }

    /// <param name="source">The stream.</param>
    /// <typeparam name="T">The type of the elements.</typeparam>
    extension<T>(IAsyncEnumerable<T> source)
    {
        /// <summary>
        /// Applies an asynchronous operation that may fail to every element of a stream, one at a time
        /// and in order, stopping at the first error: the next element is not read, nor its operation
        /// started, after one failed, and the stream's enumerator is disposed.
        /// </summary>
        /// <param name="selector">Called with each element and awaited, until one fails.</param>
        /// <typeparam name="TResult">The type of each produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with every produced value, in order, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        public async Task<Result<IReadOnlyList<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, Task<Result<TResult, TError>>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            var values = new List<TResult>();

            await foreach (var element in source.ConfigureAwait(false))
            {
                var result = await ThrowHelper.Awaitable(selector(element), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (!result.TryGetValue(out var value, out var error)) return Result<IReadOnlyList<TResult>, TError>.Error(error);

                values.Add(value);
            }

            return Result<IReadOnlyList<TResult>, TError>.Success(values);
        }

        /// <summary>
        /// Applies an asynchronous operation that may fail to every element of a stream, one at a time
        /// and in order, stopping at the first error, with a cancellation token handed to the stream and
        /// to each operation, and checked before each element is read and before each operation: once
        /// cancelled, nothing more is read or started and the <see cref="OperationCanceledException"/>
        /// propagates, since a cancellation is not an error.
        /// </summary>
        /// <param name="selector">Called with each element and the token, and awaited, until one fails.</param>
        /// <param name="cancellationToken">Handed to the stream's enumerator and to each operation, and checked before each.</param>
        /// <typeparam name="TResult">The type of each produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with every produced value, in order, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before an element was read or an operation started.</exception>
        public async Task<Result<IReadOnlyList<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, CancellationToken, Task<Result<TResult, TError>>> selector, CancellationToken cancellationToken)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            var values = new List<TResult>();

            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var element in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ThrowHelper.Awaitable(selector(element, cancellationToken), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (!result.TryGetValue(out var value, out var error)) return Result<IReadOnlyList<TResult>, TError>.Error(error);

                values.Add(value);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Result<IReadOnlyList<TResult>, TError>.Success(values);
        }

        /// <summary>
        /// Applies an asynchronous command that may fail to every element of a stream, one at a time and
        /// in order, stopping at the first error: the next element is not read, nor its command started,
        /// after one failed, and the stream's enumerator is disposed.
        /// </summary>
        /// <param name="selector">Called with each element and awaited, until one fails.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success if every command succeeded, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        public async Task<Result<TError>> TraverseAsync<TError>(Func<T, Task<Result<TError>>> selector) where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            await foreach (var element in source.ConfigureAwait(false))
            {
                var result = await ThrowHelper.Awaitable(selector(element), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (result.TryGetError(out var error)) return Result<TError>.Error(error);
            }

            return Result<TError>.Success();
        }

        /// <summary>
        /// Applies an asynchronous command that may fail to every element of a stream, one at a time and
        /// in order, stopping at the first error, with a cancellation token handed to the stream and to
        /// each command, and checked before each element is read and before each command: once
        /// cancelled, nothing more is read or started and the <see cref="OperationCanceledException"/>
        /// propagates, since a cancellation is not an error.
        /// </summary>
        /// <param name="selector">Called with each element and the token, and awaited, until one fails.</param>
        /// <param name="cancellationToken">Handed to the stream's enumerator and to each command, and checked before each.</param>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success if every command succeeded, or the first error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before an element was read or a command started.</exception>
        public async Task<Result<TError>> TraverseAsync<TError>(Func<T, CancellationToken, Task<Result<TError>>> selector, CancellationToken cancellationToken) where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(selector);

            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var element in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ThrowHelper.Awaitable(selector(element, cancellationToken), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
                if (result.TryGetError(out var error)) return Result<TError>.Error(error);

                cancellationToken.ThrowIfCancellationRequested();
            }

            return Result<TError>.Success();
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

    /// <param name="results">The stream of results.</param>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(IAsyncEnumerable<Result<T, TError>> results) where T : notnull where TError : notnull
    {
        /// <summary>Every value of a stream, if every result in it is a success.</summary>
        /// <param name="cancellationToken">Handed to the stream's enumerator, and checked before each result is read.</param>
        /// <returns>
        /// A success with every value, in order, or the first error. After an error nothing more is read
        /// and the stream's enumerator is disposed.
        /// </returns>
        /// <exception cref="InvalidOperationException">A result up to the first error is uninitialized.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before a result was read.</exception>
        public async Task<Result<IReadOnlyList<T>, TError>> SequenceAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(results);

            var values = new List<T>();

            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var result in results.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!result.TryGetValue(out var value, out var error)) return Result<IReadOnlyList<T>, TError>.Error(error);

                values.Add(value);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Result<IReadOnlyList<T>, TError>.Success(values);
        }
    }

    /// <param name="results">The stream of results.</param>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<TError>(IAsyncEnumerable<Result<TError>> results) where TError : notnull
    {
        /// <summary>A success if every result of a stream is one.</summary>
        /// <param name="cancellationToken">Handed to the stream's enumerator, and checked before each result is read.</param>
        /// <returns>
        /// A success, or the first error. After an error nothing more is read and the stream's enumerator
        /// is disposed.
        /// </returns>
        /// <exception cref="InvalidOperationException">A result up to the first error is uninitialized.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before a result was read.</exception>
        public async Task<Result<TError>> SequenceAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(results);

            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var result in results.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (result.TryGetError(out var error)) return Result<TError>.Error(error);

                cancellationToken.ThrowIfCancellationRequested();
            }

            return Result<TError>.Success();
        }
    }

    /// <param name="option">An optional input that is validated when present.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(Option<Result<T, TError>> option) where T : notnull where TError : notnull
    {
        /// <summary>
        /// Turns an optional result inside out: <c>None</c> is a success with no value, a success is a
        /// success with the value, and an error stays the error.
        /// </summary>
        /// <returns>A success holding an option, or the error.</returns>
        /// <exception cref="InvalidOperationException">The option holds an uninitialized result.</exception>
        public Result<Option<T>, TError> Transpose()
        {
            if (!option.TryGetValue(out var result)) return Result<Option<T>, TError>.Success(Option.None<T>());

            return result.TryGetValue(out var value, out var error)
                ? Result<Option<T>, TError>.Success(Option.Some(value))
                : Result<Option<T>, TError>.Error(error);
        }
    }

    /// <param name="result">A result whose value may be absent.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    extension<T, TError>(Result<Option<T>, TError> result) where T : notnull where TError : notnull
    {
        /// <summary>
        /// Turns a result of an option inside out: a success with no value is <c>None</c>, a success
        /// with a value is that success, and an error is an option holding the error.
        /// </summary>
        /// <returns>An option holding a result, or <c>None</c>.</returns>
        /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
        public Option<Result<T, TError>> Transpose()
        {
            if (!result.TryGetValue(out var option, out var error)) return Option.Some(Result<T, TError>.Error(error));

            return option.TryGetValue(out var value) ? Option.Some(Result<T, TError>.Success(value)) : Option.None<Result<T, TError>>();
        }
    }

    /// <param name="option">An optional input.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    extension<T>(Option<T> option) where T : notnull
    {
        /// <summary>
        /// Applies an operation that may fail to the value, if there is one: an optional input is
        /// validated when present, <c>dto.Phone.ToOption().Traverse(Phone.Create)</c>.
        /// </summary>
        /// <param name="selector">Called with the value if there is one.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with no value for <c>None</c>, a success with the produced value, or its error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        public Result<Option<TResult>, TError> Traverse<TResult, TError>(Func<T, Result<TResult, TError>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            return option.Map(selector).Transpose();
        }

        /// <summary>
        /// Applies an asynchronous operation that may fail to the value, if there is one: an optional
        /// input checked against a store when present, <c>await request.Coupon.TraverseAsync(coupons.FindAsync)</c>.
        /// For <c>None</c> the selector is not called.
        /// </summary>
        /// <param name="selector">Called with the value if there is one, and awaited.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with no value for <c>None</c>, a success with the produced value, or its error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        public async Task<Result<Option<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, Task<Result<TResult, TError>>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            if (!option.TryGetValue(out var value)) return Result<Option<TResult>, TError>.Success(Option.None<TResult>());

            var result = await ThrowHelper.Awaitable(selector(value), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
            return result.Map(static produced => Option.Some(produced));
        }

        /// <summary>
        /// Applies an asynchronous operation that may fail to the value, if there is one, with a
        /// cancellation token handed to the operation and checked before it: once cancelled, the
        /// operation is not started and the <see cref="OperationCanceledException"/> propagates, since a
        /// cancellation is not an error. For <c>None</c> the selector is not called and the token not checked.
        /// </summary>
        /// <param name="selector">Called with the value and the token if there is a value, and awaited.</param>
        /// <param name="cancellationToken">Checked before the operation, and handed to it.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success with no value for <c>None</c>, a success with the produced value, or its error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the operation started.</exception>
        public async Task<Result<Option<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, CancellationToken, Task<Result<TResult, TError>>> selector, CancellationToken cancellationToken)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            if (!option.TryGetValue(out var value)) return Result<Option<TResult>, TError>.Success(Option.None<TResult>());

            cancellationToken.ThrowIfCancellationRequested();

            var result = await ThrowHelper.Awaitable(selector(value, cancellationToken), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
            return result.Map(static produced => Option.Some(produced));
        }
    }

    /// <param name="patch">A field of a merge patch, validated when it carries a value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    extension<T>(Patch<T> patch) where T : notnull
    {
        /// <summary>
        /// Applies an operation that may fail to the value, if the field carried one: a patch over
        /// what the request sent is validated into a patch over the domain's type,
        /// <c>request.Email.Traverse(Email.Create)</c>, and a clear or an absent field passes through
        /// as it is, since there is nothing to validate.
        /// </summary>
        /// <param name="selector">Called with the value when the field carried one.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success holding a patch in the same state, with the produced value when the field carried one, or the error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        public Result<Patch<TResult>, TError> Traverse<TResult, TError>(Func<T, Result<TResult, TError>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            if (!patch.TryGetValue(out var value))
                return Result<Patch<TResult>, TError>.Success(patch.State == PatchState.Null ? Patch.Null<TResult>() : Patch.Absent<TResult>());

            return selector(value).Map(static produced => Patch.Value(produced));
        }

        /// <summary>
        /// Applies an asynchronous operation that may fail to the value, if the field carried one: a
        /// patch over what the request sent is checked against a store into a patch over the domain's
        /// type, <c>await request.Manager.TraverseAsync(people.FindAsync)</c>. A clear or an absent field
        /// passes through as it is, and the selector is not called.
        /// </summary>
        /// <param name="selector">Called with the value when the field carried one, and awaited.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success holding a patch in the same state, with the produced value when the field carried one, or the error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        public async Task<Result<Patch<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, Task<Result<TResult, TError>>> selector)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            if (!patch.TryGetValue(out var value))
                return Result<Patch<TResult>, TError>.Success(patch.State == PatchState.Null ? Patch.Null<TResult>() : Patch.Absent<TResult>());

            var result = await ThrowHelper.Awaitable(selector(value), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
            return result.Map(static produced => Patch.Value(produced));
        }

        /// <summary>
        /// Applies an asynchronous operation that may fail to the value, if the field carried one, with a
        /// cancellation token handed to the operation and checked before it: once cancelled, the
        /// operation is not started and the <see cref="OperationCanceledException"/> propagates, since a
        /// cancellation is not an error. A clear or an absent field passes through as it is, and neither the
        /// selector is called nor the token checked.
        /// </summary>
        /// <param name="selector">Called with the value and the token when the field carried one, and awaited.</param>
        /// <param name="cancellationToken">Checked before the operation, and handed to it.</param>
        /// <typeparam name="TResult">The type of the produced value.</typeparam>
        /// <typeparam name="TError">The type of the error.</typeparam>
        /// <returns>A success holding a patch in the same state, with the produced value when the field carried one, or the error.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="selector"/> returned an uninitialized result.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned no task.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the operation started.</exception>
        public async Task<Result<Patch<TResult>, TError>> TraverseAsync<TResult, TError>(Func<T, CancellationToken, Task<Result<TResult, TError>>> selector, CancellationToken cancellationToken)
            where TResult : notnull
            where TError : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);

            if (!patch.TryGetValue(out var value))
                return Result<Patch<TResult>, TError>.Success(patch.State == PatchState.Null ? Patch.Null<TResult>() : Patch.Absent<TResult>());

            cancellationToken.ThrowIfCancellationRequested();

            var result = await ThrowHelper.Awaitable(selector(value, cancellationToken), nameof(TraverseAsync), nameof(selector)).ConfigureAwait(false);
            return result.Map(static produced => Patch.Value(produced));
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
