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

    /// <summary>Each version's section heading: the version, and its date or <c>Unreleased</c>.</summary>
    private static Dictionary<string, string> Sections() =>
        VersionHeading().Matches(Changelog).ToDictionary(match => match.Groups["version"].Value, match => match.Groups["date"].Value);

    [GeneratedRegex(@"(?m)^## (?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?) — (?<date>Unreleased|\d{4}-\d{2}-\d{2})\s*$")]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"^CMTK\d{4}\s*\|")]
    private static partial Regex RuleRow();
}
