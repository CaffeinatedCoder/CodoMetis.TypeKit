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
}
