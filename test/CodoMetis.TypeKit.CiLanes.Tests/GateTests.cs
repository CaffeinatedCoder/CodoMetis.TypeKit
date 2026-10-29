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
    private const string EveryLeg = """["ubuntu-latest","windows-latest","macos-latest"]""";

    private readonly ToolSandbox _tool = new("CiLanes", "ci-lanes");

    public void Dispose() => _tool.Dispose();

    private sealed record Run(
        string Changes = "success",
        string? Legs = LinuxOnly,
        string? Pack = "false",
        string Test = "success",
        string PackJob = "skipped",
        string Consumer = "skipped");

    private static readonly Run Prose = new();

    private static readonly Run Package = new(Legs: EveryLeg, Pack: "true", PackJob: "success", Consumer: "success");

    private (int Code, string Output) Judge(Run run, Action<Dictionary<string, object>>? edit = null)
    {
        var outputs = new Dictionary<string, string>();
        if (run.Legs is not null) outputs["legs"] = run.Legs;
        if (run.Pack is not null) outputs["pack"] = run.Pack;

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
    public void A_package_run_passes_with_every_job_run() => Passes(Package);

    [Fact]
    public void A_test_run_passes_with_every_leg_and_no_pack() => Passes(Prose with { Legs = EveryLeg });

    [Theory]
    [InlineData("failure")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    public void A_selection_that_did_not_succeed_fails(string result) =>
        Fails(Prose with { Changes = result, Legs = null, Pack = null }, $"The changes job ended '{result}'");

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
    [InlineData("""["ubuntu-latest","windows-latest"]""")]
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
            ["changes"] = new { result = "success", outputs = new { legs = LinuxOnly, pack = "false" } },
            ["test"] = new { result = "success" },
            ["pack"] = new { result = "skipped" },
            ["consumer"] = new { result = "skipped" },
        }));
        var summary = Path.Combine(_tool.Work, "summary.md");

        _tool.Run(arguments: ["gate", file, "--summary", summary]).Code.ShouldBe(0);

        File.ReadAllText(summary).ShouldContain("| pack | no | skipped |");
    }
}
