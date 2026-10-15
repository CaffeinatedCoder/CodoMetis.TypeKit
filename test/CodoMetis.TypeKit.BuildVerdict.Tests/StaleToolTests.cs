namespace CodoMetis.TypeKit.BuildVerdict.Tests;

/// <summary>
/// A verdict tool built from other sources than the checkout holds says so and runs nothing: exit 3, the
/// difference, and the command that rebuilds it. Each case edits its own sandbox's sources.
/// </summary>
public sealed class StaleToolTests
{
    private static (int Code, string Output) Usage(ToolSandbox sandbox)
    {
        var (code, output, _) = sandbox.Run(arguments: "frob");
        return (code, output);
    }

    [Fact]
    public void A_tool_built_from_its_sources_runs()
    {
        using var sandbox = new ToolSandbox("BuildVerdict", "build-verdict");

        Usage(sandbox).Code.ShouldBe(2, "a usage error, so the tool ran");
    }

    [Fact]
    public void A_changed_source_stops_it()
    {
        using var sandbox = new ToolSandbox("BuildVerdict", "build-verdict");
        File.AppendAllText(Path.Combine(sandbox.Repo, "tools", "BuildVerdict", "Program.cs"), "\n// edited\n");

        var (code, output) = Usage(sandbox);

        code.ShouldBe(3);
        output.ShouldStartWith("TOOL STALE: tools/BuildVerdict was built from other sources than the checkout holds (tools/BuildVerdict/Program.cs differs)");
        output.ShouldContain("rebuild it: dotnet build tools/BuildVerdict -c ");
    }

    [Fact]
    public void A_changed_shared_source_stops_it()
    {
        using var sandbox = new ToolSandbox("BuildVerdict", "build-verdict");
        File.AppendAllText(Path.Combine(sandbox.Repo, "tools", "Shared", "SourceFingerprint.cs"), "\n// edited\n");

        var (code, output) = Usage(sandbox);

        code.ShouldBe(3);
        output.ShouldContain("(tools/Shared/SourceFingerprint.cs differs)");
    }

    [Fact]
    public void A_new_source_stops_it()
    {
        using var sandbox = new ToolSandbox("BuildVerdict", "build-verdict");
        File.WriteAllText(Path.Combine(sandbox.Repo, "tools", "BuildVerdict", "More.cs"), "// new\n");

        var (code, output) = Usage(sandbox);

        code.ShouldBe(3);
        output.ShouldContain("(tools/BuildVerdict/More.cs differs)");
    }

    [Fact]
    public void A_removed_source_stops_it()
    {
        using var sandbox = new ToolSandbox("BuildVerdict", "build-verdict");
        File.Delete(Path.Combine(sandbox.Repo, "tools", "BuildVerdict", "BuildVerdict.csproj"));

        var (code, output) = Usage(sandbox);

        code.ShouldBe(3);
        output.ShouldContain("(tools/BuildVerdict/BuildVerdict.csproj differs)");
    }

    [Fact]
    public void A_file_in_bin_or_obj_is_no_source()
    {
        using var sandbox = new ToolSandbox("BuildVerdict", "build-verdict");
        Directory.CreateDirectory(Path.Combine(sandbox.Repo, "tools", "BuildVerdict", "obj"));
        File.WriteAllText(Path.Combine(sandbox.Repo, "tools", "BuildVerdict", "obj", "Generated.cs"), "// generated\n");

        Usage(sandbox).Code.ShouldBe(2);
    }
}
