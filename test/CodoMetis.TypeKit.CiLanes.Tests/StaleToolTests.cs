namespace CodoMetis.TypeKit.CiLanes.Tests;

/// <summary>
/// A selection built from other sources than the checkout holds says so and selects nothing: exit 3, the
/// difference, and the command that rebuilds it. A stale selection would skip or run lanes by rules
/// nobody can read in the checkout.
/// </summary>
public sealed class StaleToolTests
{
    [Fact]
    public void A_tool_built_from_its_sources_runs()
    {
        using var sandbox = new ToolSandbox("CiLanes", "ci-lanes");

        sandbox.Run().Code.ShouldBe(2, "a usage error, so the tool ran");
    }

    [Fact]
    public void A_changed_source_stops_it()
    {
        using var sandbox = new ToolSandbox("CiLanes", "ci-lanes");
        File.AppendAllText(Path.Combine(sandbox.Repo, "tools", "CiLanes", "Program.cs"), "\n// edited\n");

        var (code, output, _) = sandbox.Run();

        code.ShouldBe(3);
        output.ShouldStartWith("TOOL STALE: tools/CiLanes was built from other sources than the checkout holds (tools/CiLanes/Program.cs differs)");
        output.ShouldContain("rebuild it: dotnet build tools/CiLanes -c ");
    }
}
