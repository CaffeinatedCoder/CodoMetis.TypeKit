using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CodoMetis.TypeKit;

/// <summary>The state of a <see cref="Result{TError}"/> or <see cref="Result{T,TError}"/>.</summary>
public enum ResultState
{
    /// <summary>A <c>default</c> instance, neither a success nor an error.</summary>
    Uninitialized,

    /// <summary>The operation succeeded.</summary>
    Success,

    /// <summary>The operation failed.</summary>
    Error
}

/// <summary>
/// The outcome of an operation that produces a value: a success holding a
/// <typeparamref name="T"/>, or an error of type <typeparamref name="TError"/>. There is no public
/// <c>.Value</c> or <c>.Error</c>: the content is reached through <c>Match</c>,
/// <see cref="TryGetValue"/> and the combinators.
/// </summary>
/// <remarks>
/// <para>
/// Create instances with <see cref="Success(T)"/> and <see cref="Error(TError)"/>, or by returning a
/// bare value, <c>Result.Success(value)</c> or <c>Result.Error(error)</c> from a method typed
/// <see cref="Result{T,TError}"/>.
/// </para>
/// <para>
/// A <c>default</c> result is <see cref="ResultState.Uninitialized"/>, neither a success nor an
/// error. Every member that would pick a branch throws <see cref="InvalidOperationException"/> on
/// it, instead of reporting a <c>default(T)</c> or a <c>default(TError)</c> that was never produced.
/// </para>
/// <para>
/// <see cref="object.ToString"/> never prints the value or the error, so a result can be logged
/// without leaking what it holds. The debugger shows both.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
/// <typeparam name="TError">The type of the error.</typeparam>
[RequireCustomInitialization]
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly record struct Result<T, TError>
    where T : notnull
    where TError : notnull
{
    private readonly T?      _value;
    private readonly TError? _error;

    /// <summary>Whether this is a success, an error, or an uninitialized <c>default</c>.</summary>
    public ResultState State { get; }

    /// <summary>
    /// Creates an <see cref="ResultState.Uninitialized"/> result. Use <see cref="Success(T)"/> or
    /// <see cref="Error(TError)"/> instead.
    /// </summary>
    [DebuggerHidden]
    [DebuggerNonUserCode]
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Result()
    {
        State = ResultState.Uninitialized;
    }

    private Result(T? value, TError? error, ResultState state)
    {
        _value = value;
        _error = error;
        State  = state;
    }

    private bool Succeeded => State switch
    {
        ResultState.Success => true,
        ResultState.Error   => false,
        _                   => ThrowHelper.ThrowUninitializedResult<bool>()
    };

    private string DebuggerDisplay => State switch
    {
        ResultState.Success => $"Success({_value})",
        ResultState.Error   => $"Error({_error})",
        _                   => nameof(ResultState.Uninitialized)
    };

    /// <summary>Creates a success.</summary>
    /// <param name="value">The value. Never null.</param>
    /// <returns>A successful result holding <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Result<T, TError> Success(T value)
    {
        // As in Option.Some: notnull is only an annotation, and a success holding null would hand it
        // out of TryGetValue despite [NotNullWhen(true)]. Map, Bind and the conversions come through here.
        if (value is null) throw new ArgumentNullException(nameof(value));

        return new(value, default, ResultState.Success);
    }

    /// <summary>Creates an error.</summary>
    /// <param name="error">The error. Never null.</param>
    /// <returns>A failed result holding <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static Result<T, TError> Error(TError error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));

        return new(default, error, ResultState.Error);
    }

    /// <summary>Produces a value from either the value or the error.</summary>
    /// <param name="onSuccess">Called with the value on success.</param>
    /// <param name="onError">Called with the error on error.</param>
    /// <typeparam name="TResult">The type of the produced value.</typeparam>
    /// <returns>What the called function returned.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onError)
    {
        // Every delegate is checked before a branch is picked, as in Option: a null for the branch
        // not taken passed until the other outcome first arrived, typically in production.
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        return Succeeded ? onSuccess(_value!) : onError(_error!);
    }

    /// <summary>Produces a value from the value, or from a fallback that drops the error.</summary>
    /// <param name="onSuccess">Called with the value on success.</param>
    /// <param name="onError">Called on error. It does not receive the error.</param>
    /// <typeparam name="TResult">The type of the produced value.</typeparam>
    /// <returns>What the called function returned.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        return Succeeded ? onSuccess(_value!) : onError();
    }

    /// <summary>Transforms the value on success, keeping the error otherwise.</summary>
    /// <param name="selector">Called with the value on success.</param>
    /// <typeparam name="TResult">The type of the transformed value.</typeparam>
    /// <returns>A success with the transformed value, or this error without calling <paramref name="selector"/>.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<TResult, TError> Map<TResult>(Func<T, TResult> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded ? Result<TResult, TError>.Success(selector(_value!)) : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>Chains an operation that may itself fail.</summary>
    /// <param name="selector">Called with the value on success.</param>
    /// <typeparam name="TResult">The type of the chained result's value.</typeparam>
    /// <returns>The result <paramref name="selector"/> returned, or this error without calling it.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded ? selector(_value!) : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>
    /// Chains a command that may itself fail and produces no value: the value is spent, and a
    /// <see cref="Result{TError}"/> remains.
    /// </summary>
    /// <param name="selector">Called with the value on success.</param>
    /// <returns>The result <paramref name="selector"/> returned, or this error without calling it.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<TError> Bind(Func<T, Result<TError>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded ? selector(_value!) : Result<TError>.Error(_error!);
    }

    /// <summary>Transforms the error, keeping a success unchanged: for crossing from one layer's faults to another's.</summary>
    /// <param name="selector">Called with the error on error. It must not return null.</param>
    /// <typeparam name="TNewError">The type of the transformed error.</typeparam>
    /// <returns>An error with the transformed error, or this success without calling <paramref name="selector"/>.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> returned null.</exception>
    public Result<T, TNewError> MapError<TNewError>(Func<TError, TNewError> selector) where TNewError : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded ? Result<T, TNewError>.Success(_value!) : Result<T, TNewError>.Error(selector(_error!));
    }

    /// <summary>Runs a side effect on the value on success.</summary>
    /// <param name="action">Called with the value on success.</param>
    /// <returns>This result, unchanged.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<T, TError> Tap(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Succeeded)
            action(_value!);

        return this;
    }

    /// <summary>Runs a side effect on the error, such as logging it.</summary>
    /// <param name="action">Called with the error on error.</param>
    /// <returns>This result, unchanged.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<T, TError> TapError(Action<TError> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!Succeeded)
            action(_error!);

        return this;
    }

    /// <summary>
    /// Keeps a success only if its value satisfies <paramref name="predicate"/>, and turns it into
    /// <paramref name="error"/> otherwise: a rule checked inside a pipeline, without the type argument
    /// a <c>Bind</c> lambda returning either a value or <c>Result.Error(…)</c> needs.
    /// </summary>
    /// <param name="predicate">Called with the value on success.</param>
    /// <param name="error">The error for a value that fails <paramref name="predicate"/>. Never null.</param>
    /// <returns>This result if it is an error or its value passes, otherwise an error with <paramref name="error"/>.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public Result<T, TError> Ensure(Func<T, bool> predicate, TError error)
    {
        // Checked on either branch, as the delegates are and as Option.ToResult checks its error: a
        // null passed for every value that satisfied the predicate until the first one that did not.
        ArgumentNullException.ThrowIfNull(predicate);
        if (error is null) throw new ArgumentNullException(nameof(error));

        return !Succeeded || predicate(_value!) ? this : Error(error);
    }

    /// <summary>Runs an asynchronous side effect on the value on success.</summary>
    /// <param name="action">Called with the value on success.</param>
    /// <returns>This result, unchanged, once <paramref name="action"/> has completed.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public async Task<Result<T, TError>> TapAsync(Func<T, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Succeeded)
            await action(_value!).ConfigureAwait(false);

        return this;
    }

    /// <summary>Transforms the value on success with an asynchronous function, keeping the error otherwise.</summary>
    /// <param name="selector">Called with the value on success.</param>
    /// <typeparam name="TResult">The type of the transformed value.</typeparam>
    /// <returns>A success with the transformed value, or this error without calling <paramref name="selector"/>.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public async Task<Result<TResult, TError>> MapAsync<TResult>(Func<T, Task<TResult>> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded
            ? Result<TResult, TError>.Success(await selector(_value!).ConfigureAwait(false))
            : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>Chains an asynchronous operation that may itself fail.</summary>
    /// <param name="selector">Called with the value on success.</param>
    /// <typeparam name="TResult">The type of the chained result's value.</typeparam>
    /// <returns>The result <paramref name="selector"/> produced, or this error without calling it.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public async Task<Result<TResult, TError>> BindAsync<TResult>(Func<T, Task<Result<TResult, TError>>> selector) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded ? await selector(_value!).ConfigureAwait(false) : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>
    /// Chains an asynchronous command that may itself fail and produces no value: the value is spent,
    /// and a <see cref="Result{TError}"/> remains.
    /// </summary>
    /// <param name="selector">Called with the value on success.</param>
    /// <returns>The result <paramref name="selector"/> produced, or this error without calling it.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public async Task<Result<TError>> BindAsync(Func<T, Task<Result<TError>>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Succeeded ? await selector(_value!).ConfigureAwait(false) : Result<TError>.Error(_error!);
    }

    /// <summary>Unwraps the value or the error.</summary>
    /// <param name="value">The value on success; otherwise <c>default</c>.</param>
    /// <param name="error">The error on error; otherwise <c>default</c>.</param>
    /// <returns>Whether this is a success.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue([NotNullWhen(returnValue: true)] out T? value, [NotNullWhen(returnValue: false)] out TError? error)
    {
        value = _value;
        error = _error;
        return Succeeded;
    }

    /// <summary>The value as a sequence of one element, or an empty sequence for an error.</summary>
    /// <returns>A sequence with the value, or an empty one.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized, on enumeration.</exception>
    public IEnumerable<T> AsEnumerable()
    {
        if (Succeeded)
            yield return _value!;
    }

    /// <summary><see langword="true"/> for a success, <see langword="false"/> for an error.</summary>
    /// <param name="result">The result.</param>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(Result<T, TError> result) => result.Succeeded;

    /// <summary>Converts the <c>Result.Error(error)</c> marker, so a method can <c>return Result.Error(error);</c>.</summary>
    /// <param name="error">The marker.</param>
    public static implicit operator Result<T, TError>(Error<TError> error) => Error(error.Value);

    /// <summary>Wraps a bare value, so a method can <c>return value;</c>.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Result<T, TError>(T value) => Success(value);

    /// <summary>Converts the <c>Result.Success(value)</c> marker, so a method can <c>return Result.Success(value);</c>.</summary>
    /// <param name="instance">The marker.</param>
    public static implicit operator Result<T, TError>(Success<T> instance) => Success(instance.Value);
}
