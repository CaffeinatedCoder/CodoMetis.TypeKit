using System.Diagnostics.CodeAnalysis;

namespace Spike.Generators;

/// <summary>A closed set: every instance is a named public static readonly field of the type, and the generators derive the rest.</summary>
public interface IClosedSet<TSelf> where TSelf : class, IClosedSet<TSelf>
{
    string Name { get; }

    string QualifiedName { get; }

    static abstract IReadOnlyList<TSelf> All { get; }

    static abstract bool TryParse(string name, [MaybeNullWhen(false)] out TSelf member);
}
