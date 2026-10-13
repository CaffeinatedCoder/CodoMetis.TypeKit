using System.Net;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every relative link in the root README, the package READMEs, the <c>docs/</c> pages and the
/// <c>design/</c> pages opens a file or directory that exists, and every anchor in one is a heading of the file it points at, derived as
/// GitHub derives it. A link to a renamed page is a 404; a link to a renamed heading opens the top
/// of the page, silently, which is the worse of the two.
/// </summary>
public sealed partial class DocumentationLinkTests
{
    private static readonly Lazy<IReadOnlyList<Link>> Links = new(() =>
        [.. ReadmeSamples.Readmes().Concat(DesignPages()).SelectMany(page => RelativeLinks(page, File.ReadAllText(Path.Combine(Repository.Root, page))))]);

    /// <summary>The design notes, the decision records and the roadmap under <c>design/</c>: linked like the pages, compiled by nothing.</summary>
    private static IEnumerable<string> DesignPages() =>
        Directory.EnumerateFiles(Path.Combine(Repository.Root, "design"), "*.md", SearchOption.AllDirectories)
                 .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
                 .Order(StringComparer.Ordinal);

    public static TheoryData<string> Locations => [.. Links.Value.Select(link => link.Location)];

    [Fact]
    public void The_pages_link_each_other() => Links.Value.Count.ShouldBeGreaterThanOrEqualTo(60, string.Join(", ", Links.Value.Select(link => link.Location)));

    [Theory]
    [MemberData(nameof(Locations))]
    public void A_relative_link_opens_a_file_and_its_anchor_is_a_heading(string location)
    {
        var link = Links.Value.Single(candidate => candidate.Location == location);
        var target = Path.GetFullPath(Path.Combine(Repository.Root, Path.GetDirectoryName(link.Page) ?? "", link.Path));

        (File.Exists(target) || Directory.Exists(target)).ShouldBeTrue($"{location} links {link.Path}, which does not exist.");

        if (link.Anchor is null) return;

        var headings = Headings(File.ReadAllText(target));
        headings.ShouldContain(link.Anchor, $"{location} links #{link.Anchor}, which is no heading of {link.Path}. The headings are: {string.Join(", ", headings)}.");
    }

    /// <summary>The positive control: links with and without anchors are found, external and in-page links are left alone, and the slug is GitHub's.</summary>
    [Fact]
    public void Links_and_headings_are_read_as_GitHub_reads_them()
    {
        const string markdown =
            """
            # Title

            See [the plan](design/plan.md#2-phases), [EF Core](ef-core.md), [here](#the-surface) and [GitHub](https://github.com/x).

            ## Option&lt;T&gt;

            ## Result&lt;T, TError&gt; and Result&lt;TError&gt;

            ## The surface

            ### CMTK0010
            """;

        RelativeLinks("docs/page.md", markdown).Select(link => (link.Path, link.Anchor)).ShouldBe([("design/plan.md", "2-phases"), ("ef-core.md", null)]);
        Headings(markdown).ShouldBe(["title", "optiont", "resultt-terror-and-resultterror", "the-surface", "cmtk0010"]);
    }

    private static IReadOnlyList<Link> RelativeLinks(string page, string markdown) =>
    [
        .. MarkdownLink().Matches(markdown.ReplaceLineEndings("\n"))
                         .Select(match => (Target: match.Groups["target"].Value, Line: markdown.ReplaceLineEndings("\n")[..match.Index].Count(c => c == '\n') + 1))
                         .Where(link => !link.Target.StartsWith("http", StringComparison.Ordinal) && !link.Target.StartsWith('#'))
                         .Select(link => link.Target.Split('#', 2) is var parts
                             ? new Link(page, link.Line, parts[0], parts.Length > 1 ? parts[1] : null)
                             : throw new InvalidOperationException())
    ];

    /// <summary>The headings' anchors as GitHub derives them: entities decoded, lowercased, punctuation dropped, spaces to hyphens.</summary>
    private static IReadOnlyList<string> Headings(string markdown) =>
    [
        .. Heading().Matches(markdown.ReplaceLineEndings("\n"))
                    .Select(match => WebUtility.HtmlDecode(match.Groups["text"].Value).Trim().ToLowerInvariant())
                    .Select(text => NotInASlug().Replace(text, "").Replace(' ', '-'))
    ];

    private sealed record Link(string Page, int Line, string Path, string? Anchor)
    {
        public string Location => $"{Page}:{Line} -> {Path}{(Anchor is null ? "" : $"#{Anchor}")}";
    }

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^#{1,6} (?<text>.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"[^\p{L}\p{N} _-]")]
    private static partial Regex NotInASlug();
}
