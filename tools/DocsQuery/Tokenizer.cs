using System.Text.RegularExpressions;

namespace DocsQuery;

/// <summary>
/// Text to search terms. The repository's identifiers are single terms: <c>CMTK0004</c>,
/// <c>decision 61</c> (as <c>adr:0061</c>), <c>§9</c>, <c>2.1.0</c>, and a PascalCase or generic
/// identifier such as <c>IValidatedValue&lt;TSelf, T, TFault&gt;</c>, which also yields its bare name so a
/// query for <c>IValidatedValue</c> finds it. A dotted name yields itself and its parts.
/// </summary>
/// <remarks>
/// Plain lowercase words are stemmed lightly (plural, past tense, <c>-ing</c>, a final <c>e</c>), so
/// "preserved references" meets "preserve reference". A capitalised word yields its stem and its
/// lowercase form, since a type name (<c>Preserve</c>) and a word in prose share one spelling.
/// Everything is ordinal and culture-free.
/// </remarks>
internal static partial class Tokenizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "do", "does", "for", "from", "has", "have", "how", "if", "in",
        "into", "is", "it", "its", "of", "on", "or", "so", "than", "that", "the", "their", "then", "there", "these", "this",
        "those", "to", "was", "were", "what", "when", "where", "which", "who", "why", "with",
    };

    /// <summary>The terms of a text, in order, repeats kept (the caller counts them).</summary>
    public static List<string> Terms(string text)
    {
        var terms = new List<string>();

        foreach (var mention in Ids.Mentions(text)) terms.Add(mention.Key);

        foreach (Match match in Token().Matches(text))
        {
            if (match.Groups["section"].Success)
            {
                terms.Add("§" + match.Groups["section"].Value);
            }
            else if (match.Groups["number"].Success)
            {
                terms.Add(match.Value);
            }
            else if (match.Groups["name"].Success)
            {
                AddName(match.Value, terms);
            }
        }

        return terms;
    }

    /// <summary>The terms of a query: as a text's, plus a bare zero-padded number taken as a record (<c>0061</c>).</summary>
    public static List<string> QueryTerms(string query)
    {
        var terms = Terms(query);

        foreach (Match match in PaddedNumber().Matches(query))
            terms.Add(Ids.RecordKey(int.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture)));

        return [.. terms.Distinct(StringComparer.Ordinal)];
    }

    private static void AddName(string name, List<string> terms)
    {
        if (Ids.DiagnosticPattern().IsMatch(name) && name.Length == 8) return; // counted as a mention

        var compact = Whitespace().Replace(name, "").ToLowerInvariant();
        var generic = compact.IndexOf('<');
        var dotted  = compact.Contains('.');

        if (generic < 0 && !dotted)
        {
            AddWord(name, terms);
            return;
        }

        terms.Add(compact);

        // The bare name of a generic, its type arguments that are not type parameters, and the parts of a dotted name.
        foreach (Match part in Identifier().Matches(name))
        {
            var word = part.Value;
            if (TypeParameter().IsMatch(word)) continue;
            if (word.Length < 2) continue;
            AddWord(word, terms);
        }
    }

    private static void AddWord(string word, List<string> terms)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length < 2 || StopWords.Contains(lower)) return;

        var stem = Stem(lower);
        terms.Add(stem);
        if (!string.Equals(lower, word, StringComparison.Ordinal) && !string.Equals(stem, lower, StringComparison.Ordinal)) terms.Add(lower);
    }

    /// <summary>A light, deterministic stem for lowercase English words; identifiers are never passed here whole.</summary>
    public static string Stem(string word)
    {
        if (word.Length <= 3 || !word.All(char.IsAsciiLetterLower)) return word;

        if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 4) word = word[..^3] + "y";
        else if (word.EndsWith("sses", StringComparison.Ordinal)) word = word[..^2];
        else if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal) && !word.EndsWith("is", StringComparison.Ordinal)) word = word[..^1];

        if (word.EndsWith("ing", StringComparison.Ordinal) && word.Length > 6) word = word[..^3];
        else if (word.EndsWith("ed", StringComparison.Ordinal) && word.Length > 5) word = word[..^2];

        if (word.EndsWith('e') && word.Length > 4) word = word[..^1];

        return word;
    }

    // A section number (§9, § 3.1.4), a number or version (2.1.0), or a name: an identifier with
    // optional type arguments (two levels deep) and dotted members, each optionally generic.
    [GeneratedRegex(@"§\s?(?<section>\d+(?:\.\d+)*)|(?<number>\b\d+(?:\.\d+)*\b)|(?<name>[A-Za-z_][A-Za-z0-9_]*(?:<(?:[^<>\n]|<[^<>\n]*>){1,80}>)?(?:\.[A-Za-z_][A-Za-z0-9_]*(?:<(?:[^<>\n]|<[^<>\n]*>){1,80}>)?)*)")]
    private static partial Regex Token();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^T(?:[A-Z][A-Za-z0-9]*)?$")]
    private static partial Regex TypeParameter();

    [GeneratedRegex(@"\b0\d{3}\b")]
    private static partial Regex PaddedNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
