using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// The candidate registry (<c>test/oracles/candidates.json</c>) names only registered oracles and tracks every entry
/// to an issue or to a labelled failure (design/adr/0112). A misspelt oracle name would match nothing, and the row
/// would silently keep failing as unknown.
/// </summary>
public sealed class OracleCandidateTests
{
    [Fact]
    public void Every_candidate_names_a_registered_oracle_and_a_row()
    {
        var oracles = OracleCatalogue.Oracles.Select(oracle => oracle.Class).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var candidate in Candidates.All)
        {
            if (!oracles.Contains(candidate.Oracle)) problems.Add($"'{candidate.Row}' names oracle '{candidate.Oracle}', which oracles.json does not register");
            if (candidate.Row.Trim().Length < 3) problems.Add($"a candidate of {candidate.Oracle} has no row text");
            if (candidate.Family.Trim().Length == 0) problems.Add($"'{candidate.Row}' has no family label");
            if (candidate.Method is { } method && !Regex.IsMatch(File.ReadAllText(SourceOf(candidate.Oracle)), $@"\b{Regex.Escape(method)}\s*\("))
                problems.Add($"'{candidate.Row}' is scoped to method '{method}', which {candidate.Oracle} does not declare");
            if (candidate.Issue.Length > 0 && !candidate.Issue.StartsWith('#') && !candidate.Issue.StartsWith("http", StringComparison.Ordinal))
                problems.Add($"'{candidate.Row}' has an issue that is neither '#<n>' nor a URL: '{candidate.Issue}'");
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Candidates_are_unique_per_oracle() =>
        Candidates.All.GroupBy(candidate => (candidate.Oracle, candidate.Row)).Where(group => group.Count() > 1)
                  .Select(group => $"{group.Key.Oracle}: '{group.Key.Row}'")
                  .ShouldBeEmpty("Duplicate candidates.");

    private static string SourceOf(string oracle) =>
        Directory.EnumerateFiles(Path.Combine(OracleCatalogue.Root, "test"), oracle + ".cs", SearchOption.AllDirectories)
                 .First(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
