using System.Text.Json;

namespace CodoMetis.TypeKit.CiLanes.Tests;

/// <summary>
/// The gate's verdict on a run, from the <c>toJSON(needs)</c> the CI job writes. Each failing case starts from a
/// run that passes and breaks one thing: a failed selection, a cancelled leg, an answer the selection does not
/// write, a selected job that was skipped, and a skipped job that ran (#71, design/adr/0113).
/// </summary>
public sealed class GateTests : IDisposable
{
    private const string LinuxOnly = """["ubuntu-latest"]""";
    private const string LinuxAndWindows = """["ubuntu-latest","windows-latest"]""";
    private const string EveryLeg = """["ubuntu-latest","windows-latest","macos-latest"]""";

    private readonly ToolSandbox _tool = new("CiLanes", "ci-lanes");

    public void Dispose() => _tool.Dispose();

    private sealed record Run(
        string Changes = "success",
        string? Legs = LinuxOnly,
        string? Pack = "false",
        string? Suite = "prose",
        string? Projects = "[]",
        string Test = "success",
        string PackJob = "skipped",
        string Consumer = "skipped");

    private static readonly Run Prose = new();

    private static readonly Run Package = new(Pack: "true", Suite: "full", PackJob: "success", Consumer: "success");

    private static readonly Run Full = Package with { Legs = EveryLeg };

    private const string ReachedProjects = """["test/CodoMetis.TypeKit.Conventions.Tests/CodoMetis.TypeKit.Conventions.Tests.csproj","tools/PrHygiene/PrHygiene.csproj"]""";

    private static readonly Run Reach = Prose with { Legs = LinuxAndWindows, Suite = "reach", Projects = ReachedProjects };

    private (int Code, string Output) Judge(Run run, Action<Dictionary<string, object>>? edit = null)
    {
        var outputs = new Dictionary<string, string>();
        if (run.Legs is not null) outputs["legs"] = run.Legs;
        if (run.Pack is not null) outputs["pack"] = run.Pack;
        if (run.Suite is not null) outputs["suite"] = run.Suite;
        if (run.Projects is not null) outputs["projects"] = run.Projects;

        var needs = new Dictionary<string, object>
        {
            ["changes"] = new { result = run.Changes, outputs },
            ["test"] = new { result = run.Test, outputs = new { } },
            ["pack"] = new { result = run.PackJob, outputs = new { } },
            ["consumer"] = new { result = run.Consumer, outputs = new { } },
        };
        edit?.Invoke(needs);

        var file = Path.Combine(_tool.Work, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(needs));
        var (code, output, _) = _tool.Run(arguments: ["gate", file]);
        return (code, output);
    }

    private void Passes(Run run)
    {
        var (code, output) = Judge(run);
        code.ShouldBe(0, output);
        output.ShouldNotContain("::error::");
    }

    private void Fails(Run run, string problem, Action<Dictionary<string, object>>? edit = null)
    {
        var (code, output) = Judge(run, edit);
        code.ShouldBe(1, output);
        output.ShouldContain($"::error::{problem}");
    }

    [Fact]
    public void A_prose_run_passes_with_the_pack_skipped() => Passes(Prose);

    [Fact]
    public void A_package_run_passes_with_the_pack_run() => Passes(Package);

    [Fact]
    public void An_analyzer_run_passes_with_Windows_and_the_pack() => Passes(Package with { Legs = LinuxAndWindows });

    [Fact]
    public void A_test_run_passes_with_Windows_and_no_pack() => Passes(Prose with { Legs = LinuxAndWindows, Suite = "full" });

    [Theory]
    [InlineData("Prose")]
    [InlineData("all")]
    [InlineData("")]
    public void A_suite_answer_the_selection_does_not_write_fails(string suite) =>
        Fails(Prose with { Suite = suite }, $"The changes job's suite output is '{suite}'");

    [Fact]
    public void The_prose_suite_beside_another_leg_fails() =>
        Fails(Prose with { Legs = LinuxAndWindows }, "The changes job chose the prose suite for a run with more than the Linux leg");

    [Fact]
    public void The_prose_suite_beside_the_pack_fails() =>
        Fails(Package with { Suite = "prose" }, "The changes job chose the prose suite for a run with more than the Linux leg");

    [Fact]
    public void A_run_that_is_not_selected_passes_with_every_leg() => Passes(Full);

    [Fact]
    public void A_reached_suite_passes_on_Linux_and_Windows() => Passes(Reach);

    [Fact]
    public void A_reached_suite_passes_on_Linux_alone() => Passes(Reach with { Legs = LinuxOnly });

    /// <summary>The narrowed suite is held like any other: a leg that was skipped or cancelled fails the run (#268).</summary>
    [Theory]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    [InlineData("failure")]
    public void A_reached_suite_whose_legs_did_not_succeed_fails(string result) =>
        Fails(Reach with { Test = result }, $"The job 'test' was selected and ended '{result}'.");

    [Fact]
    public void A_reached_suite_beside_the_pack_fails() =>
        Fails(Reach with { Pack = "true", PackJob = "success", Consumer = "success" }, "The changes job chose the reached suite for a run with the pack or the macOS leg");

    [Fact]
    public void A_reached_suite_beside_every_leg_fails() =>
        Fails(Reach with { Legs = EveryLeg }, "The changes job chose the reached suite for a run with the pack or the macOS leg");

    /// <summary>An empty filter would build and test nothing, and pass.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("""["CodoMetis.TypeKit.slnx"]""")]
    [InlineData("""["src/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj"]""")]
    [InlineData("""["test/../src/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj"]""")]
    [InlineData("""["test/A.Tests/A.Tests.csproj","test/A.Tests/A.Tests.csproj"]""")]
    public void A_reached_suite_without_its_projects_fails(string projects) =>
        Fails(Reach with { Projects = projects }, "The changes job chose the reached suite with the projects");

    [Theory]
    [InlineData("full")]
    [InlineData("prose")]
    public void Projects_beside_a_suite_that_has_its_own_fail(string suite) =>
        Fails((suite == "full" ? Package : Prose) with { Projects = ReachedProjects }, $"The changes job named projects for the {suite} suite");

    [Theory]
    [InlineData("test/A.Tests/A.Tests.csproj")]
    [InlineData("""{"projects":[]}""")]
    [InlineData("""[1]""")]
    public void A_projects_answer_the_selection_does_not_write_fails(string projects) =>
        Fails(Reach with { Projects = projects }, "The changes job's projects output is");

    [Fact]
    public void A_missing_projects_answer_fails() => Fails(Prose with { Projects = null }, "The changes job's projects output is missing");

    [Theory]
    [InlineData("failure")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    public void A_selection_that_did_not_succeed_fails(string result) =>
        Fails(Prose with { Changes = result, Legs = null, Pack = null, Suite = null, Projects = null }, $"The changes job ended '{result}'");

    [Theory]
    [InlineData("cancelled")]
    [InlineData("failure")]
    [InlineData("skipped")]
    public void A_test_matrix_that_did_not_succeed_fails(string result) =>
        Fails(Package with { Test = result }, $"The job 'test' was selected and ended '{result}'.");

    [Fact]
    public void A_selected_pack_that_was_skipped_fails() =>
        Fails(Package with { PackJob = "skipped" }, "The job 'pack' was selected and ended 'skipped'.");

    [Fact]
    public void A_cancelled_consumer_fails() =>
        Fails(Package with { Consumer = "cancelled" }, "The job 'consumer' was selected and ended 'cancelled'.");

    [Fact]
    public void An_unselected_job_that_ran_fails() =>
        Fails(Prose with { Consumer = "success" }, "The job 'consumer' was not selected and ended 'success', not skipped");

    [Theory]
    [InlineData("True")]
    [InlineData("yes")]
    [InlineData("")]
    public void A_pack_answer_the_selection_does_not_write_fails(string pack) =>
        Fails(Prose with { Pack = pack }, $"The changes job's pack output is '{pack}'");

    [Fact]
    public void A_missing_answer_fails() => Fails(Prose with { Pack = null }, "The changes job's pack output is missing");

    [Theory]
    [InlineData("""["ubuntu-latest","macos-latest"]""")]
    [InlineData("""["windows-latest","ubuntu-latest"]""")]
    [InlineData("""["ubuntu"]""")]
    [InlineData("ubuntu-latest")]
    [InlineData("[]")]
    public void A_matrix_the_selection_does_not_write_fails(string legs) =>
        Fails(Prose with { Legs = legs }, "The changes job's legs output is");

    [Fact]
    public void A_job_the_gate_does_not_know_fails() =>
        Fails(Prose, "The gate does not know the job 'docs'", needs => needs["docs"] = new { result = "success", outputs = new { } });

    [Fact]
    public void A_job_the_gate_does_not_need_fails() =>
        Fails(Prose, "The gate does not need the job 'consumer'", needs => needs.Remove("consumer"));

    [Fact]
    public void The_summary_tables_each_job()
    {
        var file = Path.Combine(_tool.Work, "needs.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["changes"] = new { result = "success", outputs = new { legs = LinuxOnly, pack = "false", suite = "prose", projects = "[]" } },
            ["test"] = new { result = "success" },
            ["pack"] = new { result = "skipped" },
            ["consumer"] = new { result = "skipped" },
        }));
        var summary = Path.Combine(_tool.Work, "summary.md");

        _tool.Run(arguments: ["gate", file, "--summary", summary]).Code.ShouldBe(0);

        File.ReadAllText(summary).ShouldContain("| pack | no | skipped |");
    }
}
