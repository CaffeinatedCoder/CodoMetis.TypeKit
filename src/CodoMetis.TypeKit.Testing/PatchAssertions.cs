namespace CodoMetis.TypeKit.Testing;

/// <summary>Assertions on a <see cref="Patch{T}"/>: which of its three states a field is in.</summary>
/// <remarks>
/// <see cref="ShouldBeValue{T}(Patch{T})"/> hands the value back, so the test compares it with the
/// assertions it already uses. A failure never prints the value, as <see cref="Patch{T}"/> never does.
/// </remarks>
public static class PatchAssertions
{
    /// <summary>The value of a patch whose field must have carried one.</summary>
    /// <param name="patch">The patch under test.</param>
    /// <returns>The value.</returns>
    /// <exception cref="TypeKitAssertionException">The field was null or not mentioned.</exception>
    public static T ShouldBeValue<T>(this Patch<T> patch) where T : notnull =>
        patch.TryGetValue(out var value)
            ? value
            : throw new TypeKitAssertionException($"Expected a value, but the {Outcomes.Name<Patch<T>>()} is {Describe(patch.State)}.");

    /// <summary>Fails unless the field was null: a clear.</summary>
    /// <param name="patch">The patch under test.</param>
    /// <exception cref="TypeKitAssertionException">The field carried a value, which is not printed, or was not mentioned.</exception>
    public static void ShouldBeNull<T>(this Patch<T> patch) where T : notnull
    {
        if (patch.State != PatchState.Null)
            throw new TypeKitAssertionException($"Expected Null, a clear, but the {Outcomes.Name<Patch<T>>()} {Was(patch.State)}.");
    }

    /// <summary>Fails unless the field was not mentioned.</summary>
    /// <param name="patch">The patch under test.</param>
    /// <exception cref="TypeKitAssertionException">The field carried a value, which is not printed, or was null.</exception>
    public static void ShouldBeAbsent<T>(this Patch<T> patch) where T : notnull
    {
        if (patch.State != PatchState.Absent)
            throw new TypeKitAssertionException($"Expected Absent, but the {Outcomes.Name<Patch<T>>()} {Was(patch.State)}.");
    }

    private static string Was(PatchState state) => state == PatchState.Value ? "holds a value (not printed)" : $"is {Describe(state)}";

    private static string Describe(PatchState state) => state switch
    {
        PatchState.Null   => "Null: the field was cleared",
        PatchState.Absent => "Absent: the field was not mentioned",
        _                 => "a value",
    };
}
