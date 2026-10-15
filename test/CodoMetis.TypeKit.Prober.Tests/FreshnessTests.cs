namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// The dll refuses to answer from other sources than the ones it was built from, its schemas among them,
/// as every tool does (tools/Directory.Build.targets, tools/Shared/SourceFingerprint.cs).
/// </summary>
public sealed class FreshnessTests
{
    [Fact]
    public void The_stamp_covers_the_schemas() =>
        SourceFingerprint.Embedded.ShouldContain(line => line.StartsWith("tools/Prober/Schemas/", StringComparison.Ordinal) && line.Contains(".json|", StringComparison.Ordinal));

    [Theory]
    [InlineData("tools/Prober/Program.cs")]
    [InlineData("tools/Prober/Schemas/round.schema.json")]
    public void A_stale_dll_refuses_and_names_the_rebuild(string source)
    {
        using var sandbox = new ToolSandbox("Prober", "prober");
        File.AppendAllText(Path.Combine(sandbox.Repo, source), "\n");

        var (code, _, error) = sandbox.Run(arguments: ["status"]);

        code.ShouldBe(3);
        error.ShouldContain($"({source} differs)");
        error.ShouldContain("Run `dotnet build tools/Prober -c ");
    }
}
