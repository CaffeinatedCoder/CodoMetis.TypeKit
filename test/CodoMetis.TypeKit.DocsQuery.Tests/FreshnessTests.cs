using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>The dll knows the sources it was built from, by content, and tells a difference.</summary>
public sealed class FreshnessTests
{
    private static string SourceDirectory => Path.Combine(RepositoryRoot.Path, Freshness.SourceDirectory);

    /// <summary>
    /// The stamp the build embeds is the manifest the runtime check computes: the same files, the same
    /// hashes. A build target that hashed another set would make every run rebuild, or none.
    /// </summary>
    [Fact]
    public void The_embedded_stamp_is_the_sources_as_they_are()
    {
        Freshness.Embedded.Count.ShouldBeGreaterThanOrEqualTo(8, string.Join('\n', Freshness.Embedded));
        Freshness.Embedded.ShouldContain(line => line.StartsWith(Freshness.ProjectFile + "|", StringComparison.Ordinal));
        Freshness.Differences(Freshness.Embedded, Freshness.Current(SourceDirectory)).ShouldBeEmpty();
    }

    [Fact]
    public void A_changed_an_added_and_a_removed_source_are_differences()
    {
        string[] built = ["A.cs|AAAA", "B.cs|BBBB", "C.cs|CCCC"];
        string[] now   = ["A.cs|aaaa", "B.cs|B0B0", "D.cs|DDDD"];

        Freshness.Differences(built, now).ShouldBe(["B.cs", "C.cs", "D.cs"]);
    }

    [Fact]
    public void The_tool_version_is_the_hash_of_the_stamp() => Freshness.ToolVersion.Length.ShouldBe(64);
}

internal static class RepositoryRoot
{
    public static string Path { get; } = Corpus.FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException($"No {Corpus.SolutionMarker} above {AppContext.BaseDirectory}.");
}
