using System.Net;
using System.Text.RegularExpressions;

namespace DocsQuery;

/// <summary>A heading: its 1-based line, its level, its rendered text and GitHub's anchor for it.</summary>
internal sealed record Heading(int Line, int Level, string Text, string Anchor);

/// <summary>A fenced code block: the fence's 1-based line, the closing fence's, the info word, its code, and the reason of a <c>not compiled</c> marker above it.</summary>
internal sealed record Fence(int Line, int EndLine, string Language, IReadOnlyList<string> Body, string? OptOut)
{
    public bool IsCSharp => Language.ToLowerInvariant() is "csharp" or "cs" or "c#";
}

/// <summary>A link as written: its 1-based line, its target, its text, and where in the line the whole <c>[text](target)</c> sits.</summary>
internal sealed record Link(int Line, string Target, string Text, int Column, int Length);

/// <summary>One Markdown page, read once: its lines, headings, fences and links, line numbers 1-based.</summary>
internal sealed partial class Page
{
    public const string RepositoryAddress = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/";

    public Page(string path, string text)
    {
        Path      = path;
        Lines     = text.ReplaceLineEndings("\n").Split('\n');
        Fences    = ReadFences(Lines);
        InFence   = new bool[Lines.Length + 2];
        foreach (var fence in Fences)
            for (var line = fence.Line; line <= fence.EndLine; line++) InFence[line] = true;

        Headings = ReadHeadings();
        Links    = ReadLinks();
    }

    public string Path { get; }

    /// <summary>The lines, 0-based in the array; every other number here is 1-based.</summary>
    public string[] Lines { get; }

    public IReadOnlyList<Fence> Fences { get; }

    public bool[] InFence { get; }

    public IReadOnlyList<Heading> Headings { get; }

    public IReadOnlyList<Link> Links { get; }

    public string Title => Headings.FirstOrDefault(heading => heading.Level == 1)?.Text ?? System.IO.Path.GetFileName(Path);

    public string Line(int number) => Lines[number - 1];

    /// <summary>A line with its code spans blanked, columns kept.</summary>
    public string Prose(int number) => CodeSpanSplit().Replace(Line(number), span => new string(' ', span.Length));

    /// <summary>
    /// The page's own <c>Key: value</c> lines right below its title (a record's <c>Date</c> and
    /// <c>Status</c>), and a spike's bold <c>**Status: …**</c> or <c>**Answer: …**</c> near the top;
    /// keys lowercased, with the line each is on.
    /// </summary>
    public IReadOnlyList<(string Key, string Value, int Line)> Metadata()
    {
        var found = new List<(string Key, string Value, int Line)>();
        var title = Headings.FirstOrDefault(heading => heading.Level == 1)?.Line ?? 0;

        for (var line = title + 1; line <= Math.Min(Lines.Length, title + 12); line++)
        {
            if (InFence[line]) break;
            var text = Line(line);
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (PlainField().Match(text) is { Success: true } plain) found.Add((plain.Groups["key"].Value.ToLowerInvariant(), plain.Groups["value"].Value.Trim(), line));
            else if (BoldField().Match(text) is { Success: true } bold) found.Add((bold.Groups["key"].Value.ToLowerInvariant(), bold.Groups["value"].Value.Trim().TrimEnd('.'), line));
        }

        return [.. found.DistinctBy(field => field.Key)];
    }

    /// <summary>
    /// The anchors GitHub gives the headings: github-slugger over the rendered text, code spans kept as
    /// written, links reduced to their text, entities decoded, punctuation dropped, spaces to hyphens,
    /// repeats suffixed <c>-1</c>, <c>-2</c>. The same derivation as the convention test that checks
    /// the links, so an anchor this tool prints is one a link may use.
    /// </summary>
    private IReadOnlyList<Heading> ReadHeadings()
    {
        var headings    = new List<Heading>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < Lines.Length; i++)
        {
            if (InFence[i + 1]) continue;
            if (HeadingLine().Match(Lines[i]) is not { Success: true } match) continue;

            var text   = RenderedText(match.Groups["text"].Value);
            var slug   = NotInASlug().Replace(text.ToLowerInvariant(), "").Replace(' ', '-');
            var anchor = slug;

            while (occurrences.ContainsKey(anchor)) anchor = $"{slug}-{++occurrences[slug]}";

            occurrences[anchor] = 0;
            headings.Add(new Heading(i + 1, match.Groups["hashes"].Length, text, anchor));
        }

        return headings;
    }

    /// <summary>A heading's text as GitHub renders it: code spans verbatim, everything else with its Markdown and HTML resolved.</summary>
    public static string RenderedText(string heading) =>
        string.Concat(CodeSpanSplit().Split(heading.Trim()).Select((part, index) => index % 2 == 1
            ? part.Trim()
            : WebUtility.HtmlDecode(BackslashEscape().Replace(HtmlTag().Replace(Image().Replace(LinkText().Replace(part, "${text}"), ""), ""), "${character}"))));

    /// <summary>Prose with each link reduced to its text and HTML tags dropped: what a reader reads, for the search terms.</summary>
    public static string Readable(string line) => HtmlTag().Replace(LinkText().Replace(line, "${text}"), " ");

    private List<Link> ReadLinks()
    {
        var links = new List<Link>();

        for (var number = 1; number <= Lines.Length; number++)
        {
            if (InFence[number]) continue;
            var prose = Prose(number);

            foreach (Match match in LinkTarget().Matches(prose))
            {
                var target = match.Groups["target"].Value;
                if (target.StartsWith("http", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal)) continue;

                var whole = WholeLink().Matches(prose).FirstOrDefault(link => link.Index <= match.Index && link.Index + link.Length >= match.Index + match.Length);
                links.Add(whole is null
                    ? new Link(number, target, "", match.Index, match.Length)
                    : new Link(number, target, whole.Groups["text"].Value, whole.Index, whole.Length));
            }

            foreach (Match match in RepositoryFile().Matches(prose))
            {
                var target = match.Value.TrimEnd('.', ',', ';', ':');
                links.Add(new Link(number, target, "", match.Index, target.Length));
            }
        }

        return links;
    }

    private static List<Fence> ReadFences(string[] lines)
    {
        var fences = new List<Fence>();

        for (var i = 0; i < lines.Length; i++)
        {
            var opening = FenceOpening().Match(lines[i]);
            if (!opening.Success) continue;

            var marker = opening.Groups["fence"].Value;
            var start  = i;
            var body   = new List<string>();

            for (i++; i < lines.Length && !IsClosingFence(lines[i], marker); i++) body.Add(lines[i]);

            fences.Add(new Fence(start + 1, Math.Min(i + 1, lines.Length), opening.Groups["info"].Value, body, OptOut(lines, start)));
        }

        return fences;
    }

    private static bool IsClosingFence(string line, string fence)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
    }

    private static string? OptOut(string[] lines, int fence)
    {
        var above = fence - 1;
        while (above >= 0 && string.IsNullOrWhiteSpace(lines[above])) above--;

        return above >= 0 && OptOutMarker().Match(lines[above]) is { Success: true } marker ? marker.Groups["reason"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^\s*(?<fence>`{3,}|~{3,})\s*(?<info>[^\s`]*)")]
    private static partial Regex FenceOpening();

    [GeneratedRegex(@"^\s*<!--\s*not compiled\b:?(?<reason>.*?)-->\s*$")]
    private static partial Regex OptOutMarker();

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})(?:[ \t]+(?<text>.*?))?(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(?<key>Date|Status|Superseded by|Supersedes|Context)\s*:\s*(?<value>.+)$")]
    private static partial Regex PlainField();

    [GeneratedRegex(@"^\*\*(?<key>Status|Answer|Decided|Date)\s*:\s*(?<value>[^*]+)\*\*")]
    private static partial Regex BoldField();

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"(?<!!)\[(?<text>[^\]]*)\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex WholeLink();

    [GeneratedRegex(@"https://github\.com/CaffeinatedCoder/CodoMetis\.TypeKit/(?:blob|tree)/main/(?<path>[^)\s>""]*)")]
    public static partial Regex RepositoryFile();

    [GeneratedRegex(@"`+(.+?)`+")]
    private static partial Regex CodeSpanSplit();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"(?<!!)\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkText();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\\(?<character>[!-/:-@\[-`{-~])")]
    private static partial Regex BackslashEscape();

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}\p{Pc} -]")]
    private static partial Regex NotInASlug();
}
