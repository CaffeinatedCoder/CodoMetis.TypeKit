using System.Globalization;

namespace DocsQuery;

/// <summary>
/// The commands. Every exact command answers from the index alone and says so; <see cref="Find"/> is
/// the only ranked one, and its summary says that a miss proves nothing.
/// </summary>
internal sealed class Queries(DocIndex index, string root)
{
    /// <summary>
    /// What an id, path, anchor or heading resolved to: the key it stands for, the document and section
    /// (-1 for none), the lines it covers, and whether a link to it is a link to that document (and anchor).
    /// </summary>
    private sealed record Scope(string? Key, int Doc, int Section, int From, int To, bool ByPath);

    public Answer Entry(string query)
    {
        var rows = new List<Row>();
        var key  = Ids.Parse(query.Trim());

        if (key is not null)
        {
            foreach (var definition in index.DefinitionsOf(key))
            {
                var doc = index.Docs[definition.Doc];
                rows.Add(Kind.IsEntry(doc.Kind) ? DocRow(definition.Doc)
                       : definition.IsHeading ? SectionRow(index.SectionAt(definition.Doc, definition.Line), "definition")
                       : Row.Of(("at", At(definition.Doc, definition.Line)), ("kind", "definition"), ("id", Ids.Display(key)), ("detail", index.Sections[index.SectionAt(definition.Doc, definition.Line)].Path)));
            }

            // An index row resolves a lookup: it is shown, labelled, after what it describes.
            rows.AddRange(index.Edges.Where(edge => edge.TargetKey == key && index.Docs[edge.Doc].Kind == Kind.Index)
                               .Select(edge => Row.Of(("at", At(edge.Doc, edge.Line)), ("kind", "index-row"), ("id", Ids.Display(key)), ("detail", index.Docs[edge.Doc].Title))));

            rows.AddRange(index.Edges.Where(edge => edge.Kind == Edge.SupersededBy && index.Docs[edge.Doc].Kind == Kind.Record && index.Docs[edge.Doc].Id == Ids.Display(key))
                               .Select(edge => Row.Of(("at", At(edge.Doc, edge.Line)), ("kind", Edge.SupersededBy), ("id", Ids.Display(edge.TargetKey!)), ("detail", ""))));
        }
        else
        {
            foreach (var scope in Resolve(query).Where(scope => scope.Doc >= 0))
                rows.Add(scope.Section < 0 ? DocRow(scope.Doc) : SectionRow(scope.Section, index.Docs[scope.Doc].Kind));
        }

        rows = [.. rows.DistinctBy(row => string.Join('\u0001', row.Fields.Select(field => field.Value)))];

        return rows.Count == 0
            ? new Answer(Answer.Exact, "entry", query, "nothing: no id, path, anchor, name or heading is this. Try `find`.", [], 1)
            : new Answer(Answer.Exact, "entry", query, Count(rows.Count, "pointer"), rows, 0);
    }

    public Answer Outline(string query)
    {
        var rows = new List<Row>();

        foreach (var scope in Resolve(query).Where(scope => scope.Doc >= 0).DistinctBy(scope => (scope.Doc, scope.Section)))
        {
            var sections = index.SectionsOf(scope.Doc).Where(s => scope.Section < 0 || s >= scope.Section && index.Sections[s].Line <= scope.To);

            rows.AddRange(sections.Select(s => index.Sections[s]).Select(section => Row.Of(
                ("at", At(section.Doc, section.Line)), ("level", "h" + section.Level.ToString(CultureInfo.InvariantCulture)),
                ("anchor", section.Anchor.Length == 0 ? "" : "#" + section.Anchor), ("heading", section.Heading))));
        }

        return rows.Count == 0
            ? new Answer(Answer.Exact, "outline", query, "nothing: no document or section is this.", [], 1)
            : new Answer(Answer.Exact, "outline", query, Count(rows.Count, "heading"), rows, 0);
    }

    /// <summary>
    /// The edges out of an entry (<paramref name="reverse"/> false) or into it. Depth 1 lists every kind;
    /// a deeper step follows explicit edges only, links and supersessions, never a mention.
    /// </summary>
    public Answer Edges(string query, int depth, bool reverse)
    {
        var command = reverse ? "cited-by" : "cites";
        var scopes  = Resolve(query).ToList();

        if (scopes.Count == 0) return new Answer(Answer.Exact, command, query, "nothing: no id, path, anchor, name or heading is this.", [], 1);

        var rows    = new List<Row>();
        var visited = new HashSet<int>(scopes.Where(scope => scope.Doc >= 0).Select(scope => scope.Doc));
        var level   = scopes.SelectMany(scope => reverse ? Into(scope) : OutOf(scope)).Distinct().ToList();

        for (var step = 1; level.Count > 0; step++)
        {
            rows.AddRange(level.OrderBy(edge => index.Docs[edge.Doc].Path, StringComparer.Ordinal).ThenBy(edge => edge.Line).Select(edge => Row.Of(
                ("depth", step.ToString(CultureInfo.InvariantCulture)), ("at", At(edge.Doc, edge.Line)), ("from", index.Docs[edge.Doc].Kind),
                ("kind", edge.Kind), ("target", edge.Target))));

            if (step == depth) break;

            var next = level.Where(edge => edge.IsExplicit).Select(edge => reverse ? edge.Doc : TargetDoc(edge)).Where(doc => doc >= 0 && visited.Add(doc)).Distinct().ToList();
            level = [.. next.SelectMany(doc => reverse ? Into(WholeDoc(doc)) : OutOf(WholeDoc(doc))).Where(edge => edge.IsExplicit).Distinct()];
        }

        var summary = Count(rows.Count, "edge") + (depth > 1 ? $", to depth {depth}; steps past the first follow links and supersessions only" : "");
        return new Answer(Answer.Exact, command, query, summary, rows, 0);
    }

    public Answer List(IReadOnlyList<string> filters)
    {
        var parsed = new List<(string Key, string Op, string Value)>();
        foreach (var filter in filters)
        {
            var op = new[] { ">=", "<=", "=", "~" }.FirstOrDefault(candidate => filter.Contains(candidate, StringComparison.Ordinal));
            if (op is null) return new Answer(Answer.Exact, "list", string.Join(' ', filters), $"'{filter}' is no filter: write key=value, key>=value, key<=value or key~text.", [], 2);
            var at = filter.IndexOf(op, StringComparison.Ordinal);
            parsed.Add((filter[..at].Trim().ToLowerInvariant(), op, filter[(at + op.Length)..].Trim()));
        }

        var rows = index.Docs.Select((doc, d) => (doc, d))
                        .Where(pair => parsed.All(filter => Matches(Value(pair.doc, filter.Key), filter.Op, filter.Value)))
                        .Select(pair => Row.Of(("at", At(pair.d, 1)), ("kind", pair.doc.Kind), ("id", pair.doc.Id), ("status", pair.doc.Get("status")), ("date", pair.doc.Date), ("title", pair.doc.Title)))
                        .ToList();

        return new Answer(Answer.Exact, "list", string.Join(' ', filters), Count(rows.Count, "document"), rows, rows.Count == 0 ? 1 : 0);

        static string? Value(Doc doc, string key) => key switch
        {
            "kind" => doc.Kind, "id" => doc.Id, "path" => doc.Path, "title" => doc.Title,
            _ => doc.Get(key),
        };

        static bool Matches(string? actual, string op, string expected) => actual is not null && op switch
        {
            "="  => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || actual.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase),
            ">=" => string.CompareOrdinal(actual, expected) >= 0,
            "<=" => string.CompareOrdinal(actual, expected) <= 0,
            _    => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// BM25 over the sections of the ranked documents, the heading weighted above the headings over
    /// it, the prose above the code. A section that defines an id the query names (a record's title, a
    /// <c>## CMTK0004</c> heading) is put first.
    /// </summary>
    public Answer Find(string query, int top, string? kind)
    {
        const double k1 = 1.2, b = 0.75, IdBoost = 20;

        var terms = Tokenizer.QueryTerms(query);
        if (terms.Count == 0) return new Answer(Answer.Ranked, "find", query, "no search terms in the query.", [], 2);

        var ranked  = Enumerable.Range(0, index.Sections.Count).Where(s => Kind.IsRanked(index.Docs[index.Sections[s].Doc].Kind)).ToList();
        var average = ranked.Count == 0 ? 1 : Math.Max(1, ranked.Average(s => index.Lengths[s]));
        var scores  = new Dictionary<int, double>();

        foreach (var term in terms)
        {
            var at = BinarySearch(index.Terms, term);
            if (at < 0) continue;

            var postings = index.Postings[at];
            var idf      = Math.Log(1 + (ranked.Count - postings.Count + 0.5) / (postings.Count + 0.5));

            foreach (var (section, frequency) in postings)
            {
                var norm = frequency + k1 * (1 - b + b * index.Lengths[section] / average);
                scores[section] = scores.GetValueOrDefault(section) + idf * frequency * (k1 + 1) / norm;
            }
        }

        var defining = new HashSet<int>();
        foreach (var key in terms.Where(term => Ids.IsRecordKey(term) || Ids.IsDiagnosticKey(term)))
        {
            foreach (var section in index.DefinitionsOf(key).Select(definition => index.SectionAt(definition.Doc, definition.Line)).Distinct())
            {
                defining.Add(section);
                scores[section] = scores.GetValueOrDefault(section) + IdBoost;
            }
        }

        var matched = scores.Where(pair => kind is null || index.Docs[index.Sections[pair.Key].Doc].Kind == kind)
                            .OrderByDescending(pair => Math.Round(pair.Value, 6))
                            .ThenBy(pair => index.Docs[index.Sections[pair.Key].Doc].Path, StringComparer.Ordinal)
                            .ThenBy(pair => index.Sections[pair.Key].Line)
                            .ToList();

        var rows = matched.Take(top).Select((pair, rank) => Row.Of(
            ("rank", (rank + 1).ToString(CultureInfo.InvariantCulture)), ("score", pair.Value.ToString("F2", CultureInfo.InvariantCulture)),
            ("at", At(index.Sections[pair.Key].Doc, index.Sections[pair.Key].Line)), ("kind", index.Docs[index.Sections[pair.Key].Doc].Kind + (defining.Contains(pair.Key) ? " defines-id" : "")),
            ("section", index.Sections[pair.Key].Path))).ToList();

        var summary = $"{rows.Count} of {matched.Count} matching sections, BM25 over {ranked.Count} sections (indexes left out). A miss proves nothing: confirm with `entry`, or grep.";
        return new Answer(Answer.Ranked, "find", query, summary, rows, rows.Count == 0 ? 1 : 0);
    }

    /// <summary>Every C# block, every line whose comment states an output, and every other fenced block of the README and docs pages.</summary>
    public Answer Claims(string? page, string? kind)
    {
        var docs = page is null ? null : Resolve(page).Where(scope => scope.Doc >= 0).Select(scope => scope.Doc).ToHashSet();
        if (docs is { Count: 0 }) return new Answer(Answer.Exact, "claims", page!, "nothing: no page is this.", [], 1);

        var rows = index.Claims.Where(claim => (docs is null || docs.Contains(claim.Doc)) && (kind is null || claim.Kind == kind || claim.Kind.StartsWith(kind + ":", StringComparison.Ordinal)))
                        .Select(claim => Row.Of(
                            ("at", At(claim.Doc, claim.Line)), ("kind", claim.Kind), ("id", claim.Id), ("hash", claim.Hash),
                            ("compiled", claim.OptOut is not null ? $"not compiled: {claim.OptOut}" : claim.Region is not null ? $"region {claim.Region}" : claim.Kind is "csharp" or "stated" ? "no region" : ""),
                            ("section", index.Sections[index.SectionAt(claim.Doc, claim.Line)].Path), ("text", claim.Text)))
                        .ToList();

        return new Answer(Answer.Exact, "claims", page ?? "", Count(rows.Count, "claim"), rows, rows.Count == 0 ? 1 : 0);
    }

    /// <summary>
    /// Links whose file or anchor does not exist, ids that name no record, diagnostic or fact, and the facts'
    /// rules (<see cref="FactRules"/>): a pin behind or ahead of what is installed, or naming nothing installed,
    /// a fact without its test, a test without its fact.
    /// </summary>
    public Answer Check()
    {
        var rows    = new List<Row>();
        var anchors = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        var links   = 0;
        var ids     = 0;

        foreach (var edge in index.Edges)
        {
            if (edge.Kind == Edge.LinkKind)
            {
                links++;
                var path = edge.TargetPath!;

                if (path.StartsWith("..", StringComparison.Ordinal) || !(File.Exists(Path.Combine(root, path)) || Directory.Exists(Path.Combine(root, path))))
                    rows.Add(Problem(edge, "broken-link", $"{path} does not exist"));
                else if (edge.TargetAnchor is { } anchor && !AnchorsOf(path).Contains(anchor))
                    rows.Add(Problem(edge, "missing-anchor", $"#{anchor} is no heading of {path}"));
            }

            if (edge.TargetKey is { } key && edge.Kind != Edge.LinkKind)
            {
                ids++;
                var known = Ids.IsRecordKey(key)
                    ? index.Docs.Any(doc => doc.Kind == Kind.Record && doc.Id == Ids.Display(key))
                    : index.DefinitionsOf(key).Any() || index.Edges.Any(other => other.TargetKey == key && index.Docs[other.Doc].Kind == Kind.Index);
                if (!known) rows.Add(Problem(edge, "dangling-id", $"{Ids.Display(key)} is defined nowhere"));
            }
        }

        rows.AddRange(FactRules.Check(index, root, out var facts).Select(problem => Row.Of(("at", problem.At), ("kind", problem.Kind), ("target", problem.Target), ("detail", problem.Detail))));

        return new Answer(Answer.Exact, "check", "", $"{Count(rows.Count, "problem")} in {links} links, {ids} ids and {Count(facts, "fact")}", rows, rows.Count == 0 ? 0 : 1);

        Row Problem(Edge edge, string kind, string detail) => Row.Of(("at", At(edge.Doc, edge.Line)), ("kind", kind), ("target", edge.Written), ("detail", detail));

        HashSet<string> AnchorsOf(string path)
        {
            if (!anchors.TryGetValue(path, out var set))
            {
                var doc = index.DocIndexOf(path);
                set = doc >= 0 ? [.. index.SectionsOf(doc).Select(s => index.Sections[s].Anchor)]
                    : path.EndsWith(".md", StringComparison.Ordinal) ? [.. new Page(path, File.ReadAllText(Path.Combine(root, path))).Headings.Select(heading => heading.Anchor)]
                    : [];
                anchors[path] = set;
            }

            return set!;
        }
    }

    /// <summary>
    /// What a query names, tried in order: an id (<c>CMTK0004</c>, <c>61</c>, <c>decision 61</c>), a
    /// path with or without an anchor, an anchor alone, a document's name (a spike, a package, a
    /// guide), a heading's text.
    /// </summary>
    private IEnumerable<Scope> Resolve(string query)
    {
        query = query.Trim();

        if (Ids.Parse(query) is { } key)
        {
            var defined = index.DefinitionsOf(key).Select(definition =>
                Kind.IsEntry(index.Docs[definition.Doc].Kind) ? WholeDoc(definition.Doc)
              : definition.IsHeading ? OfSection(index.SectionAt(definition.Doc, definition.Line), key) with { ByPath = false }
              : new Scope(key, definition.Doc, -1, definition.Line, definition.Line, ByPath: false)).ToList();

            return defined.Count > 0 ? defined : [new Scope(key, -1, -1, 0, 0, ByPath: false)];
        }

        var path = IndexBuilder.Resolve("", query);
        if (path.Path.Length > 0 && index.DocIndexOf(path.Path) is var d and >= 0)
        {
            if (path.Anchor is null) return [WholeDoc(d)];

            return index.SectionsOf(d).Where(s => index.Sections[s].Anchor == path.Anchor).Select(s => OfSection(s, IndexBuilder.KeyOf(path.Path, path.Anchor))).ToList();
        }

        var anchor   = query.TrimStart('#');
        var byAnchor = Enumerable.Range(0, index.Sections.Count).Where(s => index.Sections[s].Anchor.Length > 0 && index.Sections[s].Anchor == anchor).ToList();
        if (byAnchor.Count > 0) return byAnchor.Select(s => OfSection(s, IndexBuilder.KeyOf(index.Docs[index.Sections[s].Doc].Path, anchor))).ToList();

        var byName = index.Docs.Select((doc, i) => (doc, i)).Where(pair => pair.doc.Id is not null && string.Equals(pair.doc.Id, query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count > 0) return byName.Select(pair => WholeDoc(pair.i)).ToList();

        return Enumerable.Range(0, index.Sections.Count).Where(s => string.Equals(index.Sections[s].Heading, query, StringComparison.OrdinalIgnoreCase))
                         .Select(s => OfSection(s, null)).ToList();
    }

    private Scope WholeDoc(int doc) => new(KeyOfDoc(doc), doc, -1, 1, index.Docs[doc].LineCount, ByPath: true);

    private Scope OfSection(int section, string? key) =>
        Kind.IsEntry(index.Docs[index.Sections[section].Doc].Kind) && index.Sections[section].Level <= 1
            ? WholeDoc(index.Sections[section].Doc)
            : new Scope(key, index.Sections[section].Doc, section, index.Sections[section].Line, index.SubtreeEnd(section), ByPath: true);

    private IEnumerable<Edge> OutOf(Scope scope) =>
        scope.Doc < 0 ? [] : index.Edges.Where(edge => edge.Doc == scope.Doc && edge.Line >= scope.From && edge.Line <= scope.To);

    private IEnumerable<Edge> Into(Scope scope)
    {
        var path   = scope.ByPath && scope.Doc >= 0 ? index.Docs[scope.Doc].Path : null;
        var anchor = scope.Section >= 0 ? index.Sections[scope.Section].Anchor : null;

        return index.Edges.Where(edge =>
            scope.Key is not null && edge.TargetKey == scope.Key
         || path is not null && edge.TargetPath == path && (anchor is null ? edge.Doc != scope.Doc : edge.TargetAnchor == anchor));
    }

    private int TargetDoc(Edge edge) =>
        edge.TargetPath is not null ? index.DocIndexOf(edge.TargetPath)
      : edge.TargetKey is not null && (Ids.IsRecordKey(edge.TargetKey) || Ids.IsFactKey(edge.TargetKey)) ? index.Docs.Select((doc, i) => (doc, i)).FirstOrDefault(pair => Kind.IsEntry(pair.doc.Kind) && pair.doc.Id == Ids.Display(edge.TargetKey), (null!, -1)).Item2
      : -1;

    private string? KeyOfDoc(int doc) => index.Docs[doc] is { Kind: var kind, Id: { } id } && Kind.IsEntry(kind) ? Ids.Parse(id) : null;

    private Row DocRow(int d)
    {
        var doc = index.Docs[d];
        return Row.Of(("at", At(d, 1)), ("kind", doc.Kind), ("id", doc.Id), ("status", doc.Get("status")), ("date", doc.Date), ("detail", doc.Title));
    }

    private Row SectionRow(int s, string kind)
    {
        var section = index.Sections[s];
        return Row.Of(("at", At(section.Doc, section.Line)), ("kind", kind), ("id", section.Anchor.Length == 0 ? "" : "#" + section.Anchor), ("detail", section.Path));
    }

    private string At(int doc, int line) => $"{index.Docs[doc].Path}:{line.ToString(CultureInfo.InvariantCulture)}";

    private static string Count(int count, string noun) => $"{count.ToString(CultureInfo.InvariantCulture)} {noun}{(count == 1 ? "" : "s")}";

    private static int BinarySearch(IReadOnlyList<string> sorted, string term)
    {
        int low = 0, high = sorted.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var order = string.CompareOrdinal(sorted[mid], term);
            if (order == 0) return mid;
            if (order < 0) low = mid + 1; else high = mid - 1;
        }

        return -1;
    }
}
