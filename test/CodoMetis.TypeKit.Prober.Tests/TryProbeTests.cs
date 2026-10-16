using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// `try` is the writer's loop for one probe: it builds once in the replay's worktree, runs that probe alone,
/// prints how each row ended and exits with the probe's code, and records nothing. The probes here are a fake
/// adapter: P runs C1's three rows (row 1 violates, and the run then exits 7) and Q runs C2's.
/// </summary>
public sealed class TryProbeTests : IDisposable
{
    private readonly Fixture fx = new();
    private readonly string cache;
    private readonly FakeAdapter adapter = new();

    public TryProbeTests()
    {
        cache = fx.Root + "-cache";
        fx.Sh("git", "config", "core.autocrlf", "false");
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["replay"] = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src") },
        }.ToJsonString() + "\n```\n");
        fx.Write("tools/probe/corpus/claims.json", """[{"id": "C1", "source": "README.md#a", "surfaces": ["s"]}, {"id": "C2", "source": "README.md#a", "surfaces": ["s"]}]""");
        fx.Write("tools/probe/corpus/rows/Q.json", """{"claim": "C2", "rows": [{"row": {"x": "1", "y": "1"}}]}""");
        fx.Write("tools/probe/corpus/harness/Q.cs", "// probe Q\n");
        Registry(new JsonObject { ["claim"] = "C1", ["filter"] = "*P" });
        fx.Rows([("1", "1"), ("2", "2"), ("1", "2")]);
        fx.Commit("two probes, C1 of three rows");
    }

    public void Dispose()
    {
        fx.Dispose();
        try { Directory.Delete(cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void Registry(JsonObject p) =>
        fx.Write("tools/probe/corpus/registry.json", new JsonObject
        {
            ["probes"] = new JsonObject { ["P"] = p, ["Q"] = new JsonObject { ["claim"] = "C2", ["filter"] = "*Q" } },
        }.ToJsonString());

    private (int Exit, string Out, string Err) Try(params string[] args) => TryEnding(Environment.NewLine, args);

    /// <summary>As <see cref="Try"/>, with the line end the output writers use, so a test sees Windows' output on any OS.</summary>
    private (int Exit, string Out, string Err) TryEnding(string newLine, params string[] args)
    {
        var output = new StringWriter { NewLine = newLine }; var error = new StringWriter { NewLine = newLine };
        var context = new Context(fx.Root, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, output, error);
        int exit;
        try { exit = TryProbe.Run(context, new Args(args, new HashSet<string>(StringComparer.Ordinal) { "no-build" }), adapter); }
        catch (Refusal refusal) { error.WriteLine(refusal.Message); exit = refusal.ExitCode; }
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>The output's lines, whatever line end the writer used.</summary>
    private static string[] Lines(string output) => output.ReplaceLineEndings("\n").Split('\n');

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void It_runs_the_one_probe_and_prints_how_each_row_ended(string newLine)
    {
        var (exit, output, error) = TryEnding(newLine, "--probe", "P");
        exit.ShouldBe(7, output + error);
        adapter.Builds.ShouldBe(1);
        adapter.Runs.ShouldHaveSingleItem().Filter.ShouldBe("*P");
        adapter.Runs[0].Rows.ShouldBeNull("every row, as the registry runs it");
        var lines = Lines(output);
        lines.ShouldContain(l => l.StartsWith("  P: Tests: 1 failed of 3 (exit 7, ", StringComparison.Ordinal));
        lines.ShouldContain("    #0 pass");
        lines.ShouldContain("    #1 violation: row 1 breaks the claim");
        lines.ShouldContain("    #2 pass");
        lines.ShouldContain("prober try: P: pass 2, violation 1; exit 7");
    }

    [Fact]
    public void A_probe_that_passes_exits_0()
    {
        adapter.Violates = null;
        var (exit, output, _) = Try("--probe", "P");
        exit.ShouldBe(0, output);
        output.ShouldContain("prober try: P: pass 3; exit 0");
    }

    [Fact]
    public void It_records_nothing_under_the_state_directory()
    {
        // A staged probe and a ledger with runs and evidence, as a round leaves them.
        fx.Write(".claude/agent-memory-local/prober/branches/main/staging/probes/R.cs", "// staged\n");
        fx.Write(".claude/agent-memory-local/prober/branches/main/runs.jsonl", """{"probe": "P", "result": "pass"}""" + "\n");
        var before = Hash(fx.Memory);
        Try("--probe", "P").Exit.ShouldBe(7);
        Try("--probe", "P", "--rows", "0").Exit.ShouldBe(0);
        Hash(fx.Memory).ShouldBe(before);
    }

    [Fact]
    public void Rows_runs_the_rows_given_one_stretch_at_a_time()
    {
        var (exit, output, _) = Try("--probe", "P", "--rows", "0,2");
        exit.ShouldBe(0, output);
        adapter.Runs.Select(r => r.Rows).ShouldBe(["0-0", "2-2"]);
        output.ShouldContain("#0 pass");
        output.ShouldContain("#2 pass");
        output.ShouldNotContain("#1 ");
        Try("--probe", "P", "--rows", "1,2").Exit.ShouldBe(7);
        adapter.Runs.Skip(2).Select(r => r.Rows).ShouldBe(["1-2"]);
    }

    [Fact]
    public void Rows_runs_each_row_in_the_variant_that_selects_it()
    {
        Registry(new JsonObject
        {
            ["claim"] = "C1", ["filter"] = "*P",
            ["variants"] = new JsonArray(new JsonObject { ["PROBE_ONLY"] = "x=1" }, new JsonObject { ["PROBE_ONLY"] = "x=2" }),
        });
        fx.Commit("variants");
        var (exit, output, _) = Try("--probe", "P", "--rows", "1,2");
        exit.ShouldBe(7, output);
        adapter.Runs.Select(r => (r.Only, r.Rows)).ShouldBe([("x=1", "2-2"), ("x=2", "1-1")]);
        output.ShouldContain("  P [PROBE_ONLY=x=1] rows 2-2: ");
    }

    [Fact]
    public void A_row_the_claim_does_not_have_is_refused_before_anything_runs()
    {
        var (exit, _, error) = Try("--probe", "P", "--rows", "3");
        exit.ShouldBe(2);
        error.ShouldContain("--rows 3: the claim has 3 row(s), 0 to 2");
        adapter.Builds.ShouldBe(0);
        adapter.Runs.ShouldBeEmpty();
    }

    [Fact]
    public void A_probe_not_in_the_registry_is_refused()
    {
        var (exit, _, error) = Try("--probe", "Nope");
        exit.ShouldBe(2);
        error.ShouldContain("no probe Nope in the registry");
    }

    [Fact]
    public void No_build_runs_without_building()
    {
        Try("--probe", "P", "--no-build").Exit.ShouldBe(2, "no worktree yet, so nothing was ever built");
        Try("--probe", "P").Exit.ShouldBe(7);
        Try("--probe", "P", "--no-build").Exit.ShouldBe(7);
        adapter.Builds.ShouldBe(1);
        adapter.Runs.Count.ShouldBe(2);
    }

    [Fact]
    public void A_failed_build_runs_nothing()
    {
        adapter.BuildFails = true;
        var (exit, output, _) = Try("--probe", "P");
        exit.ShouldBe(1);
        output.ShouldContain("Build: FAILED");
        adapter.Runs.ShouldBeEmpty();
    }

    [Fact]
    public void The_staging_is_overlaid_where_the_probe_runs()
    {
        fx.Write(".claude/agent-memory-local/prober/branches/main/staging/probes/R.cs", "// staged\n");
        Try("--probe", "P").Exit.ShouldBe(7);
        var worktree = Path.Combine(Directory.GetDirectories(cache).Single(), "wt");
        adapter.Worktrees.ShouldAllBe(w => w == worktree);
        File.ReadAllText(Path.Combine(worktree, "probes", "R.cs")).ShouldBe("// staged\n");
        File.Exists(Path.Combine(worktree, "probes", "P.cs")).ShouldBeTrue();
    }

    /// <summary>Every file and directory under <paramref name="dir"/>, by relative path and content.</summary>
    private static string Hash(string dir) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                     .Select(p => Path.GetRelativePath(dir, p) + ":" + (File.Exists(p) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) : "dir"))))));

    /// <summary>
    /// Runs the probe the filter names over its claim's rows in the worktree, as the harness does: PROBE_ONLY and
    /// PROBE_ROWS select, and each row it ran is one line of PROBE_OUT's jsonl. Row 1 of C1 violates.
    /// </summary>
    private sealed class FakeAdapter : IProbeAdapter
    {
        public int? Violates { get; set; } = 1;
        public bool BuildFails { get; set; }
        public int Builds { get; private set; }
        public List<(string Filter, string? Only, string? Rows)> Runs { get; } = [];
        public List<string> Worktrees { get; } = [];

        public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment)
        {
            Builds++;
            Worktrees.Add(worktree);
            return BuildFails ? new Ran(1, "Build: FAILED\nerror X", "") : new Ran(0, "Build: OK", "");
        }

        public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
        {
            Worktrees.Add(worktree);
            var only = environment.GetValueOrDefault("PROBE_ONLY");
            var range = environment.GetValueOrDefault("PROBE_ROWS");
            Runs.Add((filter, only, range));
            coverageFile.ShouldBeEmpty("try asks for no coverage");
            // The probe's own rows: P's are C1's, Q's C2's.
            var probe = filter.TrimStart('*');
            var rows = JsonNode.Parse(File.ReadAllText(Path.Combine(worktree, "probes", "rows", probe + ".json")))!["rows"]!.AsArray();
            var (lo, hi) = range is null ? (0, int.MaxValue) : (int.Parse(range.Split('-')[0]), int.Parse(range.Split('-')[1]));
            var lines = new List<string>(); var failed = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (i < lo || i > hi || !global::Prober.Replay.Selected(Files.StringMap(rows[i]!["row"]), only ?? "")) continue;
                var violates = probe == "P" && i == Violates;
                if (violates) failed++;
                lines.Add(new JsonObject
                {
                    ["idx"] = i, ["category"] = violates ? "violation" : "pass",
                    ["reasons"] = violates ? new JsonArray("violation:row 1 breaks the claim") : new JsonArray(), ["note"] = "compared",
                }.ToJsonString());
            }
            var outDir = environment["PROBE_OUT"]!;
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, probe + ".jsonl"), string.Join('\n', lines) + "\n");
            return failed > 0 ? new Ran(7, $"Tests: {failed} failed of {lines.Count}\n", "") : new Ran(0, $"Tests: {lines.Count} passed\n", "");
        }

        public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory) => (null, "none");

        public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory) => new Dictionary<string, string?>();
    }
}
