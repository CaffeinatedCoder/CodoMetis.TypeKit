using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit.Attributes;

namespace CodoMetis.TypeKit;

/// <summary>
/// The outcome of an operation that produces no value: a success, or an error of type
/// <typeparamref name="TError"/>. There is no public <c>.Error</c>: the error is reached through
/// <see cref="Match{TResult}(Func{TResult},Func{TError,TResult})"/> and <see cref="TryGetError"/>.
/// </summary>
/// <remarks>
/// <para>
/// Create instances with <see cref="Success()"/> and <see cref="Error(TError)"/>, or by returning
/// <c>Result.Ok()</c>, <c>Result.Error(error)</c> or a bare error value from a method typed
/// <see cref="Result{TError}"/>.
/// </para>
/// <para>
/// A <c>default</c> result is <see cref="ResultState.Uninitialized"/>, neither a success nor an
/// error. Every member that would pick a branch throws <see cref="InvalidOperationException"/> on
/// it, instead of reporting a success or a <c>default(TError)</c> that was never produced.
/// </para>
/// <para>
/// <see cref="object.ToString"/> never prints the error, so a result can be logged without leaking
/// what it holds. The debugger shows it.
/// </para>
/// </remarks>
/// <typeparam name="TError">The type of the error.</typeparam>
[RequireCustomInitialization]
[JsonConverter(typeof(NotWireTypeJsonConverterFactory))]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly record struct Result<TError> where TError : notnull
{
    private readonly TError? _error;

    /// <summary>Whether this is a success, an error, or an uninitialized <c>default</c>.</summary>
    public ResultState State { get; }

    /// <summary>
    /// Creates an <see cref="ResultState.Uninitialized"/> result. Use <see cref="Success()"/> or
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

    private Result(TError? error, ResultState state)
    {
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
        ResultState.Success => "Success",
        ResultState.Error   => $"Error({_error})",
        _                   => nameof(ResultState.Uninitialized)
    };

    /// <summary>Creates a success.</summary>
    /// <returns>A successful result.</returns>
    public static Result<TError> Success() => new(default, ResultState.Success);

    /// <summary>Creates an error.</summary>
    /// <param name="error">The error. Never null.</param>
    /// <returns>A failed result holding <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static Result<TError> Error(TError error)
    {
        // As in Option.Some: notnull is only an annotation, and an error holding null would hand it
        // out of TryGetError despite [NotNullWhen(true)]. The implicit conversions come through here.
        if (error is null) throw new ArgumentNullException(nameof(error));

        return new(error, ResultState.Error);
    }

    /// <summary>Produces a value from either outcome.</summary>
    /// <param name="onSuccess">Called on success.</param>
    /// <param name="onError">Called with the error on error.</param>
    /// <typeparam name="TResult">The type of the produced value.</typeparam>
    /// <returns>What the called function returned.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<TError, TResult> onError)
    {
        // Every delegate is checked before a branch is picked, as in Option: a null for the branch
        // not taken passed until the other outcome first arrived, typically in production.
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        return Succeeded ? onSuccess() : onError(_error!);
    }

    /// <summary>Produces a value from either outcome, where the error branch does not need the error.</summary>
    /// <param name="onSuccess">Called on success.</param>
    /// <param name="onError">Called on error. It does not receive the error.</param>
    /// <typeparam name="TResult">The type of the produced value.</typeparam>
    /// <returns>What the called function returned.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onError);

        return Succeeded ? onSuccess() : onError();
    }

    /// <summary>Produces a value on success, keeping the error otherwise.</summary>
    /// <param name="fn">Called on success.</param>
    /// <typeparam name="TResult">The type of the produced value.</typeparam>
    /// <returns>A success with the produced value, or this error without calling <paramref name="fn"/>.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<TResult, TError> Map<TResult>(Func<TResult> fn) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(fn);

        return Succeeded ? Result<TResult, TError>.Success(fn()) : Result<TResult, TError>.Error(_error!);
    }

    /// <summary>Chains an operation that may itself fail.</summary>
    /// <param name="fn">Called on success.</param>
    /// <returns>The result <paramref name="fn"/> returned, or this error without calling it.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<TError> Bind(Func<Result<TError>> fn)
    {
        ArgumentNullException.ThrowIfNull(fn);

        return Succeeded ? fn() : Error(_error!);
    }

    /// <summary>Runs a side effect on success.</summary>
    /// <param name="action">Called on success.</param>
    /// <returns>This result, unchanged.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public Result<TError> Tap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Succeeded)
            action();

        return this;
    }

    /// <summary>Runs an asynchronous side effect on success.</summary>
    /// <param name="action">Called on success.</param>
    /// <returns>This result, unchanged, once <paramref name="action"/> has completed.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    public async Task<Result<TError>> TapAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Succeeded)
            await action().ConfigureAwait(false);

        return this;
    }

    /// <summary>Unwraps the error, if this is one.</summary>
    /// <param name="error">The error if this is one; otherwise <c>default</c>.</param>
    /// <returns>Whether this is an error.</returns>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetError([NotNullWhen(returnValue: true)] out TError? error)
    {
        error = _error;
        return !Succeeded;
    }

    /// <summary><see langword="true"/> for a success, <see langword="false"/> for an error.</summary>
    /// <param name="result">The result.</param>
    /// <exception cref="InvalidOperationException">The result is uninitialized.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(Result<TError> result) => result.Succeeded;

    /// <summary>Wraps a bare error value, so a method can <c>return error;</c>.</summary>
    /// <param name="error">The error.</param>
    public static implicit operator Result<TError>(TError error) => Error(error);

    /// <summary>Converts the <c>Result.Error(error)</c> marker, so a method can <c>return Result.Error(error);</c>, as it can for <see cref="Result{T,TError}"/>.</summary>
    /// <param name="error">The marker.</param>
    public static implicit operator Result<TError>(Error<TError> error) => Error(error.Value);

    /// <summary>Converts the <c>Result.Ok()</c> marker, so a method can <c>return Result.Ok();</c>.</summary>
    /// <param name="success">The marker.</param>
    public static implicit operator Result<TError>(Success success) => Success();
}
