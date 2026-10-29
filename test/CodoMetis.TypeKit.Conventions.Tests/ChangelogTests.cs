using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The changelog documents the version being built, and a version it dates as released has its
/// analyzer rules recorded as shipped.
/// </summary>
/// <remarks>
/// The release workflow refuses a tag whose section still reads <c>Unreleased</c> and uses the
/// section as the GitHub release's notes, so the heading format is load-bearing. Dating the section
/// is also the moment the analyzer rules ship: their ids and severities become public contract, and
/// RS2008 release tracking records them under <c>## Release &lt;version&gt;</c>. A rule still listed as
/// unshipped in a released version would let the next release change it without notice.
/// </remarks>
public sealed partial class ChangelogTests
{
    private static string Changelog => File.ReadAllText(Path.Combine(Repository.Root, "CHANGELOG.md"));

    /// <summary>The Version property, which every package carries and the release tag is checked against.</summary>
    private static string Version =>
        XDocument.Load(Path.Combine(Repository.Root, "Directory.Build.props")).Descendants("Version").Single().Value.Trim();

    [Fact]
    public void Every_version_heading_names_a_version_and_a_date_or_Unreleased()
    {
        var headings = Regex.Matches(Changelog, @"(?m)^## .*$").Select(match => match.Value.TrimEnd()).ToList();

        headings.ShouldNotBeEmpty("CHANGELOG.md has no version sections.");
        headings.Where(heading => !VersionHeading().IsMatch(heading))
                .ShouldBeEmpty("These headings are not '## <version> — <yyyy-mm-dd>' or '## <version> — Unreleased', which the release workflow reads.");
    }

    [Fact]
    public void The_version_being_built_has_a_section() =>
        Sections().ShouldContainKey(Version, $"CHANGELOG.md has no '## {Version}' section, so the release would have no notes and nothing documents the version.");

    [Fact]
    public void A_released_version_has_shipped_its_analyzer_rules()
    {
        if (!Sections().TryGetValue(Version, out var date) || date == "Unreleased") return;

        var analyzers = Path.Combine(Repository.Root, "src", "CodoMetis.TypeKit.Analyzers");
        var unshipped = File.ReadAllLines(Path.Combine(analyzers, "AnalyzerReleases.Unshipped.md"));
        var shipped   = File.ReadAllText(Path.Combine(analyzers, "AnalyzerReleases.Shipped.md"));

        unshipped.Where(line => RuleRow().IsMatch(line)).ShouldBeEmpty(
            $"CHANGELOG.md dates {Version} as released, but these rules are still unshipped. Move them to AnalyzerReleases.Shipped.md under '## Release {Version}'.");
        shipped.ShouldMatch($@"(?m)^## Release {Regex.Escape(Version)}\s*$",
            $"CHANGELOG.md dates {Version} as released, but AnalyzerReleases.Shipped.md has no '## Release {Version}'.");
    }

    /// <summary>
    /// A dated section is its packages' release notes on nuget.org and its GitHub release's notes, both
    /// frozen with the version, so it links the repository at its own tag. A link at <c>main</c> opens
    /// that day's page, where the section may say something else or the anchor may be gone (#76). A
    /// page written after the tag, such as the 2.1 migration guide, is linked at a full commit id,
    /// which is as frozen. The <c>Unreleased</c> section has no tag yet and links <c>main</c>.
    /// </summary>
    [Fact]
    public void A_released_version_links_the_repository_at_its_tag()
    {
        var links = DatedSections().SelectMany(section => RepositoryLink().Matches(section.Text)
                                       .Select(link => (section.Version, Ref: link.Groups["ref"].Value, Link: link.Value)))
                                   .ToList();

        links.Count.ShouldBeGreaterThanOrEqualTo(30, "Too few links read from the dated sections: the reader no longer finds them.");
        links.Where(link => link.Ref != $"v{link.Version}" && !CommitId().IsMatch(link.Ref))
             .Select(link => $"{link.Version}: {link.Link}")
             .ShouldBeEmpty("These links in a released version's section don't name its tag. Write blob/v<version>/ in place of blob/main/ when you date the section (CONTRIBUTING.md, Releasing).");
    }

    /// <summary>
    /// Package validation compares every pack with the baseline, so ApiCompat refuses a break of the
    /// public surface. A baseline left behind at an older release, or dropped, lets a break of the last
    /// release through without a word. Measured 2026-09-28: with the baseline at 1.0.0, making a member
    /// that 1.0.0 shipped internal fails the pack with CP0002.
    /// <para>
    /// The version being built is not its own baseline, even once its section is dated: the release
    /// commit dates it, and its pack is compared with the release before, the only one on nuget.org yet.
    /// Holding the baseline to the dated version being built made the release commit fail its own pack
    /// (found preparing 1.5.0). Once step 4 raises the version, the version just released is the baseline.
    /// </para>
    /// </summary>
    [Fact]
    public void The_package_validation_baseline_is_the_last_release()
    {
        var baseline = XDocument.Load(Path.Combine(Repository.Root, "Directory.Build.props"))
                                .Descendants("PackageValidationBaselineVersion").SingleOrDefault()?.Value.Trim();
        var released = Sections().Where(section => section.Value != "Unreleased" && section.Key != Version)
                                 .Select(section => section.Key)
                                 .MaxBy(version => System.Version.Parse(version.Split('-')[0]));

        if (released is null) return;

        baseline.ShouldBe(released,
            $"CHANGELOG.md dates {released} as the last release, so Directory.Build.props sets PackageValidationBaselineVersion to it (CONTRIBUTING.md, Releasing, step 4).");
    }

    /// <summary>Each version's section heading: the version, and its date or <c>Unreleased</c>.</summary>
    private static Dictionary<string, string> Sections() =>
        VersionHeading().Matches(Changelog).ToDictionary(match => match.Groups["version"].Value, match => match.Groups["date"].Value);

    /// <summary>Each dated version's section: its heading and everything up to the next one.</summary>
    private static IEnumerable<(string Version, string Text)> DatedSections()
    {
        var headings = VersionHeading().Matches(Changelog);

        return headings.Select((heading, index) =>
                           (heading, end: index + 1 < headings.Count ? headings[index + 1].Index : Changelog.Length))
                       .Where(section => section.heading.Groups["date"].Value != "Unreleased")
                       .Select(section => (section.heading.Groups["version"].Value,
                                           Changelog[section.heading.Index..section.end]));
    }

    [GeneratedRegex(@"(?m)^## (?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?) — (?<date>Unreleased|\d{4}-\d{2}-\d{2})\s*$")]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"https://github\.com/CaffeinatedCoder/CodoMetis\.TypeKit/(?:blob|tree)/(?<ref>[^/\s)]+)/[^\s)]*")]
    private static partial Regex RepositoryLink();

    [GeneratedRegex(@"^[0-9a-f]{40}$")]
    private static partial Regex CommitId();

    [GeneratedRegex(@"^CMTK\d{4}\s*\|")]
    private static partial Regex RuleRow();
}
