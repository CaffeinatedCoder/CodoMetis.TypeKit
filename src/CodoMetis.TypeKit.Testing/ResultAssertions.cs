namespace CodoMetis.TypeKit.Testing;

/// <summary>Assertions on a <see cref="Result{T, TError}"/> and a <see cref="Result{TError}"/>.</summary>
/// <remarks>
/// <para>
/// <c>ShouldBeSuccess()</c> and <c>ShouldBeError()</c> hand the value or the error back, so the test
/// compares it with the assertions it already uses. A failure names the error it found, a fault being
/// what a test needs to see, and never prints a value, as a <c>Result</c> never does.
/// </para>
/// <para>
/// An uninitialized result, a <c>default</c> one, is reported as such: it is neither a success nor an
/// error, and every assertion fails on it.
/// </para>
/// </remarks>
public static class ResultAssertions
{
    /// <summary>The value of a result that must be a success.</summary>
    /// <param name="result">The result under test.</param>
    /// <returns>The value.</returns>
    /// <exception cref="TypeKitAssertionException">The result is an error, or uninitialized.</exception>
    public static T ShouldBeSuccess<T, TError>(this Result<T, TError> result)
        where T : notnull
        where TError : notnull
    {
        Outcomes.ThrowIfUninitialized<Result<T, TError>>(result.State, expected: "a success");

        return result.TryGetValue(out var value, out var error)
            ? value
            : throw new TypeKitAssertionException($"Expected a success, but the {Outcomes.Name<Result<T, TError>>()} is the error {error}.");
    }

    /// <summary>The error of a result that must be an error.</summary>
    /// <param name="result">The result under test.</param>
    /// <returns>The error.</returns>
    /// <exception cref="TypeKitAssertionException">The result is a success, or uninitialized.</exception>
    public static TError ShouldBeError<T, TError>(this Result<T, TError> result)
        where T : notnull
        where TError : notnull
    {
        Outcomes.ThrowIfUninitialized<Result<T, TError>>(result.State, expected: "an error");

        return result.TryGetValue(out _, out var error)
            ? throw new TypeKitAssertionException($"Expected an error, but the {Outcomes.Name<Result<T, TError>>()} is a success (its value is not printed).")
            : error;
    }

    /// <summary>Fails unless the result is the error <paramref name="expected"/>.</summary>
    /// <param name="result">The result under test.</param>
    /// <param name="expected">The error the result must be, compared with its default equality.</param>
    /// <exception cref="TypeKitAssertionException">The result is a success, another error, or uninitialized.</exception>
    public static void ShouldBeError<T, TError>(this Result<T, TError> result, TError expected)
        where T : notnull
        where TError : notnull =>
        Outcomes.ThrowIfOtherError<Result<T, TError>, TError>(result.ShouldBeError(), expected);

    /// <summary>Fails unless the result is a success.</summary>
    /// <param name="result">The result under test.</param>
    /// <exception cref="TypeKitAssertionException">The result is an error, or uninitialized.</exception>
    public static void ShouldBeSuccess<TError>(this Result<TError> result) where TError : notnull
    {
        Outcomes.ThrowIfUninitialized<Result<TError>>(result.State, expected: "a success");

        if (result.TryGetError(out var error))
            throw new TypeKitAssertionException($"Expected a success, but the {Outcomes.Name<Result<TError>>()} is the error {error}.");
    }

    /// <summary>The error of a result that must be an error.</summary>
    /// <param name="result">The result under test.</param>
    /// <returns>The error.</returns>
    /// <exception cref="TypeKitAssertionException">The result is a success, or uninitialized.</exception>
    public static TError ShouldBeError<TError>(this Result<TError> result) where TError : notnull
    {
        Outcomes.ThrowIfUninitialized<Result<TError>>(result.State, expected: "an error");

        return result.TryGetError(out var error)
            ? error
            : throw new TypeKitAssertionException($"Expected an error, but the {Outcomes.Name<Result<TError>>()} is a success.");
    }

    /// <summary>Fails unless the result is the error <paramref name="expected"/>.</summary>
    /// <param name="result">The result under test.</param>
    /// <param name="expected">The error the result must be, compared with its default equality.</param>
    /// <exception cref="TypeKitAssertionException">The result is a success, another error, or uninitialized.</exception>
    public static void ShouldBeError<TError>(this Result<TError> result, TError expected) where TError : notnull =>
        Outcomes.ThrowIfOtherError<Result<TError>, TError>(result.ShouldBeError(), expected);

    /// <inheritdoc cref="ShouldBeSuccess{T, TError}(Result{T, TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task<T> ShouldBeSuccess<T, TError>(this Task<Result<T, TError>> result)
        where T : notnull
        where TError : notnull
    {
        return (await Outcomes.Pending(result, nameof(result)).ConfigureAwait(false)).ShouldBeSuccess();
    }

    /// <inheritdoc cref="ShouldBeSuccess{T, TError}(Result{T, TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task<T> ShouldBeSuccess<T, TError>(this ValueTask<Result<T, TError>> result)
        where T : notnull
        where TError : notnull
    {
        return (await result.ConfigureAwait(false)).ShouldBeSuccess();
    }

    /// <inheritdoc cref="ShouldBeError{T, TError}(Result{T, TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task<TError> ShouldBeError<T, TError>(this Task<Result<T, TError>> result)
        where T : notnull
        where TError : notnull
    {
        return (await Outcomes.Pending(result, nameof(result)).ConfigureAwait(false)).ShouldBeError();
    }

    /// <inheritdoc cref="ShouldBeError{T, TError}(Result{T, TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task<TError> ShouldBeError<T, TError>(this ValueTask<Result<T, TError>> result)
        where T : notnull
        where TError : notnull
    {
        return (await result.ConfigureAwait(false)).ShouldBeError();
    }

    /// <inheritdoc cref="ShouldBeError{T, TError}(Result{T, TError}, TError)"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task ShouldBeError<T, TError>(this Task<Result<T, TError>> result, TError expected)
        where T : notnull
        where TError : notnull
    {
        (await Outcomes.Pending(result, nameof(result)).ConfigureAwait(false)).ShouldBeError(expected);
    }

    /// <inheritdoc cref="ShouldBeError{T, TError}(Result{T, TError}, TError)"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task ShouldBeError<T, TError>(this ValueTask<Result<T, TError>> result, TError expected)
        where T : notnull
        where TError : notnull
    {
        (await result.ConfigureAwait(false)).ShouldBeError(expected);
    }

    /// <inheritdoc cref="ShouldBeSuccess{TError}(Result{TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task ShouldBeSuccess<TError>(this Task<Result<TError>> result) where TError : notnull
    {
        (await Outcomes.Pending(result, nameof(result)).ConfigureAwait(false)).ShouldBeSuccess();
    }

    /// <inheritdoc cref="ShouldBeSuccess{TError}(Result{TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task ShouldBeSuccess<TError>(this ValueTask<Result<TError>> result) where TError : notnull
    {
        (await result.ConfigureAwait(false)).ShouldBeSuccess();
    }

    /// <inheritdoc cref="ShouldBeError{TError}(Result{TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task<TError> ShouldBeError<TError>(this Task<Result<TError>> result) where TError : notnull
    {
        return (await Outcomes.Pending(result, nameof(result)).ConfigureAwait(false)).ShouldBeError();
    }

    /// <inheritdoc cref="ShouldBeError{TError}(Result{TError})"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task<TError> ShouldBeError<TError>(this ValueTask<Result<TError>> result) where TError : notnull
    {
        return (await result.ConfigureAwait(false)).ShouldBeError();
    }

    /// <inheritdoc cref="ShouldBeError{TError}(Result{TError}, TError)"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task ShouldBeError<TError>(this Task<Result<TError>> result, TError expected) where TError : notnull
    {
        (await Outcomes.Pending(result, nameof(result)).ConfigureAwait(false)).ShouldBeError(expected);
    }

    /// <inheritdoc cref="ShouldBeError{TError}(Result{TError}, TError)"/>
    /// <remarks>Awaits <paramref name="result"/> first: an exception the task ends with propagates as it is.</remarks>
    public static async Task ShouldBeError<TError>(this ValueTask<Result<TError>> result, TError expected) where TError : notnull
    {
        (await result.ConfigureAwait(false)).ShouldBeError(expected);
    }
}
