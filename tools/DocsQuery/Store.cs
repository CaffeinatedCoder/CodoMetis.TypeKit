using System.Text;

namespace DocsQuery;

/// <summary>
/// The one cache file: the index, written after its fingerprint, rebuilt whenever the fingerprint
/// changes, into a temporary file renamed over the old one. A reader sees the old index or the new,
/// never half of one. The file is disposable; deleting it costs one rebuild.
/// </summary>
internal static class Store
{
    public const string CacheFile = ".docs-query.cache";

    private const string Magic = "docs-query index";

    /// <summary>What a load did: read the cache, or rebuilt it (and whether the write succeeded).</summary>
    internal enum Outcome { Read, Rebuilt, RebuiltNotWritten }

    public static (DocIndex Index, Outcome Outcome) Load(string root, string toolVersion, string? cachePath = null)
    {
        cachePath ??= Path.Combine(root, CacheFile);

        var documents = Corpus.Documents(root).Select(path => (Path: path, Content: File.ReadAllBytes(Path.Combine(root, path)))).ToList();
        var samples   = Corpus.Samples(root).Select(path => (Path: path, Content: File.ReadAllBytes(Path.Combine(root, path)))).ToList();

        var fingerprint = Corpus.Fingerprint(DocIndex.Schema, toolVersion, documents.Concat(samples));

        if (TryRead(cachePath, fingerprint) is { } cached) return (cached, Outcome.Read);

        var index = IndexBuilder.Build(fingerprint, [.. documents.Select(Text)], [.. samples.Select(Text)]);

        try
        {
            var temporary = $"{cachePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            using (var stream = File.Create(temporary)) Write(index, stream);
            File.Move(temporary, cachePath, overwrite: true);
            return (index, Outcome.Rebuilt);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"docs-query: the index was rebuilt but not written to {cachePath}: {exception.Message}");
            return (index, Outcome.RebuiltNotWritten);
        }

        static (string, string) Text((string Path, byte[] Content) input) => (input.Path, new UTF8Encoding(false).GetString(input.Content).TrimStart('﻿'));
    }

    /// <summary>The cached index when its fingerprint is this one; null when it is missing, another, or unreadable.</summary>
    private static DocIndex? TryRead(string path, string fingerprint)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            if (reader.ReadString() != Magic || reader.ReadString() != fingerprint) return null;
            return Read(reader, fingerprint);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    public static void Write(DocIndex index, Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(index.Fingerprint);

        WriteList(writer, index.Docs, doc =>
        {
            writer.Write(doc.Path); writer.Write(doc.Kind); writer.Write(doc.Title); WriteNullable(writer, doc.Id);
            WriteList(writer, doc.Meta, field => { writer.Write(field.Key); writer.Write(field.Value); writer.Write(field.Line); });
            writer.Write(doc.LineCount);
        });

        WriteList(writer, index.Sections, section =>
        {
            writer.Write(section.Doc); writer.Write(section.Line); writer.Write(section.EndLine); writer.Write(section.Level);
            writer.Write(section.Heading); writer.Write(section.Anchor);
            WriteList(writer, section.Trail, writer.Write);
        });

        WriteList(writer, index.Edges, edge =>
        {
            writer.Write(edge.Doc); writer.Write(edge.Line); writer.Write(edge.Kind);
            WriteNullable(writer, edge.TargetPath); WriteNullable(writer, edge.TargetAnchor); WriteNullable(writer, edge.TargetKey); writer.Write(edge.Written);
        });

        WriteList(writer, index.Claims, claim =>
        {
            writer.Write(claim.Doc); writer.Write(claim.Line); writer.Write(claim.Kind); writer.Write(claim.Id); writer.Write(claim.Hash);
            writer.Write(claim.Text); WriteNullable(writer, claim.Region); WriteNullable(writer, claim.OptOut);
        });

        WriteList(writer, index.Definitions, definition => { writer.Write(definition.Key); writer.Write(definition.Doc); writer.Write(definition.Line); writer.Write(definition.IsHeading); });
        WriteList(writer, index.Terms, writer.Write);
        WriteList(writer, index.Postings, postings => WriteList(writer, postings, posting => { writer.Write(posting.Section); writer.Write(posting.Frequency); }));
        WriteList(writer, index.Lengths, writer.Write);
    }

    private static DocIndex Read(BinaryReader reader, string fingerprint) => new()
    {
        Fingerprint = fingerprint,
        // Arguments are evaluated left to right, so each constructor reads its fields in the order Write wrote them.
        Docs = ReadList(reader, () => new Doc(reader.ReadString(), reader.ReadString(), reader.ReadString(), ReadNullable(reader),
            ReadList(reader, () => new Field(reader.ReadString(), reader.ReadString(), reader.ReadInt32())), reader.ReadInt32())),
        Sections = ReadList(reader, () => new Section(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), reader.ReadString(), ReadList(reader, reader.ReadString))),
        Edges    = ReadList(reader, () => new Edge(reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), ReadNullable(reader), ReadNullable(reader), ReadNullable(reader), reader.ReadString())),
        Claims   = ReadList(reader, () => new Claim(reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadString(), ReadNullable(reader), ReadNullable(reader))),
        Definitions = ReadList(reader, () => new Definition(reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean())),
        Terms    = ReadList(reader, reader.ReadString),
        Postings = ReadList(reader, () => (IReadOnlyList<(int, float)>)ReadList(reader, () => (reader.ReadInt32(), reader.ReadSingle()))),
        Lengths  = ReadList(reader, reader.ReadSingle),
    };

    private static void WriteList<T>(BinaryWriter writer, IReadOnlyList<T> items, Action<T> write)
    {
        writer.Write(items.Count);
        foreach (var item in items) write(item);
    }

    private static List<T> ReadList<T>(BinaryReader reader, Func<T> read)
    {
        var count = reader.ReadInt32();
        if (count < 0) throw new FormatException("A negative count.");

        var items = new List<T>(count);
        for (var i = 0; i < count; i++) items.Add(read());
        return items;
    }

    private static void WriteNullable(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null) writer.Write(value);
    }

    private static string? ReadNullable(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;
}
