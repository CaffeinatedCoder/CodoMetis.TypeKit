using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocsQuery;

/// <summary>
/// Where the probe corpus leans on a verified fact to prune a row or excuse one (design/adr/0107): a <c>refused</c>
/// entry of the axes file whose reason names <c>KB-NNN</c>, a <c>not-promised</c> entry of claims.json whose
/// <c>source</c> is a fact's file or which says <c>fact:KB-NNN</c>, an obligation's declaration of where it does not
/// apply whose <c>cite</c> is <c>fact:KB-NNN</c>, and a <c>Cite.Fact("KB-NNN")</c> or <c>Baseline.Fact("KB-NNN")</c>
/// literal in the harness and probe sources, read as text with comments blanked out. Each is read from the checkout
/// at every <c>check</c>, never from the index.
/// </summary>
internal static partial class FactCitations
{
    public const string AxesFile = ".claude/probe-axes.md", ClaimsFile = "tools/probe/corpus/claims.json";

    public static readonly string[] SourceDirectories = ["tools/probe/corpus/harness", "tools/probe/corpus/probes"];

    /// <summary>The four places, in the order <c>check</c> counts them.</summary>
    public static readonly string[] Kinds = [Refused, NotPromised, Obligation, Harness];

    public const string Refused = "refused", NotPromised = "not-promised", Obligation = "obligation", Harness = "harness";

    /// <summary>
    /// One citation: the kind of place, where it is written (<c>path:line</c>), the fact's id, the place in words, and
    /// whether it reads the fact's fixture as a baseline (<c>Baseline.Fact</c>), which takes a fixture as well as a test.
    /// </summary>
    internal sealed record Citation(string Kind, string At, string Fact, string Place, bool Baseline = false);

    /// <summary>What a scan read: every citation, and how many places of each kind it looked at, cited or not.</summary>
    internal sealed record Scan(IReadOnlyList<Citation> Citations, IReadOnlyDictionary<string, int> Read)
    {
        public int Of(string kind) => Citations.Count(citation => citation.Kind == kind);
    }

    public static Scan Find(string root)
    {
        var citations = new List<Citation>();
        var read      = Kinds.ToDictionary(kind => kind, _ => 0, StringComparer.Ordinal);

        ReadAxes(root, citations, read);
        ReadClaims(root, citations, read);
        ReadSources(root, citations, read);

        return new Scan(citations, read);
    }

    /// <summary>The axes file's <c>refused</c> entries: axis to value to the reason the framework refuses it.</summary>
    private static void ReadAxes(string root, List<Citation> citations, Dictionary<string, int> read)
    {
        var path = Path.Combine(root, AxesFile);
        if (!File.Exists(path)) return;

        const string marker = "```json probe-axes";
        var text  = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return;
        start += marker.Length;
        var end = text.IndexOf("```", start, StringComparison.Ordinal);
        if (end < 0) return;

        using var block = JsonDocument.Parse(text[start..end]);
        if (!block.RootElement.TryGetProperty("refused", out var refused) || refused.ValueKind != JsonValueKind.Object) return;

        var cursor = new Cursor(AxesFile, text, text.IndexOf("\"refused\"", start, StringComparison.Ordinal));
        foreach (var axis in refused.EnumerateObject().Where(axis => axis.Value.ValueKind == JsonValueKind.Object))
        {
            foreach (var value in axis.Value.EnumerateObject())
            {
                read[Refused]++;
                var place = $"the refused entry {axis.Name}={value.Name}";
                cursor.Seek($"\"{value.Name}\"");
                foreach (var fact in FactIds(value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString()! : value.Value.GetRawText()))
                    citations.Add(new Citation(Refused, cursor.At(fact), fact, place));
            }
        }
    }

    /// <summary>Each claim's <c>not-promised</c> entries and its obligations' <c>inapplicable</c> declarations.</summary>
    private static void ReadClaims(string root, List<Citation> citations, Dictionary<string, int> read)
    {
        var path = Path.Combine(root, ClaimsFile);
        if (!File.Exists(path)) return;

        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        using var claims = JsonDocument.Parse(text);
        if (claims.RootElement.ValueKind != JsonValueKind.Array) return;

        // Walked in the file's order, so each citation's line is the next occurrence of its id from the last one.
        var cursor = new Cursor(ClaimsFile, text, 0);
        foreach (var claim in claims.RootElement.EnumerateArray())
        {
            var id = Str(claim, "id");
            cursor.Seek($"\"{id}\"");

            foreach (var property in claim.EnumerateObject())
            {
                if (property.NameEquals("obligations") && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var obligation in property.Value.EnumerateArray())
                    {
                        cursor.Seek($"\"{Str(obligation, "id")}\"");
                        var number = 0;
                        foreach (var declaration in Array(obligation, "inapplicable"))
                        {
                            read[Obligation]++;
                            number++;
                            if (FactOf(Str(declaration, "cite")) is not { } fact) continue;
                            citations.Add(new Citation(Obligation, cursor.At("fact:" + fact), fact,
                                $"claim {id}, obligation {Str(obligation, "id")}, inapplicable declaration {number.ToString(CultureInfo.InvariantCulture)}"));
                        }
                    }
                }
                else if (property.NameEquals("not-promised") && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in property.Value.EnumerateArray())
                    {
                        read[NotPromised]++;
                        cursor.Seek($"\"{Str(entry, "id")}\"");
                        var place = $"claim {id}, not-promised {Str(entry, "id")}";
                        var facts = new List<string>();
                        if (KnowledgeSource().Match(Str(entry, "source")) is { Success: true } source) facts.Add(source.Groups["id"].Value);
                        foreach (var field in entry.EnumerateObject().Where(field => field.Value.ValueKind == JsonValueKind.String))
                            facts.AddRange(CiteFact().Matches(field.Value.GetString()!).Select(match => match.Groups["id"].Value));

                        foreach (var fact in facts.Distinct(StringComparer.Ordinal))
                            citations.Add(new Citation(NotPromised, cursor.At(fact), fact, place));
                    }
                }
            }
        }
    }

    /// <summary>The harness's and the probes' <c>Cite.Fact</c> and <c>Baseline.Fact</c> literals, comments blanked out.</summary>
    private static void ReadSources(string root, List<Citation> citations, Dictionary<string, int> read)
    {
        foreach (var directory in SourceDirectories.Select(directory => Path.Combine(root, directory)).Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs").Order(StringComparer.Ordinal))
            {
                read[Harness]++;
                var path   = Corpus.Relative(root, file);
                var source = File.ReadAllText(file).ReplaceLineEndings("\n");
                foreach (Match match in FactLiteral().Matches(WithoutComments(source)))
                {
                    var line = 1 + source.AsSpan(0, match.Index).Count('\n');
                    citations.Add(new Citation(Harness, $"{path}:{line.ToString(CultureInfo.InvariantCulture)}", match.Groups["id"].Value,
                        $"{match.Groups["factory"].Value}.Fact(\"{match.Groups["id"].Value}\")", match.Groups["factory"].Value == "Baseline"));
                }
            }
        }
    }

    /// <summary>
    /// The source with each comment's content replaced by spaces, newlines kept, and string literals as they are, so a
    /// citation written in a comment does not count and a line number still holds.
    /// </summary>
    internal static string WithoutComments(string source)
    {
        var result = new StringBuilder(source);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                for (; i < source.Length && source[i] != '\n'; i++) result[i] = ' ';
            }
            else if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop  = close < 0 ? source.Length : close + 2;
                for (; i < stop; i++) if (source[i] != '\n') result[i] = ' ';
                i--;
            }
            else if (source[i] == '"')
            {
                i = AfterString(source, i);
            }
            else if (source[i] == '\'')
            {
                // A character literal: '"' must not open a string.
                var j = i + 1;
                if (j < source.Length && source[j] == '\\') j++;
                if (j + 1 < source.Length && source[j + 1] == '\'') i = j + 1;
            }
        }

        return result.ToString();
    }

    /// <summary>The index of the closing quote of the string literal opened at <paramref name="i"/>: raw, verbatim or regular.</summary>
    private static int AfterString(string source, int i)
    {
        var quotes = 0;
        while (i + quotes < source.Length && source[i + quotes] == '"') quotes++;

        if (quotes >= 3)
        {
            var close = source.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
            return close < 0 ? source.Length : close + quotes - 1;
        }

        if (quotes == 2) return i + 1;   // ""

        var verbatim = i > 0 && source[i - 1] == '@' || i > 1 && source[i - 1] == '$' && source[i - 2] == '@';
        for (var j = i + 1; j < source.Length; j++)
        {
            if (verbatim && source[j] == '"' && j + 1 < source.Length && source[j + 1] == '"') { j++; continue; }
            if (!verbatim && source[j] == '\\') { j++; continue; }
            if (source[j] == '"' || !verbatim && source[j] == '\n') return j;
        }

        return source.Length;
    }

    /// <summary>The ids a refused entry's reason names, as <c>fact:KB-NNN</c> or plain <c>KB-NNN</c>.</summary>
    private static IEnumerable<string> FactIds(string text) => AnyFact().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal);

    private static string? FactOf(string cite) => cite.StartsWith("fact:", StringComparison.Ordinal) ? cite["fact:".Length..] : null;

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    /// <summary>Line numbers for places read in a file's order: each needle is looked for from the last place found.</summary>
    private sealed class Cursor(string path, string text, int offset)
    {
        private int at = Math.Max(0, offset);

        public void Seek(string needle)
        {
            var found = text.IndexOf(needle, at, StringComparison.Ordinal);
            if (found >= 0) at = found;
        }

        public string At(string needle)
        {
            var found = text.IndexOf(needle, at, StringComparison.Ordinal);
            var line  = 1 + text.AsSpan(0, found >= 0 ? found : at).Count('\n');
            if (found >= 0) at = found + needle.Length;
            return $"{path}:{line.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    [GeneratedRegex(@"\bKB-\d{3}\b")]
    private static partial Regex AnyFact();

    [GeneratedRegex(@"\bfact:\s?(?<id>KB-\d{3})\b")]
    private static partial Regex CiteFact();

    [GeneratedRegex(@"^design/knowledge/(?<id>KB-\d{3})-[^/#]*\.md(#.*)?$")]
    private static partial Regex KnowledgeSource();

    [GeneratedRegex(@"\b(?<factory>Cite|Baseline)\s*\.\s*Fact\s*\(\s*""(?<id>KB-\d{3})""\s*\)")]
    private static partial Regex FactLiteral();
}
