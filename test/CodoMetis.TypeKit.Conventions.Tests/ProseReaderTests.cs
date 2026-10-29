using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// A prose-only change builds and runs only the test projects of <c>test/Prose.slnf</c> (design/adr/0113). So no test
/// project outside it may read prose: a file under <c>docs/</c>, <c>design/</c>, <c>site/</c> or <c>.github/</c>, or a
/// Markdown file outside <c>src/</c>. One that did would go untested by the very change that breaks it, silently.
/// </summary>
/// <remarks>
/// Read from the sources, by the string literals that name such a path: <c>"docs"</c>, <c>"design"</c>,
/// <c>"site"</c>, <c>".github"</c>, a literal starting with one of them and a slash, and one ending in <c>.md</c>. A
/// package's README is not prose (the selection classes it with its package), so <c>"README.md"</c> passes in a file
/// that also names <c>"src"</c>, as <c>Path.Combine(root, "src", package, "README.md")</c> does, and a literal holding <c>src/</c> passes.
/// A literal that names such a path without reading it is waived below, with the reason.
/// </remarks>
public sealed partial class ProseReaderTests
{
    private const string Filter = "test/Prose.slnf";

    /// <summary>Files whose prose-shaped literals are not reads, each with the reason.</summary>
    private static readonly Dictionary<string, string> Waived = new(StringComparer.Ordinal)
    {
        ["test/CodoMetis.TypeKit.CiLanes.Tests/SelectionTests.cs"] = "paths handed to the classifier and written into a throwaway repository",
        ["test/CodoMetis.TypeKit.BashGuard.Tests/GuardTests.cs"] = "command lines the guard judges, never run",
        ["test/CodoMetis.TypeKit.CiLanes.Tests/GateTests.cs"] = "a job named docs the gate must refuse, and a summary file in the sandbox",
        ["test/CodoMetis.TypeKit.PrHygiene.Tests/HygieneTests.cs"] = "branch names such as docs/plain-voice, which name no file",
    };

    private static IReadOnlyList<string> ProseProjects()
    {
        using var filter = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, Filter)));
        return
        [
            .. filter.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray()
                     .Select(project => Path.GetDirectoryName(project.GetString()!)!.Replace('\\', '/'))
        ];
    }

    [Fact]
    public void The_prose_suite_holds_the_projects_that_read_the_documents()
    {
        var projects = ProseProjects();

        foreach (var project in projects)
            Directory.Exists(Path.Combine(Repository.Root, project)).ShouldBeTrue($"{Filter} names {project}, which does not exist.");
        projects.ShouldContain("test/CodoMetis.TypeKit.Conventions.Tests", $"{Filter} lost the conventions tests, which read every document's links.");
        projects.ShouldContain("test/CodoMetis.TypeKit.Samples", $"{Filter} lost the samples, which compile the documents' code blocks.");
    }

    [Fact]
    public void No_test_project_outside_the_prose_suite_reads_prose()
    {
        var prose = ProseProjects().ToHashSet(StringComparer.Ordinal);
        var readers = new List<string>();

        foreach (var project in Directory.GetDirectories(Path.Combine(Repository.Root, "test")))
        {
            var relative = Path.GetRelativePath(Repository.Root, project).Replace('\\', '/');
            if (prose.Contains(relative) || Directory.GetFiles(project, "*.csproj").Length == 0) continue;

            foreach (var file in SourceFiles(project))
            {
                var path = Path.GetRelativePath(Repository.Root, file).Replace('\\', '/');
                if (Waived.ContainsKey(path)) continue;

                var found = Prose(File.ReadAllText(file)).ToList();
                if (found.Count > 0) readers.Add($"{path} ({string.Join(", ", found)})");
            }
        }

        readers.ShouldBeEmpty(
            $"These test sources name prose, which a prose-only change does not run them for. Add their project to {Filter}, " +
            "or waive the file in ProseReaderTests.Waived with the reason it reads nothing.");
    }

    [Theory]
    [InlineData("""File.ReadAllText(Path.Combine(root, "docs", "json.md"));""", true)]
    [InlineData("""var page = "design/adr/README.md";""", true)]
    [InlineData("""Path.Combine(root, ".github", "workflows", "dotnet.yml")""", true)]
    [InlineData("""Path.Combine(root, "site", "toc.yml")""", true)]
    [InlineData("""Path.Combine(root, "CHANGELOG.md")""", true)]
    [InlineData("""Path.Combine(root, "README.md")""", true)]
    [InlineData("""Path.Combine(root, "src", "CodoMetis.TypeKit", "README.md")""", false)]
    [InlineData("""const string Link = "https://github.com/o/r/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md";""", false)]
    [InlineData("""rule.HelpLinkUri.ShouldEndWith("README.md#cmtk0014");""", false)]
    [InlineData("""var name = "documented";""", false)]
    public void A_literal_that_names_prose_is_found(string source, bool reads) =>
        Prose(source).Any().ShouldBe(reads);

    private static IEnumerable<string> Prose(string source)
    {
        var literals = Literal().Matches(source).Select(match => match.Groups["text"].Value).ToList();
        var namesSrc = literals.Contains("src");

        foreach (var literal in literals)
        {
            var directory = literal is "docs" or "design" or "site" or ".github"
                         || literal.StartsWith("docs/", StringComparison.Ordinal)
                         || literal.StartsWith("design/", StringComparison.Ordinal)
                         || literal.StartsWith("site/", StringComparison.Ordinal)
                         || literal.StartsWith(".github/", StringComparison.Ordinal);
            var markdown = literal.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        && !literal.Contains("src/", StringComparison.Ordinal)
                        && !(literal == "README.md" && namesSrc);

            if (directory || markdown) yield return $"\"{literal}\"";
        }
    }

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                 .Where(file => !Path.GetRelativePath(project, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    // A regular string literal's text; a verbatim or raw one is matched as far as its first quote, which is enough for a path.
    [GeneratedRegex(@"""(?<text>(?:[^""\\\r\n]|\\.)*)""")]
    private static partial Regex Literal();
}
