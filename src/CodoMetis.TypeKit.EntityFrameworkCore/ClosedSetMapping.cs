using System.Collections.Concurrent;
using System.Reflection;
using CodoMetis.TypeKit.ClosedSets;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// A closed set as a column: its member's name, read back with the set's own <c>TryParse</c>, and a
/// JSON reader/writer (primitive collections, JSON columns) over the string's. Made per set through
/// <see cref="GeneratedClosedSetAttribute.Accept{TResult}"/>, never by <c>MakeGenericType</c>, so it
/// works under Native AOT as the value-object mappings do.
/// </summary>
/// <remarks>
/// As for a value object (<see cref="ValueObjectMapping"/>), the converter is a plain
/// <see cref="ValueConverter{TModel,TProvider}"/> over the expressions of
/// <see cref="ClosedSetConverter{TSet}"/>, whose public methods the compiled model's code can call, and
/// the JSON reader/writer is composed here, with both types known, where EF's own composition uses
/// <c>MakeGenericType</c>.
/// </remarks>
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

    /// <summary>The set's reader/writer over the string's, or <see langword="null"/> where the text mapping has none.</summary>
    protected abstract JsonValueReaderWriter? ReaderWriterOver(JsonValueReaderWriter? text);

    public RelationalTypeMapping ComposeOnto(RelationalTypeMapping text) =>
        (RelationalTypeMapping)text.WithComposedConverter(Converter, jsonValueReaderWriter: ReaderWriterOver(text.JsonValueReaderWriter));

    private sealed class Of<TSet> : ClosedSetMapping where TSet : class, IClosedSet<TSet>
    {
        public override ValueConverter Converter { get; } = new ValueConverter<TSet, string>(ClosedSetConverter<TSet>.ToProvider, ClosedSetConverter<TSet>.FromProvider);

        protected override JsonValueReaderWriter? ReaderWriterOver(JsonValueReaderWriter? text) =>
            text is JsonValueReaderWriter<string> typed ? new JsonConvertedValueReaderWriter<TSet, string>(typed, Converter) : null;
    }

    private sealed class Factory : IClosedSetVisitor<ClosedSetMapping>
    {
        public static readonly Factory Instance = new();

        public ClosedSetMapping Visit<TSet>() where TSet : class, IClosedSet<TSet> => new Of<TSet>();
    }
}
