using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.TypeKit;

internal static class ThrowHelper
{
    private const string UninitializedResult =
        "This Result was never initialized: it is a default instance, such as an array element or an unassigned field, "
      + "and is neither a success nor an error. Create results with Result.Success/Result.Error, "
      + "the Success/Error factories or an implicit conversion." + Refusals.UninitializedResult;

    private const string NullContentMessage =
        "An Option or a Result never holds null: an option that reports a value always has one, and a result's value "
      + "and error are never null. ToOption() turns a null into None." + Refusals.NullContent;

    private const string NullPatchValueMessage =
        "A patch never holds null as its value: a clear is the Null state, Patch.Null(), and a value is never null. "
      + "Map's selector returned null for a value." + Refusals.NullContent;

    /// <summary>
    /// A <see cref="ResultState.Uninitialized"/> result is a bug at the site that produced it. Picking
    /// either branch would hand the caller a plausible answer, and for the error branch a fabricated
    /// <c>default(TError)</c>.
    /// </summary>
    [DoesNotReturn]
    public static T ThrowUninitializedResult<T>() => throw new InvalidOperationException(UninitializedResult);

    /// <summary>
    /// What <c>Option.Some</c>, the <c>Result</c> factories and every member that takes a result's
    /// error throw for a null: <c>notnull</c> is an annotation the runtime does not enforce.
    /// </summary>
    /// <param name="paramName">The parameter that was null.</param>
    /// <returns>The exception, to throw.</returns>
    public static ArgumentNullException NullContent(string paramName) => new(paramName, NullContentMessage);

    private const string NullTaskMessage =
        "The delegate given to CatchAsync returned no task: a null cannot be awaited, and it is not an exception the call threw. "
      + "Return the task, or make the lambda async." + Refusals.NullContent;

    /// <summary>What <c>CatchAsync</c> throws for a delegate that returned a null task, before anything is awaited.</summary>
    /// <param name="paramName">The delegate's parameter.</param>
    /// <returns>The exception, to throw.</returns>
    public static ArgumentNullException NullTask(string paramName) => new(paramName, NullTaskMessage);

    /// <summary>
    /// What an asynchronous combinator throws for a delegate that returned a null task, as
    /// <c>CatchAsync</c> does: awaited, it was a <see cref="NullReferenceException"/> naming nothing.
    /// </summary>
    /// <param name="task">What the delegate returned.</param>
    /// <param name="method">The combinator, named in the message.</param>
    /// <param name="paramName">The delegate's parameter.</param>
    /// <returns>The task, never null.</returns>
    public static Task<T> Awaitable<T>(Task<T>? task, string method, string paramName) => task ?? throw NullTaskFrom(method, paramName);

    /// <inheritdoc cref="Awaitable{T}(Task{T}?, string, string)"/>
    public static Task Awaitable(Task? task, string method, string paramName) => task ?? throw NullTaskFrom(method, paramName);

    private static ArgumentNullException NullTaskFrom(string method, string paramName) =>
        new(paramName, $"The delegate given to {method} returned no task: a null cannot be awaited. Return the task, or make the lambda async.{Refusals.NullContent}");

    /// <summary>What <c>Patch&lt;T&gt;.Map</c> throws for a selector that returned null.</summary>
    /// <param name="paramName">The selector's parameter.</param>
    /// <returns>The exception, to throw.</returns>
    public static ArgumentNullException NullPatchValue(string paramName) => new(paramName, NullPatchValueMessage);
}
