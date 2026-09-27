using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.TypeKit;

internal static class ThrowHelper
{
    private const string UninitializedResult =
        "This Result was never initialized: it is a default instance, such as an array element or an unassigned field, "
      + "and is neither a success nor an error. Create results with Success/Error, Result.Ok/Result.Error, "
      + "or an implicit conversion.";

    /// <summary>
    /// A <see cref="ResultState.Uninitialized"/> result is a bug at the site that produced it. Picking
    /// either branch would hand the caller a plausible answer, and for the error branch a fabricated
    /// <c>default(TError)</c>.
    /// </summary>
    [DoesNotReturn]
    public static T ThrowUninitializedResult<T>() => throw new InvalidOperationException(UninitializedResult);
}
