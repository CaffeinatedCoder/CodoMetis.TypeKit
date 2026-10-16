using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>
/// The replay runs probes in parallel, by family, with or without a footprint, and `merge` joins the states
/// of a replay split by family (decision 101). The probes here are a fake adapter: P covers C1 (surface s)
/// and Q covers C2 (surface t); a run writes its rows and, when asked for one, a coverage report that names
/// src/a.cs.
/// </summary>
public sealed class ReplayTests : IDisposable
{
    private readonly Fixture fx = new();
    private readonly string cache;
    private readonly FakeAdapter adapter = new();
    private readonly List<string> states = [];

    public ReplayTests()
    {
        cache = fx.Root + "-cache";
        fx.Sh("git", "config", "core.autocrlf", "false");
        Axes(new JsonObject { ["a"] = new JsonArray("s"), ["b"] = new JsonArray("t") });
        fx.Write("tools/probe/corpus/claims.json", """[{"id": "C1", "source": "README.md#a", "surfaces": ["s"]}, {"id": "C2", "source": "README.md#a", "surfaces": ["t"]}]""");
        fx.Write("tools/probe/corpus/registry.json", """{"probes": {"P": {"claim": "C1", "filter": "*P"}, "Q": {"claim": "C2", "filter": "*Q"}}}""");
        fx.Write("tools/probe/corpus/harness/Q.cs", "// probe Q\n");
        fx.Write("tools/probe/corpus/rows/C2.json", new JsonObject
        {
            ["claim"] = "C2", ["rows"] = new JsonArray(new JsonObject { ["row"] = new JsonObject { ["x"] = "1", ["y"] = "1" } }),
        }.ToJsonString());
        fx.Commit("two probes of two families");
    }

    public void Dispose()
    {
        fx.Dispose();
        foreach (var dir in states.Append(cache))
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The axes file with a replay configuration; its families are the given ones, or none.</summary>
    private void Axes(JsonObject? families)
    {
        var replay = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src") };
        if (families is not null) replay["families"] = families;
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject
            {
                ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") },
                ["t"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") },
            },
            ["replay"] = replay,
        }.ToJsonString() + "\n```\n");
    }

    /// <summary>A state directory of its own, as a leg of a matrix or the job that merges them has.</summary>
    private string State(string name)
    {
        var dir = fx.Root + "-" + name;
        states.Add(dir);
        return dir;
    }

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "all", "dry-run", "where", "no-footprint" };

    private (int Exit, string Out, string Err) Replay(string? state, params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var env = new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache };
        if (state is not null) env["PROBE_STATE_DIR"] = state;
        int exit;
        try { exit = global::Prober.Replay.Run(new Context(fx.Root, env, output, error), new Args(args, Flags), adapter); }
        catch (Refusal refusal) { error.WriteLine(refusal.Message); exit = refusal.ExitCode; }
        return (exit, output.ToString(), error.ToString());
    }

    private (int Exit, string Out, string Err) Ledger(string? state, params string[] args) =>
        state is null ? fx.Ledger(args) : fx.LedgerWith(new Dictionary<string, string?> { ["PROBE_STATE_DIR"] = state }, args);

    private string BranchDir(string? state) => Path.Combine(state ?? fx.Memory, "branches", "main");
    private bool HasEvidence(string? state, string probe) => File.Exists(Path.Combine(BranchDir(state), "evidence", probe + ".json"));
    private JsonObject Last(string? state) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(BranchDir(state), "replay-last.json")))!;
    private int Lines(string? state, string file) =>
        File.Exists(Path.Combine(BranchDir(state), file)) ? File.ReadAllLines(Path.Combine(BranchDir(state), file)).Count(l => l.Trim().Length > 0) : 0;

    [Fact]
    public void A_family_replays_the_probes_whose_claim_surface_it_holds()
    {
        var (exit, output, error) = Replay(null, "--all", "--family", "a");
        exit.ShouldBe(0, error + output);
        output.ShouldContain("1 of 1 probes to replay (family a)");
        HasEvidence(null, "P").ShouldBeTrue();
        HasEvidence(null, "Q").ShouldBeFalse("Q's claim is of surface t, family b");
        Files.Strings(Last(null)["selected"]).ShouldBe(["P"]);
        Files.Str(Last(null)["family"]).ShouldBe("a");

        Replay(null, "--all", "--family", "b").Exit.ShouldBe(0);
        HasEvidence(null, "Q").ShouldBeTrue();
    }

    [Fact]
    public void A_probe_whose_surface_is_in_no_family_refuses_the_replay()
    {
        Axes(new JsonObject { ["a"] = new JsonArray("s") });
        fx.Commit("family b dropped");
        var (exit, _, error) = Replay(null, "--all", "--family", "a");
        exit.ShouldBe(1, "a matrix by family would never run Q");
        error.ShouldContain("Q");
        error.ShouldContain("in no family");
        HasEvidence(null, "P").ShouldBeFalse("nothing ran");
    }

    [Fact]
    public void An_unknown_family_and_a_bad_parallel_are_usage_errors()
    {
        Replay(null, "--all", "--family", "c").Exit.ShouldBe(2);
        Replay(null, "--all", "--parallel", "0").Exit.ShouldBe(2);
        Replay(null, "--all", "--parallel", "two").Exit.ShouldBe(2);
        Axes(null);
        fx.Commit("no families");
        var (exit, _, error) = Replay(null, "--all", "--family", "a");
        exit.ShouldBe(2);
        error.ShouldContain("names no families");
    }

    [Fact]
    public void A_replay_without_a_footprint_records_evidence_and_leaves_the_probes_stale()
    {
        var (exit, output, error) = Replay(null, "--all", "--no-footprint");
        exit.ShouldBe(0, error + output);
        adapter.CoverageAsked.ShouldBeEmpty("no coverage report is asked for without a footprint");
        output.ShouldContain("footprint: not recorded (--no-footprint)");
        HasEvidence(null, "P").ShouldBeTrue();
        HasEvidence(null, "Q").ShouldBeTrue();
        Lines(null, "footprints.jsonl").ShouldBe(0);
        Last(null)["footprint"]!.GetValue<bool>().ShouldBeFalse();
        Ledger(null, "stale").Out.ShouldStartWith("2 of 2 probes to replay");

        Replay(null, "--all").Exit.ShouldBe(0);
        adapter.CoverageAsked.Count.ShouldBe(2);
        Lines(null, "footprints.jsonl").ShouldBe(2);
        Ledger(null, "stale").Out.ShouldStartWith("0 of 2 probes to replay");
    }

    [Fact]
    public void Probes_run_as_many_at_a_time_as_asked_and_are_recorded_in_registry_order()
    {
        var (exit, output, error) = Replay(null, "--all", "--parallel", "2");
        exit.ShouldBe(0, error + output);
        adapter.MostAtOnce.ShouldBe(2);
        output.IndexOf("  P:", StringComparison.Ordinal).ShouldBeLessThan(output.IndexOf("  Q:", StringComparison.Ordinal));
        Last(null)["parallel"]!.GetValue<int>().ShouldBe(2);

        adapter.MostAtOnce = 0;
        Replay(null, "--all", "--parallel", "1").Exit.ShouldBe(0);
        adapter.MostAtOnce.ShouldBe(1);
    }

    [Fact]
    public void Merge_joins_the_legs_and_refuses_a_probe_no_leg_saw()
    {
        var legA = State("leg-a"); var legB = State("leg-b"); var merged = State("merged");
        Replay(legA, "--all", "--family", "a").Exit.ShouldBe(0);
        Replay(legB, "--all", "--family", "b").Exit.ShouldBe(0);

        var (exit, output, error) = Ledger(merged, "merge", "--from", legA, legB);
        exit.ShouldBe(0, error + output);
        output.ShouldContain("2 leg(s)");
        HasEvidence(merged, "P").ShouldBeTrue();
        HasEvidence(merged, "Q").ShouldBeTrue();
        Lines(merged, "runs.jsonl").ShouldBe(2);
        Lines(merged, "footprints.jsonl").ShouldBe(2);
        Files.Strings(Last(merged)["selected"]).ShouldBe(["P", "Q"]);
        Files.Strings(Last(merged)["legs"]).ShouldBe(["a", "b"]);
        Ledger(merged, "stale").Out.ShouldStartWith("0 of 2 probes to replay");

        Ledger(merged, "merge", "--from", legA, legB).Exit.ShouldBe(0);
        Lines(merged, "runs.jsonl").ShouldBe(2, "a line a leg carried is added once");

        var (shortExit, _, shortError) = Ledger(State("short"), "merge", "--from", legA);
        shortExit.ShouldBe(1, "Q was in no leg");
        shortError.ShouldContain("Q");

        var (noneExit, _, noneError) = Ledger(State("none"), "merge", "--from", State("empty"));
        noneExit.ShouldBe(1);
        noneError.ShouldContain("no replay of main");
    }

    [Fact]
    public void Merge_fails_when_a_leg_failed()
    {
        adapter.Violating.Add("C1");
        var legA = State("leg-a"); var legB = State("leg-b"); var merged = State("merged");
        var (legExit, legOutput, _) = Replay(legA, "--all", "--family", "a");
        legExit.ShouldBe(1, "P violates");
        File.Exists(Path.Combine(BranchDir(legA), "logs", "P-0-host.log")).ShouldBeTrue("a failed run's full log is kept in the state");
        legOutput.ShouldContain("    log: ");
        Replay(legB, "--all", "--family", "b").Exit.ShouldBe(0);
        Directory.Exists(Path.Combine(BranchDir(legB), "logs")).ShouldBeFalse("a passing run keeps no log");

        var (exit, output, _) = Ledger(merged, "merge", "--from", legA, legB);
        exit.ShouldBe(1);
        output.ShouldContain("failed: P");
        Files.Strings(Last(merged)["failed"]).ShouldBe(["P"]);
        HasEvidence(merged, "Q").ShouldBeTrue("the merge still joined every leg");
        File.Exists(Path.Combine(BranchDir(merged), "logs", "P-0-host.log")).ShouldBeTrue("the merge carries the legs' logs");
    }

    /// <summary>P over C1's two rows and Q over C2's one; a run takes a moment, so runs at once can be counted.</summary>
    private sealed class FakeAdapter : IProbeAdapter
    {
        private int running;
        public int MostAtOnce;
        public List<string> CoverageAsked { get; } = [];
        public HashSet<string> Violating { get; } = new(StringComparer.Ordinal);

        public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment) => new(0, "Build: OK", "");

        public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
        {
            var now = Interlocked.Increment(ref running);
            lock (this) MostAtOnce = Math.Max(MostAtOnce, now);
            try
            {
                Thread.Sleep(300);
                var claim = filter.EndsWith('P') ? "C1" : "C2";
                var violates = Violating.Contains(claim);
                var outDir = environment["PROBE_OUT"]!;
                Directory.CreateDirectory(outDir);
                var lines = Enumerable.Range(0, claim == "C1" ? 2 : 1).Select(idx => new JsonObject
                {
                    ["idx"] = idx, ["category"] = violates ? "violation" : "pass", ["applied"] = new JsonObject { ["x"] = "1", ["y"] = "1" },
                    ["reasons"] = violates ? new JsonArray("violation:seeded") : new JsonArray(),
                }.ToJsonString());
                File.WriteAllText(Path.Combine(outDir, claim + ".jsonl"), string.Join('\n', lines) + "\n");
                if (violates)
                {
                    Directory.CreateDirectory(environment["PROBE_LOG_DIR"]!);
                    File.WriteAllText(Path.Combine(environment["PROBE_LOG_DIR"]!, "host.log"), "the host's full output");
                }
                if (coverageFile.Length > 0)
                {
                    lock (this) CoverageAsked.Add(coverageFile);
                    Directory.CreateDirectory(results);
                    File.WriteAllText(Path.Combine(results, coverageFile),
                        $"<coverage><sources><source>{worktree}</source></sources><packages><package name=\"p\"><classes>" +
                        "<class name=\"A\" filename=\"src/a.cs\"><lines><line number=\"1\" hits=\"1\"/></lines></class></classes></package></packages></coverage>");
                }
                return new Ran(violates ? 1 : 0, violates ? "TESTS FAILED" : "TESTS OK", "");
            }
            finally { Interlocked.Decrement(ref running); }
        }

        public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory) => (null, "none");

        public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory) => new Dictionary<string, string?>();
    }
}
