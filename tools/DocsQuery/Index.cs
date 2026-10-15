using System.Text;
using System.Text.RegularExpressions;

namespace DocsQuery;

/// <summary>A document: its path, kind, title, the id it answers to (a record's number, a spike's or a package's name), and its metadata lines.</summary>
internal sealed record Doc(string Path, string Kind, string Title, string? Id, IReadOnlyList<Field> Meta, int LineCount)
{
    public string? Get(string key) => Meta.FirstOrDefault(field => field.Key == key)?.Value;
}

internal sealed record Field(string Key, string Value, int Line);

/// <summary>A heading and the text up to the next heading. <see cref="Trail"/> is the headings above it, the page's title first.</summary>
internal sealed record Section(int Doc, int Line, int EndLine, int Level, string Heading, string Anchor, IReadOnlyList<string> Trail)
{
    public string Path => string.Join(" > ", Trail.Append(Heading));
}

/// <summary>
/// A typed edge, from a line of a document to a file, an anchor or an id. Kinds: <c>link</c> (a
/// Markdown link or an absolute address into the repository), <c>superseded-by</c> (a record's
/// status), <c>mention</c> (an id named in prose, unlinked) and <c>code</c> (an id in a fenced block).
/// Only the first two are explicit; a transitive walk follows nothing else.
/// </summary>
internal sealed record Edge(int Doc, int Line, string Kind, string? TargetPath, string? TargetAnchor, string? TargetKey, string Written)
{
    public const string LinkKind = "link", SupersededBy = "superseded-by", MentionKind = "mention", CodeKind = "code";

    public bool IsExplicit => Kind is LinkKind or SupersededBy;

    public string Target =>
        TargetPath is null ? Ids.Display(TargetKey!)
      : (TargetAnchor is null ? TargetPath : $"{TargetPath}#{TargetAnchor}") + (TargetKey is null ? "" : $" ({Ids.Display(TargetKey)})");
}

/// <summary>
/// Something a consumer page claims: a C# block (<c>csharp</c>), a line of C# with the output its
/// comment states (<c>stated</c>), or another fenced block shown as output or input (<c>block:json</c>,
/// <c>block:sql</c>, …). <see cref="Id"/> is <c>{page}#{anchor}/{kind}-{n}</c>, stable while the
/// section and the order within it hold; <see cref="Hash"/> changes with the content.
/// </summary>
internal sealed record Claim(int Doc, int Line, string Kind, string Id, string Hash, string Text, string? Region, string? OptOut);

/// <summary>
/// Where an id is defined: a record's title, a heading that starts with a diagnostic id (<c>## CMTK0004</c>),
/// or a table row or list item that opens with one (<c>| CMTK1011 | … |</c>, <c>- CMTK1011: …</c>), in a ranked document.
/// </summary>
internal sealed record Definition(string Key, int Doc, int Line, bool IsHeading);

/// <summary>The index: every document, section, edge and claim, and the postings for ranking.</summary>
internal sealed class DocIndex
{
    public const int Schema = 1;

    public required string Fingerprint { get; init; }
    public required IReadOnlyList<Doc> Docs { get; init; }
    public required IReadOnlyList<Section> Sections { get; init; }
    public required IReadOnlyList<Edge> Edges { get; init; }
    public required IReadOnlyList<Claim> Claims { get; init; }
    public required IReadOnlyList<Definition> Definitions { get; init; }

    /// <summary>The search terms, in ordinal order, each with its postings: a section and its weighted term frequency.</summary>
    public required IReadOnlyList<string> Terms { get; init; }
    public required IReadOnlyList<IReadOnlyList<(int Section, float Frequency)>> Postings { get; init; }

    /// <summary>Each section's weighted length; zero for a section that is not ranked.</summary>
    public required IReadOnlyList<float> Lengths { get; init; }

    private Dictionary<string, int>? _docsByPath;
    private int[]? _firstSection;

    public Doc? DocAt(string path) => (_docsByPath ??= Docs.Select((doc, index) => (doc, index)).ToDictionary(pair => pair.doc.Path, pair => pair.index, StringComparer.Ordinal))
        .TryGetValue(path, out var index) ? Docs[index] : null;

    public int DocIndexOf(string path) => DocAt(path) is null ? -1 : _docsByPath![path];

    /// <summary>The sections of a document, in line order.</summary>
    public IEnumerable<int> SectionsOf(int doc)
    {
        if (_firstSection is null)
        {
            _firstSection = Enumerable.Repeat(-1, Docs.Count + 1).ToArray();
            for (var s = Sections.Count - 1; s >= 0; s--) _firstSection[Sections[s].Doc] = s;
        }

        for (var s = _firstSection[doc]; s >= 0 && s < Sections.Count && Sections[s].Doc == doc; s++) yield return s;
    }

    /// <summary>The section a line of a document is in.</summary>
    public int SectionAt(int doc, int line) => SectionsOf(doc).LastOrDefault(s => Sections[s].Line <= line, SectionsOf(doc).First());

    /// <summary>The last line of a section with everything below it: up to the next heading at its level or above.</summary>
    public int SubtreeEnd(int section)
    {
        var start = Sections[section];
        var next  = SectionsOf(start.Doc).FirstOrDefault(s => s > section && Sections[s].Level <= start.Level && Sections[s].Level > 0, -1);
        return next < 0 ? Docs[start.Doc].LineCount : Sections[next].Line - 1;
    }

    /// <summary>The definitions of an id, headings first.</summary>
    public IEnumerable<Definition> DefinitionsOf(string key) =>
        Definitions.Where(definition => definition.Key == key).OrderBy(definition => definition.IsHeading ? 0 : 1)
                   .ThenBy(definition => Docs[definition.Doc].Path, StringComparer.Ordinal).ThenBy(definition => definition.Line);
}

/// <summary>Builds an index from the documents' and samples' contents.</summary>
internal static partial class IndexBuilder
{
    private const float HeadingWeight = 3f, TrailWeight = 1f, BodyWeight = 1f, CodeWeight = 0.5f;

    public static DocIndex Build(string fingerprint, IReadOnlyList<(string Path, string Text)> documents, IReadOnlyList<(string Path, string Text)> samples)
    {
        var regions  = SampleRegions.Read(samples);
        var docs     = new List<Doc>();
        var sections = new List<Section>();
        var edges    = new List<Edge>();
        var claims   = new List<Claim>();
        var defined  = new List<Definition>();
        var pages    = new List<Page>();

        foreach (var (path, text) in documents.OrderBy(document => document.Path, StringComparer.Ordinal))
        {
            var page = new Page(path, text);
            var kind = Kind.Of(path);
            var meta = page.Metadata().Select(field => new Field(field.Key, field.Value, field.Line)).ToList();
            var id   = kind switch
            {
                Kind.Record  => Ids.Display(Ids.RecordKey(Ids.RecordFile(System.IO.Path.GetFileName(path))!.Value)),
                Kind.Spike   => path.Split('/')[1],
                Kind.Package => path.Split('/')[1],
                Kind.Guide   => System.IO.Path.GetFileNameWithoutExtension(path),
                _            => null,
            };

            docs.Add(new Doc(path, kind, page.Title, id, meta, page.Lines.Length));
            pages.Add(page);
        }

        for (var d = 0; d < docs.Count; d++)
        {
            var page  = pages[d];
            var first = sections.Count;
            AddSections(d, page, sections);
            var own = AddDefinitions(d, docs[d], page, defined);
            AddEdges(d, docs[d], page, own, edges);
            if (Kind.HasClaims(docs[d].Kind)) AddClaims(d, docs[d], page, sections, first, regions, claims);
        }

        var (terms, postings, lengths) = Postings(docs, pages, sections);

        return new DocIndex
        {
            Fingerprint = fingerprint, Docs = docs, Sections = sections, Edges = edges, Claims = claims, Definitions = defined,
            Terms = terms, Postings = postings, Lengths = lengths,
        };
    }

    private static void AddSections(int doc, Page page, List<Section> sections)
    {
        var headings = page.Headings;

        // Text above the first heading is a section of its own, named after the file.
        if (headings.Count == 0 || headings[0].Line > 1 && Enumerable.Range(1, headings[0].Line - 1).Any(line => !string.IsNullOrWhiteSpace(page.Line(line))))
            sections.Add(new Section(doc, 1, headings.Count == 0 ? page.Lines.Length : headings[0].Line - 1, 0, System.IO.Path.GetFileName(page.Path), "", []));

        var trail = new List<Heading>();

        for (var h = 0; h < headings.Count; h++)
        {
            var heading = headings[h];
            trail.RemoveAll(above => above.Level >= heading.Level);

            var end = h + 1 < headings.Count ? headings[h + 1].Line - 1 : page.Lines.Length;
            sections.Add(new Section(doc, heading.Line, end, heading.Level, heading.Text, heading.Anchor, [.. trail.Select(above => above.Text)]));
            trail.Add(heading);
        }
    }

    /// <summary>The ids this document defines, added to <paramref name="all"/>; returned as the lines (and, for a heading, the section below it) each covers.</summary>
    private static List<(string Key, int From, int To)> AddDefinitions(int doc, Doc document, Page page, List<Definition> all)
    {
        var own = new List<(string Key, int From, int To)>();

        if (document.Kind == Kind.Record)
        {
            var key = Ids.RecordKey(int.Parse(document.Id!, System.Globalization.CultureInfo.InvariantCulture));
            all.Add(new Definition(key, doc, page.Headings.FirstOrDefault()?.Line ?? 1, true));
            own.Add((key, 1, page.Lines.Length));
            return own;
        }

        if (!Kind.IsRanked(document.Kind)) return own;

        for (var h = 0; h < page.Headings.Count; h++)
        {
            if (Ids.DiagnosticPattern().Match(page.Headings[h].Text) is not { Success: true, Index: 0 } match) continue;

            var key = Ids.DiagnosticKey(match.Value);
            var end = page.Headings.Skip(h + 1).FirstOrDefault(next => next.Level <= page.Headings[h].Level)?.Line - 1 ?? page.Lines.Length;
            all.Add(new Definition(key, doc, page.Headings[h].Line, true));
            own.Add((key, page.Headings[h].Line, end));
        }

        for (var line = 1; line <= page.Lines.Length; line++)
        {
            if (page.InFence[line] || DefiningRow().Match(page.Line(line)) is not { Success: true } row) continue;

            var key = Ids.DiagnosticKey(row.Groups["id"].Value);
            all.Add(new Definition(key, doc, line, false));
            own.Add((key, line, line));
        }

        return own;
    }

    private static void AddEdges(int doc, Doc document, Page page, List<(string Key, int From, int To)> own, List<Edge> all)
    {
        var edges       = new List<Edge>();
        var linksByLine = page.Links.GroupBy(link => link.Line).ToDictionary(group => group.Key, group => group.ToList());

        foreach (var link in page.Links)
        {
            var (path, anchor) = Resolve(page.Path, link.Target);
            edges.Add(new Edge(doc, link.Line, Edge.LinkKind, path, anchor, KeyOf(path, anchor), link.Target));
        }

        for (var line = 1; line <= page.Lines.Length; line++)
        {
            var text = page.Line(line);

            foreach (var mention in Ids.Mentions(text).OrderBy(mention => mention.Index))
            {
                // An id named where it is defined is no edge: a record naming itself, a diagnostic inside its own section or row.
                if (own.Any(definition => definition.Key == mention.Key && definition.From <= line && line <= definition.To)) continue;

                if (!page.InFence[line] && linksByLine.TryGetValue(line, out var links)
                    && links.Any(link => link.Column <= mention.Index && link.Column + link.Length >= mention.Index + mention.Length
                                      && Resolve(page.Path, link.Target) is var (path, anchor) && KeyOf(path, anchor) == mention.Key))
                    continue;

                edges.Add(new Edge(doc, line, page.InFence[line] ? Edge.CodeKind : Edge.MentionKind, null, null, mention.Key, text.Substring(mention.Index, mention.Length)));
            }
        }

        if (document.Kind == Kind.Record && document.Meta.FirstOrDefault(field => field.Key == "status") is { } status
            && SupersededBy().Match(status.Value) is { Success: true } superseded)
        {
            var key = Ids.RecordKey(int.Parse(superseded.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture));
            edges.Add(new Edge(doc, status.Line, Edge.SupersededBy, null, null, key, status.Value));
        }

        // A repeat on one line is one edge.
        all.AddRange(edges.OrderBy(edge => edge.Line).DistinctBy(edge => (edge.Line, edge.Kind, edge.TargetPath, edge.TargetAnchor, edge.TargetKey)));
    }

    private static int SectionOf(List<Section> sections, int first, int doc, int line)
    {
        var found = first;
        for (var s = first; s < sections.Count && sections[s].Doc == doc; s++)
            if (sections[s].Line <= line) found = s;
        return found;
    }

    /// <summary>The id a link's target stands for: a record's file, or a heading anchor that is a diagnostic id.</summary>
    public static string? KeyOf(string? path, string? anchor)
    {
        if (path is not null && path.StartsWith("design/adr/", StringComparison.Ordinal) && Ids.RecordFile(System.IO.Path.GetFileName(path)) is { } number)
            return Ids.RecordKey(number);

        return anchor is not null && Ids.DiagnosticPattern().Match(anchor) is { Success: true } match && match.Length == anchor.Length ? Ids.DiagnosticKey(anchor) : null;
    }

    /// <summary>A link's target as a path relative to the root and an anchor; an in-page anchor resolves to the page itself.</summary>
    public static (string Path, string? Anchor) Resolve(string page, string target)
    {
        if (target.StartsWith(Page.RepositoryAddress, StringComparison.Ordinal))
        {
            var match = Page.RepositoryFile().Match(target);
            var (absolutePath, absoluteAnchor) = Split(match.Groups["path"].Value);
            return (absolutePath.TrimEnd('/'), absoluteAnchor);
        }

        var (path, anchor) = Split(target);
        if (path.Length == 0) return (page, anchor);

        var directory = page.Contains('/') ? page[..page.LastIndexOf('/')] : "";
        return (Normalize(directory.Length == 0 ? path : $"{directory}/{path}"), anchor);

        static (string Path, string? Anchor) Split(string target)
        {
            var parts = target.Split('#', 2);
            return (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null);
        }
    }

    /// <summary>A relative path with <c>.</c> and <c>..</c> resolved, without touching the file system.</summary>
    public static string Normalize(string path)
    {
        var parts = new List<string>();

        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == ".." ) { if (parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1); else parts.Add(".."); }
            else parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static void AddClaims(int doc, Doc document, Page page, List<Section> sections, int firstSection, SampleRegions regions, List<Claim> claims)
    {
        var prefix   = document.Kind == Kind.Readme ? "README" : document.Kind == Kind.Package ? document.Id! : document.Id!;
        var ordinals = new Dictionary<(int Section, string Kind), int>();

        string NextId(int section, string kind)
        {
            var n = ordinals[(section, kind)] = ordinals.GetValueOrDefault((section, kind)) + 1;
            var anchor = sections[section].Anchor;
            return $"{prefix}#{anchor}/{kind}-{n}";
        }

        foreach (var fence in page.Fences)
        {
            var section = SectionOf(sections, firstSection, doc, fence.Line);
            var code    = SampleRegions.Normalize(fence.Body);

            if (!fence.IsCSharp)
            {
                var kind = "block:" + (fence.Language.Length == 0 ? "text" : fence.Language.ToLowerInvariant());
                claims.Add(new Claim(doc, fence.Line, kind, NextId(section, kind), ShortHash(code), code, null, fence.OptOut));
                continue;
            }

            var region = regions.Find(prefix, code);
            claims.Add(new Claim(doc, fence.Line, "csharp", NextId(section, "csharp"), ShortHash(code), code, region, fence.OptOut));

            for (var i = 0; i < fence.Body.Count; i++)
            {
                var (statement, comment) = SampleRegions.SplitComment(fence.Body[i]);
                if (statement.Length == 0) continue;

                if (comment is null)
                {
                    if (!statement.EndsWith(';')) continue;

                    var below = fence.Body.Skip(i + 1).TakeWhile(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                                     .Select(line => line.TrimStart()[2..].Trim()).ToList();
                    if (below.Count == 0) continue;
                    comment = string.Join(' ', below);
                }

                var text = $"{statement} // {comment}";
                claims.Add(new Claim(doc, fence.Line + 1 + i, "stated", NextId(section, "stated"), ShortHash(text), text, region, fence.OptOut));
            }
        }
    }

    private static string ShortHash(string text) => Corpus.Hash(Encoding.UTF8.GetBytes(text))[..12].ToLowerInvariant();

    private static (List<string>, List<IReadOnlyList<(int, float)>>, List<float>) Postings(List<Doc> docs, List<Page> pages, List<Section> sections)
    {
        var frequencies = new Dictionary<string, Dictionary<int, float>>(StringComparer.Ordinal);
        var lengths     = new List<float>(sections.Count);

        for (var s = 0; s < sections.Count; s++)
        {
            var section = sections[s];
            var doc     = docs[section.Doc];

            if (!Kind.IsRanked(doc.Kind)) { lengths.Add(0); continue; }

            var page   = pages[section.Doc];
            var length = 0f;

            void Count(string text, float weight)
            {
                foreach (var term in Tokenizer.Terms(text))
                {
                    if (!frequencies.TryGetValue(term, out var postings)) frequencies[term] = postings = [];
                    postings[s] = postings.GetValueOrDefault(s) + weight;
                    length += weight;
                }
            }

            Count(section.Heading, HeadingWeight);
            Count(string.Join(' ', section.Trail.Prepend(doc.Kind == Kind.Record ? $"decision {doc.Id}" : "").Distinct()), TrailWeight);

            var bodyStart = section.Level == 0 ? section.Line : section.Line + 1;
            for (var line = bodyStart; line <= section.EndLine; line++)
                Count(page.InFence[line] ? page.Line(line) : Page.Readable(page.Line(line)), page.InFence[line] ? CodeWeight : BodyWeight);

            lengths.Add(length);
        }

        var terms = frequencies.Keys.Order(StringComparer.Ordinal).ToList();
        return (terms, [.. terms.Select(term => (IReadOnlyList<(int, float)>)[.. frequencies[term].OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value))])], lengths);
    }

    [GeneratedRegex(@"^\s*(?:\|\s*|[-*]\s+)`?(?<id>CMTK\d{4})`?\s*(?:\||:)")]
    private static partial Regex DefiningRow();

    [GeneratedRegex(@"superseded by (?<number>\d{1,4})", RegexOptions.IgnoreCase)]
    private static partial Regex SupersededBy();
}

/// <summary>
/// The regions of the samples project that compile the pages' C# blocks, as
/// <c>// sample: {page}/{name}</c> … <c>// end sample</c>, parts of one name joined in file order and
/// each dedented, as the convention test that holds a block to its region reads them.
/// </summary>
internal sealed partial class SampleRegions
{
    private readonly Dictionary<string, List<(string Name, string Location)>> _byCode = new(StringComparer.Ordinal);

    public static SampleRegions Read(IReadOnlyList<(string Path, string Text)> files)
    {
        var regions = new SampleRegions();

        foreach (var (path, text) in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            var lines = text.ReplaceLineEndings("\n").Split('\n');
            var parts = new List<(string Name, int Line, List<string> Lines)>();
            (string Name, int Line, List<string> Lines)? open = null;

            for (var i = 0; i < lines.Length; i++)
            {
                if (RegionStart().Match(lines[i]) is { Success: true } start) open = (start.Groups["name"].Value, i + 1, []);
                else if (RegionEnd().IsMatch(lines[i])) { if (open is { } part) parts.Add(part); open = null; }
                else open?.Lines.Add(lines[i]);
            }

            foreach (var sample in parts.GroupBy(part => part.Name, StringComparer.Ordinal))
            {
                var code = Trim(sample.SelectMany(part => Dedent(part.Lines)));
                if (!regions._byCode.TryGetValue(code, out var list)) regions._byCode[code] = list = [];
                list.Add((sample.Key, $"{path}:{sample.First().Line}"));
            }
        }

        return regions;
    }

    /// <summary>The region compiling a block of the page with this prefix, as <c>name @ file:line</c>, or null.</summary>
    public string? Find(string prefix, string code) =>
        _byCode.TryGetValue(code, out var list) && list.FirstOrDefault(region => region.Name.StartsWith(prefix + "/", StringComparison.Ordinal)) is { Name: not null } found
            ? $"{found.Name} @ {found.Location}"
            : null;

    public static string Normalize(IEnumerable<string> lines) => Trim(Dedent(lines));

    /// <summary>A line of C# split at its <c>//</c> comment, outside any string literal.</summary>
    public static (string Code, string? Comment) SplitComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (inString && line[i] == '\\') i++;
            else if (line[i] == '"') inString = !inString;
            else if (!inString && line[i] == '/' && line[i + 1] == '/') return (line[..i].Trim(), line[(i + 2)..].Trim());
        }

        return (line.Trim(), null);
    }

    private static IEnumerable<string> Dedent(IEnumerable<string> lines)
    {
        var trimmed = lines.Select(line => line.TrimEnd()).ToArray();
        var indent  = trimmed.Where(line => line.Length > 0 && !line.TrimStart().StartsWith('#'))
                             .Select(line => line.Length - line.TrimStart().Length)
                             .DefaultIfEmpty(0)
                             .Min();

        return trimmed.Select(line => line.Length == 0 ? line : line.TrimStart().StartsWith('#') ? line.TrimStart() : line[indent..]);
    }

    private static string Trim(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        while (list.Count > 0 && list[0].Length == 0) list.RemoveAt(0);
        while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
        return string.Join('\n', list);
    }

    [GeneratedRegex(@"^\s*// sample: (?<name>[^\s/]+/[^\s/]+)\s*$")]
    private static partial Regex RegionStart();

    [GeneratedRegex(@"^\s*// end sample\s*$")]
    private static partial Regex RegionEnd();
}
