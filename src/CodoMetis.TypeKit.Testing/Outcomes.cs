namespace CodoMetis.TypeKit.Testing;

/// <summary>What the assertions share: how an outcome is named, and the two failures every shape has.</summary>
internal static class Outcomes
{
    public static void ThrowIfUninitialized<TOutcome>(ResultState state, string expected)
    {
        if (state == ResultState.Uninitialized)
            throw new TypeKitAssertionException(
                $"Expected {expected}, but the {Name<TOutcome>()} is uninitialized: a default instance, neither a success nor an error.");
    }

    public static void ThrowIfOtherError<TOutcome, TError>(TError actual, TError expected)
    {
        if (!EqualityComparer<TError>.Default.Equals(actual, expected))
            throw new TypeKitAssertionException($"Expected the error {expected}, but the {Name<TOutcome>()} is the error {actual}.");
    }

    /// <summary><c>Result&lt;Order, OrderFault&gt;</c>, as the type is written in C#.</summary>
    public static string Name<T>() => Name(typeof(T));

    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;
}
