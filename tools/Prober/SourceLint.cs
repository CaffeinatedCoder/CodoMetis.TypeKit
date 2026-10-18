using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Prober;

/// <summary>
/// What `register` and `publish` refuse in the staged probe sources before anything is built (decision 103):
/// a <c>Note</c> whose first argument is not a key of the catalogue (<c>NoteKeys.Name</c>, a member the
/// corpus or the staging declares), and a two-argument <c>Skip</c> (the harness's, not LINQ's) whose first
/// argument is not a string literal naming a finding this ledger holds open. The compiler refuses a key
/// outside the catalogue too; this says so by file and line before a build, and holds the finding ids,
/// which stay strings. A check's <c>Inapplicable</c> and <c>Refused</c> cite their reason by a <c>Cite</c>
/// factory with a string literal, and what it names exists: a not-promised entry of the probe's claim (of any
/// claim, for a harness source no one probe owns), a file of the docs, a knowledge entry under design/knowledge/,
/// a record under design/adr/. It reads code with comments and string literals blanked out, so a call written in
/// either does not count.
/// </summary>
internal static partial class SourceLint
{
    /// <summary>The catalogue's members, as <c>harness/Row.cs</c> declares them.</summary>
    [GeneratedRegex(@"public\s+static\s+readonly\s+NoteKeys\s+(\w+)\s*=")]
    private static partial Regex CatalogueMember();

    [GeneratedRegex(@"\.\s*(Note|Skip|Inapplicable|Refused)\s*\(")]
    private static partial Regex Call();

    [GeneratedRegex("^Cite\\s*\\.\\s*(NotPromised|Docs|Fact|Record)\\s*\\(\\s*\"([^\"\\\\]*)\"\\s*\\)$")]
    private static partial Regex CiteFactory();

    [GeneratedRegex(@"^NoteKeys\s*\.\s*(\w+)$")]
    private static partial Regex CatalogueKey();

    [GeneratedRegex("^\"([^\"\\\\]*)\"$")]
    private static partial Regex PlainLiteral();

    /// <summary>Refuses, naming each file and line, while a staged source breaks a rule; does nothing without a staging.</summary>
    public static void Refuse(Ledger l, string command)
    {
        var problems = Problems(l);
        if (problems.Count > 0)
            throw new Refusal($"{command}: the staged sources make a note, a skip or a citation that is not written out (decision 103):\n  " + string.Join("\n  ", problems));
    }

    /// <summary>Every problem in the staged harness and probe sources, with the finding ids checked against this ledger.</summary>
    public static List<string> Problems(Ledger l)
    {
        var staged = Sources(l.StagingDir).ToList();
        if (staged.Count == 0) return [];
        var catalogue = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Sources(l.CorpusDir).Concat(staged))
            foreach (Match m in CatalogueMember().Matches(Mask(File.ReadAllText(file)))) catalogue.Add(m.Groups[1].Value);
        var findings = l.Findings();
        // The not-promised ids of each claim, and the claim of each registered probe: a probe's source cites its own
        // claim's entries, a harness source that no one probe owns any claim's.
        var notPromised = l.Claims().ToDictionary(c => Files.Str(c["id"]),
            c => (IReadOnlySet<string>)(c["not-promised"] as JsonArray ?? []).OfType<JsonObject>().Select(n => Files.Str(n["id"])).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        // A probe's source is named for it, or for the class its filter selects (*C1Probe).
        var claimOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, entry) in l.Registry())
        {
            claimOf[name] = Files.Str(entry["claim"]);
            if (Files.Str(entry["filter"]).TrimStart('*') is { Length: > 0 } cls) claimOf.TryAdd(cls, Files.Str(entry["claim"]));
        }
        var problems = new List<string>();
        foreach (var file in staged)
        {
            // Relative to the checkout where the state lives in it, else in full (PROBE_STATE_DIR elsewhere).
            var path = Path.GetRelativePath(l.Top, file) is var rel && !rel.StartsWith("..", StringComparison.Ordinal) ? rel.Replace('\\', '/') : file;
            var cites = new Citations(l.Top, claimOf.TryGetValue(Path.GetFileNameWithoutExtension(file), out var claim) ? claim : null, notPromised);
            foreach (var (line, problem, finding) in Scan(File.ReadAllText(file), catalogue, cites.Problem))
            {
                if (problem is not null) problems.Add($"{path}:{line}: {problem}");
                else if (finding is not null && Status(findings, finding) is { } status)
                    problems.Add($"{path}:{line}: Skip(\"{finding}\", ...) names {status}; a skip steps around an open finding only");
            }
        }
        return problems;
    }

    private static string? Status(Dictionary<string, System.Text.Json.Nodes.JsonObject> findings, string id) =>
        !findings.TryGetValue(id, out var f) ? "no finding this ledger has"
        : Files.Str(f["status"]) is var status && status != "open" ? $"finding {id}, which is {status}"
        : null;

    private static IEnumerable<string> Sources(string root) =>
        new[] { "harness", "probes" }.Select(sub => Path.Combine(root, sub)).Where(Directory.Exists)
                                     .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs")).Order(StringComparer.Ordinal);

    /// <summary>
    /// What a citation of one source may name, and why one does not: <see cref="Problem"/> takes the factory and its
    /// literal and says what is missing, or null. <paramref name="Claim"/> is the claim of the probe the source is, or
    /// null for a source that no one probe owns, which may cite any claim's not-promised entry.
    /// </summary>
    internal sealed record Citations(string Top, string? Claim, IReadOnlyDictionary<string, IReadOnlySet<string>> NotPromised)
    {
        public string? Problem(string factory, string value)
        {
            switch (factory)
            {
                case "NotPromised" when Claim is not null:
                    return NotPromised.GetValueOrDefault(Claim)?.Contains(value) ?? false ? null : $"names no not-promised entry of its claim {Claim}";
                case "NotPromised":
                    return NotPromised.Values.Any(ids => ids.Contains(value)) ? null : "names no not-promised entry of any claim";
                case "Docs":
                    var file = value.Split('#')[0];
                    return file.Length > 0 && !file.Contains("..", StringComparison.Ordinal) && File.Exists(Path.Combine(Top, file)) ? null : $"names no file {file} in the repository";
                case "Fact":
                    return Named(Path.Combine(Top, "design", "knowledge"), value) ? null : "names no entry under design/knowledge/";
                default:
                    return Named(Path.Combine(Top, "design", "adr"), value) ? null : "names no record under design/adr/";
            }
        }

        /// <summary>A file of the directory named for the id: <c>KB-031-...md</c>, <c>0094-...md</c>.</summary>
        private static bool Named(string directory, string id) =>
            id.Length > 0 && Directory.Exists(directory)
            && Directory.EnumerateFiles(directory, "*.md").Any(f => Path.GetFileName(f).StartsWith(id + "-", StringComparison.Ordinal) || Path.GetFileName(f) == id + ".md");
    }

    /// <summary>
    /// Each <c>Note</c>, two-argument <c>Skip</c>, and <c>Inapplicable</c> or <c>Refused</c> with an argument, of one
    /// source: its line, the rule it breaks (or null), and for a skip written out, the finding it names.
    /// <paramref name="cite"/> says what a citation's factory and literal fail to name; without it only the form is held.
    /// </summary>
    internal static IEnumerable<(int Line, string? Problem, string? Finding)> Scan(string source, IReadOnlySet<string> catalogue, Func<string, string, string?>? cite = null)
    {
        var masked = Mask(source);
        foreach (Match m in Call().Matches(masked))
        {
            var open = m.Index + m.Length - 1;
            var arguments = Arguments(masked, source, open);
            if (arguments is null) continue;
            var line = 1 + masked.AsSpan(0, m.Index).Count('\n');
            var first = arguments.Count > 0 ? arguments[0] : "";
            if (m.Groups[1].Value is "Inapplicable" or "Refused")
            {
                if (arguments.Count == 0) continue;   // an entry's own Refused() of the harness, not a check's
                var call = m.Groups[1].Value;
                var factory = CiteFactory().Match(first);
                if (!factory.Success) yield return (line, $"{call}({Shown(first)}, ...) does not cite by a Cite factory with a string literal", null);
                else if (cite?.Invoke(factory.Groups[1].Value, factory.Groups[2].Value) is { } missing)
                    yield return (line, $"{call}({Shown(first)}, ...) {missing}", null);
            }
            else if (m.Groups[1].Value == "Note")
            {
                var key = CatalogueKey().Match(first);
                if (!key.Success) yield return (line, $"Note({Shown(first)}, ...) does not name a key of NoteKeys", null);
                else if (!catalogue.Contains(key.Groups[1].Value)) yield return (line, $"Note({Shown(first)}, ...) names no key of NoteKeys", null);
            }
            else if (arguments.Count == 2)
            {
                var id = PlainLiteral().Match(first);
                yield return id.Success ? (line, null, id.Groups[1].Value) : (line, $"Skip({Shown(first)}, ...) does not name its finding as a string literal", null);
            }
        }
    }

    private static string Shown(string argument) => Regex.Replace(argument, @"\s+", " ") is var s && s.Length > 60 ? s[..60] + "..." : s;

    /// <summary>The top-level arguments of the call whose parenthesis opens at <paramref name="open"/>, from the original text; null if it never closes.</summary>
    private static List<string>? Arguments(string masked, string source, int open)
    {
        var arguments = new List<string>();
        var depth = 0; var start = open + 1;
        for (var i = open + 1; i < masked.Length; i++)
        {
            switch (masked[i])
            {
                case '(' or '[' or '{': depth++; break;
                case ')' or ']' or '}' when depth > 0: depth--; break;
                case ',' when depth == 0: arguments.Add(source[start..i].Trim()); start = i + 1; break;
                case ')':
                    var last = source[start..i].Trim();
                    if (last.Length > 0 || arguments.Count > 0) arguments.Add(last);
                    return arguments;
            }
        }
        return null;
    }

    /// <summary>
    /// The source with every comment and the content of every string and character literal blanked out (line
    /// breaks kept, so lines still count), and the code of an interpolation hole kept as code.
    /// </summary>
    internal static string Mask(string s)
    {
        var m = new StringBuilder(s);
        Code(s, m, 0, hole: false);
        return m.ToString();
    }

    private static void Blank(StringBuilder m, int from, int to)
    {
        for (var i = from; i < to && i < m.Length; i++) if (m[i] != '\n') m[i] = ' ';
    }

    /// <summary>Scans code from <paramref name="i"/>; in a hole, returns the index after its closing brace.</summary>
    private static int Code(string s, StringBuilder m, int i, bool hole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var end = s.IndexOf('\n', i); end = end < 0 ? s.Length : end;
                Blank(m, i, end); i = end;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal); end = end < 0 ? s.Length : end + 2;
                Blank(m, i, end); i = end;
            }
            else if (c == '\'')
            {
                var j = i + 1;
                while (j < s.Length && s[j] != '\'' && s[j] != '\n') j += s[j] == '\\' ? 2 : 1;
                Blank(m, i + 1, j); i = j + 1;
            }
            else if (c is '"' or '$' or '@' && StringStart(s, i) is { } literal) i = String(s, m, i, literal);
            else if (hole && c == '{') { depth++; i++; }
            else if (hole && c == '}') { if (depth-- == 0) return i + 1; i++; }
            else i++;
        }
        return i;
    }

    private sealed record Literal(int Dollars, bool Verbatim, int Quotes, int QuoteAt);

    /// <summary>The literal that starts at <paramref name="i"/>: its <c>$</c> count, <c>@</c>, and run of quotes.</summary>
    private static Literal? StringStart(string s, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_') && s[i] != '"') return null;
        var j = i; var dollars = 0; var verbatim = false;
        while (j < s.Length && (s[j] == '$' || s[j] == '@')) { if (s[j] == '$') dollars++; else verbatim = true; j++; }
        if (j >= s.Length || s[j] != '"') return null;
        var quotes = 0;
        while (j + quotes < s.Length && s[j + quotes] == '"') quotes++;
        return new Literal(dollars, verbatim, quotes >= 3 ? quotes : 1, j);
    }

    /// <summary>Blanks one literal's content, keeping its holes as code; returns the index after it.</summary>
    private static int String(string s, StringBuilder m, int i, Literal literal)
    {
        var j = literal.QuoteAt + literal.Quotes;
        if (literal.Quotes >= 3)
        {
            // A raw literal ends at a run of as many quotes as it opened with. Holes in it hold no note or skip
            // (both return nothing), so it is blanked whole.
            var close = new string('"', literal.Quotes);
            var end = s.IndexOf(close, j, StringComparison.Ordinal); end = end < 0 ? s.Length : end;
            Blank(m, j, end);
            return end + literal.Quotes;
        }
        var from = j;
        while (j < s.Length)
        {
            var c = s[j];
            if (literal.Verbatim && c == '"' && j + 1 < s.Length && s[j + 1] == '"') { j += 2; continue; }
            if (!literal.Verbatim && c == '\\') { j += 2; continue; }
            if (c == '"') { Blank(m, from, j); return j + 1; }
            if (literal.Dollars > 0 && c == '{')
            {
                if (j + 1 < s.Length && s[j + 1] == '{') { j += 2; continue; }
                Blank(m, from, j + 1);
                j = Code(s, m, j + 1, hole: true);
                from = j - 1;
                continue;
            }
            if (!literal.Verbatim && c == '\n') break;
            j++;
        }
        Blank(m, from, j);
        return j;
    }
}
