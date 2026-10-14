using CodoMetis.TypeKit;

namespace ResultCatch;

/// <summary>The factories as the base package would ship them on its <c>Result</c> static class.</summary>
public static class Candidate
{
    public static Result<T, Exception> Catch<T>(Func<T> action) where T : notnull
    {
        try
        {
            return Result<T, Exception>.Success(action());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T, Exception>.Error(exception);
        }
    }

    public static Result<Exception> Catch(Action action)
    {
        try
        {
            action();
            return Result<Exception>.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<Exception>.Error(exception);
        }
    }

    public static Result<T, TException> Catch<T, TException>(Func<T> action)
        where T : notnull
        where TException : Exception
    {
        try
        {
            return Result<T, TException>.Success(action());
        }
        catch (TException exception) when (exception is not OperationCanceledException)
        {
            return Result<T, TException>.Error(exception);
        }
    }

    public static Result<TException> Catch<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return Result<TException>.Success();
        }
        catch (TException exception) when (exception is not OperationCanceledException)
        {
            return Result<TException>.Error(exception);
        }
    }

    // The trap: a Task-returning delegate given to Catch binds T to the task and catches nothing
    // the task throws. The overload is more specific than Func<T>, so it wins, and it is an error.
    [Obsolete("The delegate returns a Task, which Catch cannot observe: use CatchAsync.", error: true)]
    public static Result<Task<T>, Exception> Catch<T>(Func<Task<T>> action) => throw new NotSupportedException();

    [Obsolete("The delegate returns a Task, which Catch cannot observe: use CatchAsync.", error: true)]
    public static Result<Exception> Catch(Func<Task> action) => throw new NotSupportedException();

    public static async Task<Result<T, Exception>> CatchAsync<T>(Func<Task<T>> action) where T : notnull
    {
        try
        {
            return Result<T, Exception>.Success(await action().ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T, Exception>.Error(exception);
        }
    }

    public static async Task<Result<Exception>> CatchAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return Result<Exception>.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<Exception>.Error(exception);
        }
    }

    public static async Task<Result<T, TException>> CatchAsync<T, TException>(Func<Task<T>> action)
        where T : notnull
        where TException : Exception
    {
        try
        {
            return Result<T, TException>.Success(await action().ConfigureAwait(false));
        }
        catch (TException exception) when (exception is not OperationCanceledException)
        {
            return Result<T, TException>.Error(exception);
        }
    }
}
