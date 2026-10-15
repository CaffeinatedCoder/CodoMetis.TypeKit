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

    /// <summary>
    /// <c>Result&lt;Order, OrderFault&gt;</c>, as the type is written in C# from outside its containing
    /// types: <c>Billing.Fault</c>, <c>Outer&lt;Int32&gt;.Inner</c>.
    /// </summary>
    public static string Name<T>() => Name(typeof(T));

    /// <remarks>
    /// A nested type's generic arguments are its containing types' and then its own, in one list, and a
    /// type nested in a generic one is generic without an arity of its own (<c>Inner</c>, not
    /// <c>Inner`1</c>): each name in the chain takes as many arguments as its own arity says.
    /// </remarks>
    public static string Name(Type type)
    {
        if (type.IsArray)
            return $"{Name(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";

        if (type.IsGenericParameter)
            return type.Name;

        var arguments = type.IsGenericType ? type.GetGenericArguments() : [];
        var chain = new List<Type>();
        for (var link = type; link is not null; link = link.DeclaringType)
            chain.Insert(0, link);

        var taken = 0;
        return string.Join(".", chain.Select(link =>
        {
            var tick = link.Name.IndexOf('`', StringComparison.Ordinal);
            if (tick < 0) return link.Name;

            var arity = int.Parse(link.Name.AsSpan(tick + 1), System.Globalization.CultureInfo.InvariantCulture);
            var own = arguments.Skip(taken).Take(arity).Select(Name);
            taken += arity;
            return $"{link.Name[..tick]}<{string.Join(", ", own)}>";
        }));
    }
}
