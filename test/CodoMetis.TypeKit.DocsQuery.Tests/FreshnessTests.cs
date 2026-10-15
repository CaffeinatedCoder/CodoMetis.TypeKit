using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>
/// The dll knows the sources it was built from, by content, and refuses to answer from others, as every
/// tool does (tools/Directory.Build.targets, tools/Shared/SourceFingerprint.cs).
/// </summary>
public sealed class FreshnessTests
{
    /// <summary>
    /// The stamp the build embeds is the manifest the runtime check computes: the same files, the same
    /// hashes. A build target that hashed another set would make every run refuse, or none.
    /// </summary>
    [Fact]
    public void The_embedded_stamp_is_the_sources_as_they_are()
    {
        SourceFingerprint.Embedded.Count.ShouldBeGreaterThanOrEqualTo(8, string.Join('\n', SourceFingerprint.Embedded));
        SourceFingerprint.Embedded.ShouldContain(line => line.StartsWith("tools/DocsQuery/DocsQuery.csproj|", StringComparison.Ordinal));
        SourceFingerprint.Embedded.ShouldContain(line => line.StartsWith("tools/Shared/SourceFingerprint.cs|", StringComparison.Ordinal));
        SourceFingerprint.Differences(SourceFingerprint.Embedded, SourceFingerprint.Current(RepositoryRoot.Path)).ShouldBeEmpty();
    }

    [Fact]
    public void A_changed_an_added_and_a_removed_source_are_differences()
    {
        string[] built = ["A.cs|AAAA", "B.cs|BBBB", "C.cs|CCCC"];
        string[] now   = ["A.cs|aaaa", "B.cs|B0B0", "D.cs|DDDD"];

        SourceFingerprint.Differences(built, now).ShouldBe(["B.cs", "C.cs", "D.cs"]);
    }

    [Fact]
    public void The_tool_version_is_the_hash_of_the_stamp() => SourceFingerprint.ToolVersion.Length.ShouldBe(64);

    [Fact]
    public void A_stale_dll_refuses_and_names_the_rebuild()
    {
        using var sandbox = new ToolSandbox("DocsQuery", "docs-query");
        File.AppendAllText(Path.Combine(sandbox.Repo, "tools", "DocsQuery", "Program.cs"), "\n// edited\n");

        var (code, _, error) = sandbox.Run(arguments: ["outline", "README"]);

        code.ShouldBe(3);
        error.ShouldContain("(tools/DocsQuery/Program.cs differs)");
        error.ShouldContain("Run `dotnet build tools/DocsQuery -c ");
    }
}

internal static class RepositoryRoot
{
    public static string Path { get; } = Corpus.FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException($"No {Corpus.SolutionMarker} above {AppContext.BaseDirectory}.");
}
