using System.Collections.Concurrent;
using System.Reflection;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// A closed set as a column: its member's name, read back with the set's own <c>TryParse</c>.
/// Made per set through <see cref="GeneratedClosedSetAttribute.Accept{TResult}"/>, never by
/// <c>MakeGenericType</c>, so it works under Native AOT as the value-object mappings do.
/// </summary>
internal abstract class ClosedSetMapping
{
    private static readonly ConcurrentDictionary<Type, GeneratedClosedSetAttribute?> Descriptions = new();
    private static readonly ConcurrentDictionary<Type, ClosedSetMapping> Mappings = new();

    /// <summary>The set's attribute, or null for any other type.</summary>
    public static GeneratedClosedSetAttribute? Describe(Type type) =>
        Descriptions.GetOrAdd(type, static candidate =>
            candidate.GetCustomAttribute<GeneratedClosedSetAttribute>(inherit: false) is { } set && set.SetType == candidate ? set : null);

    public static ClosedSetMapping For(GeneratedClosedSetAttribute set) =>
        Mappings.GetOrAdd(set.SetType, static (_, description) => description.Accept(Factory.Instance), set);

    public abstract ValueConverter Converter { get; }

    public RelationalTypeMapping ComposeOnto(RelationalTypeMapping text) =>
        (RelationalTypeMapping)text.WithComposedConverter(Converter, jsonValueReaderWriter: null);

    private sealed class Of<TSet> : ClosedSetMapping where TSet : class, IClosedSet<TSet>
    {
        // Plain static methods: an expression tree can call neither a static abstract member nor an out variable.
        public override ValueConverter Converter { get; } = new ValueConverter<TSet, string>(member => NameOf(member), name => MemberOf(name));

        private static string NameOf(TSet member) => member.Name;

        private static TSet MemberOf(string name) =>
            TSet.TryParse(name, out var member)
                ? member
                : throw new InvalidOperationException($"{typeof(TSet).Name} has no member of that name: the column holds a name the set no longer has.");
    }

    private sealed class Factory : IClosedSetVisitor<ClosedSetMapping>
    {
        public static readonly Factory Instance = new();

        public ClosedSetMapping Visit<TSet>() where TSet : class, IClosedSet<TSet> => new Of<TSet>();
    }
}
