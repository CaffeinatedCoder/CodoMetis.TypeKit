using System.Text.RegularExpressions;

namespace ConsumerSmokeTest;

/// <summary>
/// What docs/walkthrough.md states in its C# comments: the answers, the log line and the SQL of the orders service
/// its blocks show. The smoke test's walkthrough consumer is that service, and test/CodoMetis.TypeKit.Samples compiles
/// this file for WalkthroughTests, which runs the service in memory; both compare what it gives with this.
/// </summary>
/// <remarks>
/// A statement is the comment after the one line of C# whose code holds a fragment, or the comment lines right below
/// that line. An ellipsis (U+2026) in it stands for any text, such as an order's id or a trace id. The expected text
/// is read from the page, never written into a check, so a changed answer is corrected in the page's comment: and
/// ReadmeSampleTests holds the page's blocks to the consumer's sources, so the line read here is the line it runs.
/// </remarks>
internal static partial class WalkthroughPage
{
    public const string Path = "docs/walkthrough.md";

    private const char Ellipsis = (char)0x2026;

    /// <summary>The statement for <paramref name="fragment"/>, one entry per line; throws when no single line holds it, or it states nothing.</summary>
    public static IReadOnlyList<string> Stated(string page, string fragment)
    {
        var lines = CSharpLines(page).ToArray();
        var matches = Enumerable.Range(0, lines.Length).Where(i => CodeOf(lines[i]).Contains(fragment, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"{Path} should have one line of C# holding '{fragment}', but has {matches.Length}.");

        if (CommentOf(lines[matches[0]]) is { } comment) return [comment];

        var below = lines.Skip(matches[0] + 1).TakeWhile(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                         .Select(line => line.TrimStart()[2..].Trim()).ToArray();
        return below.Length > 0 ? below : throw new InvalidOperationException($"{Path} states nothing for '{fragment}'.");
    }

    /// <summary>The answers the page states: each <c>Answer</c> or <c>string</c> declared on a line with a comment, by the variable's name.</summary>
    public static IReadOnlyList<string> Declarations(string page) =>
    [
        .. CSharpLines(page).Where(line => CommentOf(line) is not null)
                            .Select(line => Declaration().Match(CodeOf(line)))
                            .Where(match => match.Success)
                            .Select(match => match.Groups["name"].Value)
    ];

    /// <summary>The fragment that finds the statement of a declared answer: its name and the assignment.</summary>
    public static string Answer(string name) => $" {name} = ";

    /// <summary>Whether <paramref name="value"/> is the one-line statement, whole.</summary>
    public static bool Is(IReadOnlyList<string> stated, string value) => stated.Count == 1 && Pattern(stated[0], whole: true).IsMatch(value);

    /// <summary>Whether the stated lines are consecutive lines of <paramref name="lines"/>, each ending in its statement.</summary>
    public static bool Within(IReadOnlyList<string> stated, IReadOnlyList<string> lines)
    {
        var patterns = stated.Select(line => Pattern(line, whole: false)).ToArray();
        return Enumerable.Range(0, Math.Max(0, lines.Count - patterns.Length + 1))
                         .Any(start => patterns.Select((pattern, i) => pattern.IsMatch(lines[start + i].Trim())).All(matched => matched));
    }

    private static Regex Pattern(string statement, bool whole) =>
        new((whole ? "^" : "") + string.Join(".*?", statement.Split(Ellipsis).Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);

    private static IEnumerable<string> CSharpLines(string page)
    {
        var inCSharp = false;
        foreach (var line in page.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inCSharp = !inCSharp && line.TrimStart()[3..].Trim() is "csharp" or "cs";
            else if (inCSharp) yield return line;
        }
    }

    private static string CodeOf(string line) => (CommentStart(line) is var start and >= 0 ? line[..start] : line).Trim();

    // The comment after code on the same line; a line that is only a comment has none.
    private static string? CommentOf(string line) =>
        CommentStart(line) is var start and >= 0 && line[..start].Trim().Length > 0 ? line[(start + 2)..].Trim() : null;

    // Where a line's // comment starts, outside any string literal.
    private static int CommentStart(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (inString && line[i] == '\\') i++;
            else if (line[i] == '"') inString = !inString;
            else if (!inString && line[i] == '/' && line[i + 1] == '/') return i;
        }

        return -1;
    }

    [GeneratedRegex(@"^(?:Answer|string) (?<name>\w+) = ")]
    private static partial Regex Declaration();
}
