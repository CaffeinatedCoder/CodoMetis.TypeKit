using System.Net;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every link into the repository from the root README, the package READMEs, the <c>docs/</c> pages
/// and the <c>design/</c> pages opens a file or directory that exists, and every anchor in one is a
/// heading of the file it points at, derived as GitHub derives it. That covers a relative link, an
/// in-page <c>#anchor</c>, and an absolute GitHub address on <c>main</c>, which the package READMEs
/// use, wherever it stands: in prose, a list or a table. A link to a renamed page is a 404; a link to
/// a renamed heading opens the top of the page, silently, which is the worse of the two.
/// </summary>
public sealed partial class DocumentationLinkTests
{
    /// <summary>A file on <c>main</c> at this address is a file of this repository, checked like a relative link.</summary>
    private const string RepositoryAddress = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/";

    private static readonly Lazy<IReadOnlyList<Link>> Links = new(() =>
        [.. ReadmeSamples.Readmes().Concat(DesignPages())
                         .SelectMany(page => RepositoryLinks(page, File.ReadAllText(Path.Combine(Repository.Root, page))))
                         .DistinctBy(link => link.Location)]);

    /// <summary>The design notes, the decision records and the roadmap under <c>design/</c>: linked like the pages, compiled by nothing.</summary>
    private static IEnumerable<string> DesignPages() =>
        Directory.EnumerateFiles(Path.Combine(Repository.Root, "design"), "*.md", SearchOption.AllDirectories)
                 .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
                 .Order(StringComparer.Ordinal);

    public static TheoryData<string> Locations => [.. Links.Value.Select(link => link.Location)];

    [Fact]
    public void The_pages_link_each_other() => Links.Value.Count.ShouldBeGreaterThanOrEqualTo(60, string.Join(", ", Links.Value.Select(link => link.Location)));

    /// <summary>
    /// Each kind of link is found in the pages. Measured 2026-10-14, the reader once skipped every
    /// in-page anchor and every absolute address, and a broken one passed with all the others.
    /// </summary>
    [Theory]
    [InlineData(LinkKind.Relative, 60)]
    [InlineData(LinkKind.InPage, 10)]
    [InlineData(LinkKind.Absolute, 10)]
    public void Each_kind_of_link_is_found(LinkKind kind, int floor) =>
        Links.Value.Count(link => link.Kind == kind).ShouldBeGreaterThanOrEqualTo(floor, $"Too few {kind} links read from the pages: the reader no longer finds them.");

    [Theory]
    [MemberData(nameof(Locations))]
    public void A_link_opens_a_file_and_its_anchor_is_a_heading(string location)
    {
        var link   = Links.Value.Single(candidate => candidate.Location == location);
        var target = Path.Combine(Repository.Root, link.File);

        (File.Exists(target) || Directory.Exists(target)).ShouldBeTrue($"{location} links {link.File}, which does not exist.");

        if (link.Anchor is null) return;

        Path.GetExtension(target).ShouldBe(".md", $"{location} links #{link.Anchor} in {link.File}, which has no headings: it is no Markdown page.");

        var headings = Headings(File.ReadAllText(target));
        headings.ShouldContain(link.Anchor, $"{location} links #{link.Anchor}, which is no heading of {link.File}. The headings are: {string.Join(", ", headings)}.");
    }

    public static TheoryData<string> PackageReadmes => [.. Repository.ShippingProjects().Select(project => $"src/{project}/README.md")];

    /// <summary>
    /// A package README is packed into its package, and nuget.org renders it there, where a link
    /// relative to the repository opens nothing. It links another file by its absolute GitHub address,
    /// as every other package README does; an in-page anchor stays relative.
    /// </summary>
    [Theory]
    [MemberData(nameof(PackageReadmes))]
    public void A_package_README_links_other_files_by_their_absolute_address(string readme)
    {
        PackageReadmes.Count.ShouldBeGreaterThanOrEqualTo(5);
        File.Exists(Path.Combine(Repository.Root, readme)).ShouldBeTrue($"{readme} does not exist.");

        Links.Value.Where(link => link.Page == readme && link.Kind == LinkKind.Relative).Select(link => link.Location).ShouldBeEmpty(
            $"{readme} is packed into its package, where a relative link opens nothing on nuget.org. Link {RepositoryAddress}blob/main/... instead.");
    }

    /// <summary>
    /// The positive control: relative links, in-page anchors and absolute addresses into the
    /// repository are found in prose, lists and tables; other sites and code are left alone; and the
    /// headings' anchors are GitHub's, entities, inline code and duplicates included.
    /// </summary>
    [Fact]
    public void Links_and_headings_are_read_as_GitHub_reads_them()
    {
        const string markdown =
            """
            # Title

            See [the plan](../design/plan.md#2-phases), [EF Core](ef-core.md), [here](#the-surface) and [GitHub](https://github.com/x).

            - In a list: [Option](#optiont "Option"), and [the base README](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#resultt-terror-and-resultterror).

            | Topic | Page |
            |---|---|
            | Surface | [the second surface](#the-surface-1) and [the docs](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/tree/main/docs) |

            The workflow is at https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/actions, and `list[0](#not-a-link)` is code.

            ```bash
            # Not a heading, and [not a link](nowhere.md)
            ```

            ## Option&lt;T&gt;

            ## Result&lt;T, TError&gt; and Result&lt;TError&gt;

            ## The surface

            ## The surface

            ### `Option<T>.Match` and [its overloads](option.md) ###

            ### CMTK0010
            """;

        RepositoryLinks("docs/page.md", markdown).Select(link => (link.Kind, link.File, link.Anchor)).ShouldBe(
        [
            (LinkKind.Relative, "design/plan.md", "2-phases"),
            (LinkKind.Relative, "docs/ef-core.md", null),
            (LinkKind.InPage, "docs/page.md", "the-surface"),
            (LinkKind.InPage, "docs/page.md", "optiont"),
            (LinkKind.Absolute, "src/CodoMetis.TypeKit/README.md", "resultt-terror-and-resultterror"),
            (LinkKind.InPage, "docs/page.md", "the-surface-1"),
            (LinkKind.Absolute, "docs", null),
            (LinkKind.Relative, "docs/option.md", null),
        ]);

        Headings(markdown).ShouldBe(["title", "optiont", "resultt-terror-and-resultterror", "the-surface", "the-surface-1", "optiontmatch-and-its-overloads", "cmtk0010"]);
    }

    public enum LinkKind { Relative, InPage, Absolute }

    /// <summary>The page's links into the repository, in order, with the file each opens relative to the root.</summary>
    private static IReadOnlyList<Link> RepositoryLinks(string page, string markdown)
    {
        var text  = WithoutCode(markdown.ReplaceLineEndings("\n"));
        var links = new List<(int Index, Link Link)>();

        foreach (Match match in MarkdownLink().Matches(text))
        {
            var target = match.Groups["target"].Value;
            if (target.StartsWith("http", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal)) continue;

            var (path, anchor) = Split(target);
            var kind = path.Length == 0 ? LinkKind.InPage : LinkKind.Relative;
            var file = kind == LinkKind.InPage
                ? page
                : Path.GetRelativePath(Repository.Root, Path.GetFullPath(Path.Combine(Repository.Root, Path.GetDirectoryName(page) ?? "", path))).Replace('\\', '/');

            links.Add((match.Index, new Link(page, LineOf(text, match.Index), target, kind, file, anchor)));
        }

        foreach (Match match in RepositoryFile().Matches(text))
        {
            var target = match.Value.TrimEnd('.', ',', ';', ':');
            var (path, anchor) = Split(target[(match.Groups["path"].Index - match.Index)..]);

            links.Add((match.Index, new Link(page, LineOf(text, match.Index), target, LinkKind.Absolute, path.TrimEnd('/'), anchor)));
        }

        return [.. links.OrderBy(link => link.Index).Select(link => link.Link)];

        static (string Path, string? Anchor) Split(string target) =>
            target.Split('#', 2) is var parts
                ? (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null)
                : throw new InvalidOperationException();
    }

    /// <summary>
    /// The headings' anchors as GitHub derives them, which is github-slugger over the heading's
    /// rendered text: inline code kept as written, a link reduced to its text, tags dropped, entities
    /// decoded; then lowercased, punctuation dropped, each space a hyphen, and a repeated anchor
    /// suffixed <c>-1</c>, <c>-2</c>. A line in a fenced code block is no heading.
    /// </summary>
    private static IReadOnlyList<string> Headings(string markdown)
    {
        var anchors     = new List<string>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in Heading().Matches(WithoutFencedCode(markdown.ReplaceLineEndings("\n"))))
        {
            var slug   = NotInASlug().Replace(RenderedText(match.Groups["text"].Value).ToLowerInvariant(), "").Replace(' ', '-');
            var anchor = slug;

            while (occurrences.ContainsKey(anchor))
                anchor = $"{slug}-{++occurrences[slug]}";

            occurrences[anchor] = 0;
            anchors.Add(anchor);
        }

        return anchors;
    }

    /// <summary>A heading's text as GitHub renders it: code spans verbatim, everything else with its Markdown and HTML resolved.</summary>
    private static string RenderedText(string heading) =>
        string.Concat(CodeSpan().Split(heading.Trim()).Select((part, index) => index % 2 == 1
            ? part.Trim()
            : WebUtility.HtmlDecode(BackslashEscape().Replace(HtmlTag().Replace(Image().Replace(LinkText().Replace(part, "${text}"), ""), ""), "${character}"))));

    /// <summary>Fenced code blocks and code spans blanked out, line breaks and positions kept, so neither yields a link.</summary>
    private static string WithoutCode(string markdown) =>
        CodeSpan().Replace(WithoutFencedCode(markdown), span => new string(' ', span.Length));

    /// <summary>Every line of a fenced code block, fences included, emptied: the lines stay, so line numbers do.</summary>
    private static string WithoutFencedCode(string markdown)
    {
        var lines = markdown.Split('\n');
        string? fence = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var opening = Fence().Match(lines[i]);

            if (fence is null && opening.Success) fence = opening.Groups["fence"].Value;
            else if (fence is not null && opening.Success && opening.Groups["fence"].Value[0] == fence[0] && opening.Groups["fence"].Length >= fence.Length && string.IsNullOrWhiteSpace(opening.Groups["info"].Value)) fence = null;
            else if (fence is null) continue;

            lines[i] = "";
        }

        return string.Join('\n', lines);
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private sealed record Link(string Page, int Line, string Target, LinkKind Kind, string File, string? Anchor)
    {
        public string Location => $"{Page}:{Line} -> {Target}";
    }

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"https://github\.com/CaffeinatedCoder/CodoMetis\.TypeKit/(?:blob|tree)/main/(?<path>[^)\s>""]*)")]
    private static partial Regex RepositoryFile();

    [GeneratedRegex(@"^ {0,3}#{1,6}(?:[ \t]+(?<text>.*?))?(?:[ \t]+#+)?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*(?<fence>`{3,}|~{3,})(?<info>.*)$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"`+(.+?)`+")]
    private static partial Regex CodeSpan();

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
