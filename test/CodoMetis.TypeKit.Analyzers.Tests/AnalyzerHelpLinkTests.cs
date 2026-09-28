using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// Every rule links to its own section of the analyzer README, which an IDE opens from the
/// diagnostic. A link to a heading that does not exist opens the top of the page, silently, so the
/// anchor is held to the README's headings.
/// </summary>
public sealed class AnalyzerHelpLinkTests
{
    private const string Readme = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Analyzers/README.md";

    public static TheoryData<string> Analyzers() =>
        [.. typeof(ForbiddenDefaultInitializationAnalyzer).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
                .Select(type => type.Name)];

    [Theory]
    [MemberData(nameof(Analyzers))]
    public void Every_rule_links_to_its_section_of_the_README(string analyzer)
    {
        var type     = typeof(ForbiddenDefaultInitializationAnalyzer).Assembly.GetTypes().Single(candidate => candidate.Name == analyzer);
        var instance = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
        var headings = File.ReadAllLines(Path.Combine(RepositoryRoot(), "src", "CodoMetis.TypeKit.Analyzers", "README.md"))
                           .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                           .Select(line => line[3..].Trim())
                           .ToHashSet(StringComparer.Ordinal);

        foreach (var rule in instance.SupportedDiagnostics)
        {
            rule.HelpLinkUri.ShouldBe($"{Readme}#{rule.Id.ToLowerInvariant()}", $"{rule.Id} has no link to its README section.");
            headings.ShouldContain(rule.Id, $"The analyzer README has no '## {rule.Id}' heading, so the link from {rule.Id} opens the top of the page.");
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("CodoMetis.TypeKit.slnx not found above the test's directory.");
    }
}
