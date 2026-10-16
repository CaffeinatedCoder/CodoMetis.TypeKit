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

    /// <summary>A replay in another checkout of the repository, a linked worktree, with the fake adapter.</summary>
    private (int Exit, string Out, string Err) ReplayIn(string checkout, params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        int exit;
        try { exit = global::Prober.Replay.Run(new Context(checkout, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, output, error), new Args(args, Flags), adapter); }
        catch (Refusal refusal) { error.WriteLine(refusal.Message); exit = refusal.ExitCode; }
        return (exit, output.ToString(), error.ToString());
    }

    private string Home(string checkout) =>
        global::Prober.Replay.Home(new Context(checkout, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, TextWriter.Null, TextWriter.Null),
                                   new global::Prober.Ledger(new Context(checkout, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null));

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
    public void Probes_run_as_many_at_a_time_as_asked_without_a_footprint_and_are_recorded_in_registry_order()
    {
        var (exit, output, error) = Replay(null, "--all", "--no-footprint", "--parallel", "2");
        exit.ShouldBe(0, error + output);
        adapter.MostAtOnce.ShouldBe(2);
        output.IndexOf("  P:", StringComparison.Ordinal).ShouldBeLessThan(output.IndexOf("  Q:", StringComparison.Ordinal));
        Last(null)["parallel"]!.GetValue<int>().ShouldBe(2);

        adapter.MostAtOnce = 0;
        Replay(null, "--all", "--no-footprint", "--parallel", "1").Exit.ShouldBe(0);
        adapter.MostAtOnce.ShouldBe(1);
    }

    /// <summary>
    /// The coverage collector rewrites the assemblies on disk, so two hosts from one output directory corrupt
    /// each other (KB-025): a replay that records a footprint runs one host at a time, whatever --parallel says.
    /// </summary>
    [Fact]
    public void A_replay_with_a_footprint_runs_one_host_at_a_time()
    {
        var (exit, output, error) = Replay(null, "--all", "--parallel", "2");
        exit.ShouldBe(0, error + output);
        adapter.MostAtOnce.ShouldBe(1);
        output.ShouldContain("one at a time (the coverage collector rewrites the assemblies on disk)");
        Last(null)["parallel"]!.GetValue<int>().ShouldBe(1);
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

    [Fact]
    public void Two_checkouts_of_one_repository_get_scratch_worktrees_of_their_own()
    {
        var other = fx.Linked("other");

        var (main, linked) = (Home(fx.Root), Home(other));

        linked.ShouldNotBe(main);
        Path.GetFileName(linked).ShouldStartWith(Path.GetFileName(other) + "-");
        ReplayIn(other, "--where").Out.ShouldContain(Path.Combine(linked, "seeds"));
    }

    /// <summary>Removing a linked worktree leaves its cache behind; the line `--where` prints removes it, worktrees first.</summary>
    [Fact]
    public void The_remove_line_of_where_removes_the_cache_and_its_worktrees()
    {
        var other = fx.Linked("other");
        var home = Home(other);
        fx.Sh("git", "worktree", "add", "-q", "--detach", Path.Combine(home, "seeds"), "HEAD");
        var line = ReplayIn(other, "--where").Out.Split('\n').Single(l => l.StartsWith("remove:", StringComparison.Ordinal))["remove:".Length..].Trim();

        static string Slashed(string path) => path.Replace('\\', '/');
        Slashed(line).ShouldContain($"'{Slashed(home)}'");
        fx.Sh("sh", "-c", line);

        Directory.Exists(home).ShouldBeFalse();
        Slashed(fx.Sh("git", "worktree", "list", "--porcelain")).ShouldNotContain(Slashed(home));
    }

    /// <summary>The main checkout's cache is keyed as it was before linked worktrees had their own, so its warm copy is kept.</summary>
    [Fact]
    public void The_main_checkout_keeps_the_cache_it_had()
    {
        var root = Path.GetDirectoryName(fx.Sh("git", "rev-parse", "--path-format=absolute", "--git-common-dir").Trim().TrimEnd('/', '\\'))!;
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(root)))[..8];

        Home(fx.Root).ShouldBe(Path.Combine(cache, $"{Path.GetFileName(root)}-{key}"));
    }

    [Fact]
    public void A_round_on_one_branch_does_not_skip_a_replay_of_another()
    {
        var other = fx.Linked("other");
        fx.Ledger("lock", "--holder", "round-explore-1", "--mode", "explore").Exit.ShouldBe(0);

        var (exit, output, error) = ReplayIn(other, "--all");

        exit.ShouldBe(0, error + output);
        output.ShouldNotContain("skipped");
        File.Exists(Path.Combine(fx.Memory, "branches", "other", "evidence", "P.json")).ShouldBeTrue(output);
    }

    /// <summary>
    /// The lock is per branch and the replay worktree per checkout: a replay of another branch in this checkout (the
    /// branch switched while the post-commit replay runs) skips while one holds the worktree, and never shares it.
    /// </summary>
    [Fact]
    public void A_replay_skips_while_another_branchs_replay_in_its_checkout_holds_the_worktree()
    {
        using (global::Prober.Replay.HoldWorktree(Home(fx.Root)))
        {
            var (exit, output, error) = Replay(null, "--all", "--branch", "other");

            exit.ShouldBe(0, error);
            output.ShouldContain("skipped, another branch's replay in this checkout");
            File.Exists(Path.Combine(fx.Memory, "branches", "other", "evidence", "P.json")).ShouldBeFalse();
        }
        Replay(null, "--all", "--branch", "other").Out.ShouldNotContain("skipped");
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
