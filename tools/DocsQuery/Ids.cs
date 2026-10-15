using System.Globalization;
using System.Text.RegularExpressions;

namespace DocsQuery;

/// <summary>
/// The repository's identifiers, as its pages write them, and their canonical keys.
/// </summary>
/// <remarks>
/// A decision record is <c>design/adr/NNNN-slug.md</c>; prose says "decision 61" or "decisions 74 to
/// 76" and links the file. Its key is <c>adr:0061</c>, and it prints as <c>0061</c>. A diagnostic
/// is <c>CMTK0004</c>, key <c>cmtk0004</c>. A verified fact is <c>design/knowledge/KB-NNN-slug.md</c>,
/// written <c>KB-007</c>; its key is <c>kb:007</c>, and it prints as <c>KB-007</c>.
/// </remarks>
internal static partial class Ids
{
    public static string RecordKey(int number) => $"adr:{number.ToString("D4", CultureInfo.InvariantCulture)}";

    public static string DiagnosticKey(string id) => id.ToLowerInvariant();

    public static string FactKey(int number) => $"kb:{number.ToString("D3", CultureInfo.InvariantCulture)}";

    public static bool IsFactKey(string key) => key.StartsWith("kb:", StringComparison.Ordinal);

    public static bool IsRecordKey(string key) => key.StartsWith("adr:", StringComparison.Ordinal);

    public static bool IsDiagnosticKey(string key) => DiagnosticPattern().IsMatch(key);

    /// <summary>How a key prints: <c>0061</c> for a record, <c>CMTK0004</c> for a diagnostic, <c>KB-007</c> for a fact.</summary>
    public static string Display(string key) =>
        IsRecordKey(key) ? key[4..] : IsDiagnosticKey(key) ? key.ToUpperInvariant() : IsFactKey(key) ? $"KB-{key[3..]}" : key;

    /// <summary>The record number of a file name <c>NNNN-slug.md</c>, or null.</summary>
    public static int? RecordFile(string fileName) =>
        RecordFileName().Match(fileName) is { Success: true } match ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>The fact number of a file name <c>KB-NNN-slug.md</c>, or null.</summary>
    public static int? FactFile(string fileName) =>
        FactFileName().Match(fileName) is { Success: true } match ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>A mention of an identifier in text.</summary>
    internal readonly record struct Mention(int Index, int Length, string Key);

    /// <summary>
    /// The decisions, diagnostics and facts a text names: <c>CMTK0004</c>, <c>decision 61</c>,
    /// <c>decisions 74, 75 and 76</c>, <c>ADR-0068</c>, <c>KB-007</c>.
    /// </summary>
    public static IEnumerable<Mention> Mentions(string text)
    {
        foreach (Match match in DiagnosticPattern().Matches(text))
            yield return new Mention(match.Index, match.Length, DiagnosticKey(match.Value));

        foreach (Match match in DecisionPhrase().Matches(text))
        {
            foreach (Capture number in match.Groups["number"].Captures)
                yield return new Mention(match.Index, match.Length, RecordKey(int.Parse(number.Value, CultureInfo.InvariantCulture)));
        }

        foreach (Match match in RecordAbbreviation().Matches(text))
            yield return new Mention(match.Index, match.Length, RecordKey(int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)));

        foreach (Match match in FactPattern().Matches(text))
            yield return new Mention(match.Index, match.Length, FactKey(int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// What a user typed as an id, as a key: <c>CMTK0010</c>, <c>61</c>, <c>0061</c>, <c>decision 61</c>,
    /// <c>ADR-0061</c>, <c>0061-slug.md</c>, <c>KB-007</c>, <c>KB007</c>, <c>KB-007-slug.md</c>. Null for
    /// anything else (a path, an anchor, a heading).
    /// </summary>
    public static string? Parse(string text)
    {
        text = text.Trim();

        if (DiagnosticPattern().Match(text) is { Success: true } diagnostic && diagnostic.Length == text.Length) return DiagnosticKey(text);
        if (TypedFact().Match(text) is { Success: true } fact) return FactKey(int.Parse(fact.Groups["number"].Value, CultureInfo.InvariantCulture));
        if (FactFile(Path.GetFileName(text)) is { } factNumber && !text.Contains('#')) return FactKey(factNumber);
        if (TypedRecord().Match(text) is { Success: true } record) return RecordKey(int.Parse(record.Groups["number"].Value, CultureInfo.InvariantCulture));
        if (RecordFile(Path.GetFileName(text)) is { } number && !text.Contains('#')) return RecordKey(number);

        return null;
    }

    [GeneratedRegex(@"(?i)\bCMTK\d{4}\b")]
    public static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"\b[Dd]ecisions?\s+(?<number>\d{1,4})\b(?:(?:\s*,\s*|\s+and\s+|\s+or\s+|\s+to\s+|\s*[–-]\s*)(?<number>\d{1,4})\b)*")]
    private static partial Regex DecisionPhrase();

    [GeneratedRegex(@"\bADR[- ]?(?<number>\d{1,4})\b")]
    private static partial Regex RecordAbbreviation();

    [GeneratedRegex(@"^(?i:(?:decision|adr|record)[\s-]*)?#?(?<number>\d{1,4})$")]
    private static partial Regex TypedRecord();

    [GeneratedRegex(@"^(?<number>\d{4})-[^/]*\.md$")]
    private static partial Regex RecordFileName();

    [GeneratedRegex(@"\bKB-(?<number>\d{3})\b")]
    private static partial Regex FactPattern();

    [GeneratedRegex(@"^(?i:KB)-?(?<number>\d{1,3})$")]
    private static partial Regex TypedFact();

    [GeneratedRegex(@"^KB-(?<number>\d{3})-[^/]*\.md$")]
    private static partial Regex FactFileName();
}
