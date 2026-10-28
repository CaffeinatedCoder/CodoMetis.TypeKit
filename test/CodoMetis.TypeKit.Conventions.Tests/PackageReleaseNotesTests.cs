using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every package's release notes are its version's section of CHANGELOG.md, in the plain text
/// nuget.org shows, followed by one line linking the whole changelog (#75).
/// </summary>
/// <remarks>
/// nuget.org's Release Notes tab and Visual Studio's Updates tab show the nuspec's
/// <c>releaseNotes</c>, frozen with the version once pushed. The <c>ReleaseNotesFromChangelog</c>
/// target in <c>src/Directory.Build.targets</c> writes it at pack. These read the packed nuspecs and
/// derive the expected text again from the changelog, line by line rather than by the target's
/// expressions, so a target that reads another version's section or stops running fails here. A
/// missing or empty section fails the pack itself, and with it the fixture. The version's section
/// may hold no Markdown at all, which would let a target that leaves Markdown in pass, so the target
/// also runs on a sample changelog of every shape, and on samples it must refuse.
/// <para>
/// Plain text, measured on nuget.org on 2026-10-20: release notes are escaped, each line break becomes
/// <c>&lt;br /&gt;</c>, and a bare URL is linked; the brackets of <c>[text](url)</c>, backticks, <c>**</c>
/// and <c>#</c> reach the page literally (Bogus 35.6.5, Asp.Versioning.Http 10.2.3), and a URL in
/// parentheses is linked without its closing one (RazorEngine 3.10.0).
/// </para>
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed partial class PackageReleaseNotesTests(AnalyzerPackagingTests.Packs packs)
{
    public static TheoryData<string> ShippingPackages => [.. Repository.ShippingProjects()];

    private static XElement Props => XDocument.Load(Path.Combine(Repository.Root, "Directory.Build.props")).Root!;

    private static string Version => Props.Descendants("Version").Single().Value.Trim();

    private static string Footer => $"Full changelog: {Props.Descendants("PackageProjectUrl").Single().Value.Trim()}/blob/main/CHANGELOG.md";

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_release_notes_are_the_versions_changelog_section(string package) =>
        ReleaseNotes(package).ShouldBe(Expected(File.ReadAllText(Path.Combine(Repository.Root, "CHANGELOG.md")), Version),
            $"{package}'s release notes are not CHANGELOG.md's '## {Version}' section in plain text. The ReleaseNotesFromChangelog target (src/Directory.Build.targets) writes them at pack.");

    [Theory]
    [MemberData(nameof(ShippingPackages))]
    public void The_release_notes_carry_no_markdown(string package) =>
        Markdown().Matches(ReleaseNotes(package)).Select(match => match.Value).ShouldBeEmpty(
            $"{package}'s release notes contain Markdown, which nuget.org shows literally.");

    /// <summary>A section between two others, with every shape the changelog writes, and text MSBuild would expand or unescape.</summary>
    private const string Sample = """
        # Changelog

        ## 9.9.10 — Unreleased

        - Later: not this version.

        ## 9.9.9 — 2026-01-02

        ### Fixed

        - Generators: `Parse` on a **span** ([What is generated](https://example.org/g#what)).
        - [`Code` in a link](https://example.org/c?a=1&b=2) and 50%3B; $(Foo) @(Bar) %(Baz) 'q' "d" <a> & b — ü.

        ## 9.9.8 — 2026-01-01

        - Earlier: not this version.
        """;

    private const string SamplePlain = """
        Fixed

        - Generators: Parse on a span (What is generated (https://example.org/g#what)).
        - Code in a link (https://example.org/c?a=1&b=2) and 50%3B; $(Foo) @(Bar) %(Baz) 'q' "d" <a> & b — ü.
        """;

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task The_target_writes_every_shape_as_plain_text(string lineEnding)
    {
        var changelog = Sample.ReplaceLineEndings(lineEnding);
        var expected  = $"{SamplePlain.ReplaceLineEndings("\n")}\n\n{Footer}";

        Expected(changelog, "9.9.9").ShouldBe(expected, "This test's own reading of a section disagrees with the sample's plain text.");

        var (exitCode, output) = await RunTarget(changelog, "9.9.9");

        exitCode.ShouldBe(0, output);
        output.ReplaceLineEndings("\n").Trim().ShouldBe(expected);
    }

    public static TheoryData<string, string, string> Refused => new()
    {
        { "no section",    "## 9.9.8 — 2026-01-01\n\n- Earlier.\n",                                  "has no '## 9.9.9' section" },
        { "empty section", "## 9.9.9 — Unreleased\n\n## 9.9.8 — 2026-01-01\n\n- Earlier.\n",       "'## 9.9.9' section is empty" },
        { "relative link", "## 9.9.9 — Unreleased\n\n- See [the docs](docs/json.md).\n",            "relative link" },
        { "too long",      $"## 9.9.9 — Unreleased\n\n- {new string('x', 35_000)}\n",              "over nuget.org's 35,000" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task The_target_refuses_a_section_nuget_org_cannot_show(string what, string changelog, string message)
    {
        var (exitCode, output) = await RunTarget(changelog, "9.9.9");

        exitCode.ShouldNotBe(0, $"The target accepted a changelog with {what}:{Environment.NewLine}{output}");
        output.ShouldContain(message, Case.Sensitive, $"The target refused a changelog with {what}, but not for that reason:{Environment.NewLine}{output}");
        output.ShouldContain("CHANGELOG.md", Case.Sensitive, "The refusal does not name the changelog.");
    }

    /// <summary>The section, read line by line from the line after its heading up to the next <c>## </c>, in plain text, with the footer.</summary>
    private static string Expected(string changelog, string version)
    {
        var lines   = changelog.ReplaceLineEndings("\n").Split('\n');
        var heading = Array.FindIndex(lines, line => line == $"## {version}" || line.StartsWith($"## {version} ", StringComparison.Ordinal));

        heading.ShouldBeGreaterThanOrEqualTo(0, $"CHANGELOG.md has no '## {version}' section.");

        var section = string.Join('\n', lines.Skip(heading + 1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))).Trim();
        section.ShouldNotBeEmpty($"CHANGELOG.md's '## {version}' section is empty.");

        var plain = section.Split('\n').Select(line =>
            Link().Replace(line.StartsWith("### ", StringComparison.Ordinal) ? line[4..] : line, "$1 ($2)").Replace("`", "").Replace("**", ""));

        return $"{string.Join('\n', plain)}\n\n{Footer}";
    }

    private string ReleaseNotes(string package)
    {
        var notes = packs.Nuspec(package).Descendants()
                         .SingleOrDefault(element => element.Name.LocalName == "releaseNotes" && element.Parent?.Name.LocalName == "metadata");

        notes.ShouldNotBeNull($"{package} has no <releaseNotes> in its nuspec.");
        return notes.Value;
    }

    /// <summary>
    /// The target alone, on one shipping project, against a changelog of the test's own: what it sets
    /// <c>PackageReleaseNotes</c> to, or its refusal. Nothing is built or written beside the project.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunTarget(string changelog, string version)
    {
        var directory = Directory.CreateTempSubdirectory("codometis-typekit-release-notes-");
        try
        {
            var path = Path.Combine(directory.FullName, "CHANGELOG.md");
            await File.WriteAllTextAsync(path, changelog);

            var project = Path.Combine(Repository.Root, "src", "CodoMetis.TypeKit.Serilog", "CodoMetis.TypeKit.Serilog.csproj");
            using var process = Process.Start(new ProcessStartInfo("dotnet",
                ["msbuild", project, "-t:ReleaseNotesFromChangelog", "-getProperty:PackageReleaseNotes", $"-p:ReleaseNotesChangelog={path}", $"-p:Version={version}", "-nodeReuse:false"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, await output + await errors);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex Link();

    /// <summary>Inline code, emphasis, a link, or a heading marker at the start of a line.</summary>
    [GeneratedRegex(@"(?m)`|\*\*|\]\(|^#")]
    private static partial Regex Markdown();
}
