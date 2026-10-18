namespace CodoMetis.TypeKit.PrHygiene.Tests;

/// <summary>
/// A check built from other sources than the checkout holds says so and checks nothing: exit 3, the
/// difference, and the command that rebuilds it. A stale check would pass or fail pull requests by rules
/// nobody can read in the checkout.
/// </summary>
public sealed class StaleToolTests
{
    [Fact]
    public void A_tool_built_from_its_sources_runs()
    {
        using var sandbox = new ToolSandbox("PrHygiene", "pr-hygiene");

        sandbox.Run().Code.ShouldBe(2, "a usage error, so the tool ran");
    }

    [Fact]
    public void A_changed_source_stops_it()
    {
        using var sandbox = new ToolSandbox("PrHygiene", "pr-hygiene");
        File.AppendAllText(Path.Combine(sandbox.Repo, "tools", "PrHygiene", "Program.cs"), "\n// edited\n");

        var (code, output, _) = sandbox.Run();

        code.ShouldBe(3);
        output.ShouldStartWith("TOOL STALE: tools/PrHygiene was built from other sources than the checkout holds (tools/PrHygiene/Program.cs differs)");
        output.ShouldContain("rebuild it: dotnet build tools/PrHygiene -c ");
    }
}
