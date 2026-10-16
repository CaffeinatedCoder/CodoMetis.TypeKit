using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>Coverage is earned only by replayed evidence, bound to the identity it ran under (the review's priority 1).</summary>
public sealed class AccountingTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private static Dictionary<string, string> R(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };
    private static readonly IReadOnlyList<Dictionary<string, string>> TwoRows = [R("1", "1"), R("2", "2")];

    private void RecordPass(IReadOnlyList<Dictionary<string, string>> applied, string category = "pass", int[]? executed = null)
    {
        fx.Footprint();
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", fx.Evidence(applied, category, executed)).Exit.ShouldBe(0);
    }

    [Fact]
    public void A_pass_credits_the_pairs_of_the_rows_it_ran()
    {
        RecordPass(TwoRows);
        fx.Covered().ShouldBe(2);
    }

    [Fact]
    public void The_status_line_counts_what_the_json_counts()
    {
        RecordPass(TwoRows);
        fx.Ledger("status").Out.ShouldStartWith("main at ");
        fx.Ledger("status").Out.ShouldContain(": 2 of 4 pairs current (50.0%), 0 open finding(s)");
    }

    [Fact]
    public void An_old_pass_does_not_credit_a_row_added_since()
    {
        RecordPass(TwoRows);
        fx.Rows([("1", "1"), ("2", "2"), ("1", "2")]);
        fx.Commit("a new row");
        fx.Covered().ShouldBe(0, "the pass was for another corpus: nothing of it may count now");
    }

    [Fact]
    public void A_changed_harness_voids_the_pass()
    {
        RecordPass(TwoRows);
        fx.Write("tools/probe/corpus/harness/P.cs", "// probe P, changed\n");
        fx.Commit("harness");
        fx.Covered().ShouldBe(0);
    }

    [Fact]
    public void A_changed_axes_file_voids_the_pass()
    {
        RecordPass(TwoRows);
        var path = Path.Combine(fx.Root, ".claude/probe-axes.md");
        fx.Write(".claude/probe-axes.md", File.ReadAllText(path).Replace("\"watched\":[\"src\"]", "\"watched\":[\"src\",\"README.md\"]", StringComparison.Ordinal));
        fx.Commit("axes");
        fx.Covered().ShouldBe(0);
    }

    [Fact]
    public void Only_the_settings_that_applied_earn_credit()
    {
        RecordPass([R("1", "1"), R("1", "2")]);   // row 2 asked for x=2 and ran as x=1
        fx.Covered().ShouldBe(2, "x1y1 and the applied x1y2, never the requested x2y2");
        var rows = JsonNode.Parse(fx.Ledger("rows", "--claim", "C1").Out)!["rows"]!.AsArray();
        rows.SelectMany(r => r!["pairs"]!.AsArray()).Select(p => $"{p![0]} {p[1]}").ShouldContain("x=2 y=2");
    }

    [Fact]
    public void An_axis_the_harness_never_read_earns_nothing()
    {
        RecordPass([new(StringComparer.Ordinal) { ["x"] = "1" }, new(StringComparer.Ordinal) { ["x"] = "2" }]);
        fx.Covered().ShouldBe(0);
    }

    [Fact]
    public void A_refusal_is_no_evidence_of_success()
    {
        RecordPass(TwoRows, category: "expected-refusal");
        fx.Covered().ShouldBe(0);
    }

    [Fact]
    public void A_row_that_did_not_run_is_never_credited()
    {
        RecordPass(TwoRows, executed: [0]);
        fx.Covered().ShouldBe(1);
    }
}

/// <summary>
/// What earned nothing is counted where the coverage is read, and a row that steps around a finding binds: the
/// gate refuses while one does, and the ledger names a skip whose finding is fixed or dismissed.
/// </summary>
public sealed class OutcomeTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private static Dictionary<string, string> R(string x, string y) => new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };

    private string Record(params (string Category, string? Finding)[] outcomes)
    {
        fx.Footprint();
        var evidence = fx.Evidence([R("1", "1"), R("2", "2")], outcomes: outcomes);
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", evidence).Exit.ShouldBe(0);
        return evidence;
    }

    private List<string> StaleSkips(string evidence) =>
        new Ledger(new Context(fx.Root, new Dictionary<string, string?>(), TextWriter.Null, TextWriter.Null), null)
            .StaleSkips(JsonNode.Parse(File.ReadAllText(evidence)) as JsonObject);

    private void Finding(string status, string commit) =>
        File.AppendAllText(Path.Combine(fx.BranchDir, "findings.jsonl"),
            new JsonObject { ["id"] = "F9", ["claim"] = "C1", ["status"] = status, ["commit"] = commit }.ToJsonString() + "\n");

    [Fact]
    public void Status_counts_how_the_rows_of_a_claim_ended()
    {
        Record(("pass", null), ("expected-refusal", null));
        var outcomes = JsonNode.Parse(fx.Ledger("status", "--json").Out)!["claims"]!["C1"]!["outcomes"]!;
        outcomes["pass"]!.GetValue<double>().ShouldBe(1);
        outcomes["expected-refusal"]!.GetValue<double>().ShouldBe(1);
        fx.Ledger("status").Out.ShouldContain("C1: 1/4  (rows: expected-refusal 1, pass 1)");
    }

    [Fact]
    public void A_skip_for_a_finding_is_shown_and_refused_by_the_gate_without_any_ledger_of_findings()
    {
        fx.Sh("git", "tag", "v1");
        Record(("pass", null), ("known-finding", "F9"));
        fx.Ledger("status").Out.ShouldContain("1 row(s) skip for finding F9");
        var (exit, output, _) = fx.Ledger("gate", "--since", "v1", "--json");
        exit.ShouldNotBe(0);
        JsonNode.Parse(output)!["reasons"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldContain("P: 1 row(s) skip for finding F9; fix it and remove the skip, or dismiss it");
    }

    [Fact]
    public void A_skip_for_an_open_finding_stands()
    {
        var evidence = Record(("known-finding", "F9"), ("pass", null));
        Finding("open", "");
        StaleSkips(evidence).ShouldBeEmpty();
    }

    [Fact]
    public void A_skip_for_a_fixed_finding_is_stale()
    {
        var evidence = Record(("known-finding", "F9"), ("pass", null));
        var head = fx.Sh("git", "rev-parse", "HEAD").Trim();
        Finding("open", "");
        Finding("fixed", head);
        StaleSkips(evidence).ShouldBe([$"1 row(s) skip for F9, which is fixed at {head[..7]}: remove the skip"]);
    }

    [Fact]
    public void A_skip_for_a_dismissed_finding_is_stale()
    {
        var evidence = Record(("known-finding", "F9"), ("pass", null));
        Finding("open", "");
        fx.Write("tools/probe/corpus/dismissed.jsonl", new JsonObject { ["id"] = "F9", ["reason"] = "intended", ["date"] = "2026-10-15" }.ToJsonString() + "\n");
        StaleSkips(evidence).ShouldBe(["1 row(s) skip for F9, which is dismissed: remove the skip"]);
    }
}

/// <summary>One round at a time, on every OS: an exclusive create, liveness by heartbeat, a mutex for takeovers.</summary>
public sealed class LockTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    [Fact]
    public void A_second_holder_is_refused_while_the_first_is_alive()
    {
        fx.Ledger("lock", "--holder", "a", "--mode", "explore").Exit.ShouldBe(0);
        fx.Ledger("lock", "--holder", "b", "--mode", "replay").Exit.ShouldNotBe(0);
    }

    [Fact]
    public void A_holder_whose_heartbeat_stopped_is_taken_over()
    {
        fx.Ledger("lock", "--holder", "a", "--mode", "explore").Exit.ShouldBe(0);
        fx.AgeTheLock();
        fx.Ledger("lock", "--holder", "b", "--mode", "replay").Exit.ShouldBe(0);
    }

    [Fact]
    public void A_heartbeat_keeps_the_holder_alive()
    {
        fx.Ledger("lock", "--holder", "a", "--mode", "explore").Exit.ShouldBe(0);
        fx.AgeTheLock();
        fx.Ledger("heartbeat", "--holder", "a").Exit.ShouldBe(0);
        fx.Ledger("lock", "--holder", "b", "--mode", "replay").Exit.ShouldNotBe(0);
    }

    [Fact]
    public void Two_takers_at_once_get_one_lock() => RaceOf(8).ShouldBe(1);

    [Fact]
    public void Racing_takers_of_a_dead_holder_get_one_lock()
    {
        fx.Ledger("lock", "--holder", "dead", "--mode", "explore").Exit.ShouldBe(0);
        fx.AgeTheLock();
        RaceOf(8).ShouldBe(1);
    }

    [Fact]
    public void Racing_threads_over_a_dead_holder_get_one_lock()
    {
        // Threads released together by a barrier collide in the critical section far more tightly than
        // processes can; ten rounds, each must end with exactly one holder.
        for (var round = 0; round < 10; round++)
        {
            fx.Ledger("lock", "--holder", $"dead{round}", "--mode", "explore").Exit.ShouldBe(0);
            fx.AgeTheLock();
            const int takers = 16;
            using var barrier = new Barrier(takers);
            var exits = new int[takers];
            var threads = Enumerable.Range(0, takers).Select(i => new Thread(() =>
            {
                barrier.SignalAndWait();
                exits[i] = fx.Ledger("lock", "--holder", $"t{round}-{i}", "--mode", "replay").Exit;
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            exits.Count(e => e == 0).ShouldBe(1, $"round {round}");
            File.Delete(fx.LockFile());
        }
    }

    /// <summary>
    /// What crashed the test host on windows-latest (run 38732628888): a taker creating round.lock.mutex while
    /// its last holder's delete was still pending got UnauthorizedAccessException, which Windows throws there
    /// instead of IOException, and the mutex loop let it through. Here the OS refuses the create every time,
    /// the same exception on every platform: a directory in the mutex's place on Windows, a state directory
    /// without write permission elsewhere. A taker waits, then refuses; it never throws.
    /// </summary>
    [Fact]
    public void A_mutex_the_OS_refuses_to_create_is_waited_for_and_refused_not_thrown()
    {
        var directory = Path.GetDirectoryName(fx.LockFile())!;
        Directory.CreateDirectory(directory);
        var mutex = fx.LockFile() + ".mutex";
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(mutex);
        else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var (exit, _, error) = fx.Ledger("lock", "--holder", "a", "--mode", "explore");

            exit.ShouldNotBe(0);
            error.ShouldContain("mutex");
        }
        finally
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(mutex);
            else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void A_lock_of_one_branch_leaves_another_branchs_lock_free()
    {
        var other = fx.Linked("other");
        fx.Ledger("lock", "--holder", "a", "--mode", "explore").Exit.ShouldBe(0);

        var (exit, output, error) = fx.LedgerIn(other, "lock", "--holder", "b", "--mode", "explore");

        exit.ShouldBe(0, error + output);
        File.Exists(fx.LockFile("other")).ShouldBeTrue();
        File.Exists(fx.LockFile("main")).ShouldBeTrue("a's lock is still held");
    }

    [Fact]
    public void A_second_lock_of_one_branch_is_refused_from_another_checkout()
    {
        var other = fx.Linked("other");
        fx.Ledger("lock", "--holder", "a", "--mode", "explore").Exit.ShouldBe(0);

        fx.LedgerIn(other, "lock", "--branch", "main", "--holder", "b", "--mode", "replay").Exit.ShouldNotBe(0);
    }

    private int RaceOf(int takers)
    {
        var processes = Enumerable.Range(0, takers).Select(i => fx.Spawn("lock", "--holder", $"h{i}", "--mode", "replay")).ToList();
        foreach (var p in processes) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); }
        return processes.Count(p => p.ExitCode == 0);
    }
}

/// <summary>The release gate needs no model: every obligation is a probe or a reviewed waiver.</summary>
public sealed class GateTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    private void TouchTheClaim()
    {
        fx.Write("README.md", "# a\n\nThe claim, reworded.\n");
        fx.Commit("docs");
    }

    [Fact]
    public void An_unprobed_touched_claim_fails_with_what_is_missing()
    {
        fx.Sh("git", "tag", "v1");
        TouchTheClaim();
        var (exit, output, _) = fx.Ledger("gate", "--since", "v1", "--json");
        exit.ShouldNotBe(0);
        JsonNode.Parse(output)!["reasons"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldContain(r => r.StartsWith("C1:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reviewed_waiver_meets_the_obligation_without_a_probe()
    {
        fx.Sh("git", "tag", "v1");
        fx.Write("tools/probe/corpus/registry.json", """{"probes": {}}""");
        fx.Write("tools/probe/corpus/waivers.json", """[{"claim": "C1", "reason": "covered by the suite's C1Tests", "reviewed_by": "maintainer", "date": "2026-10-15"}]""");
        TouchTheClaim();
        var (exit, _, error) = fx.Ledger("gate", "--since", "v1");
        exit.ShouldBe(0, error);
    }

    [Fact]
    public void An_unsigned_waiver_meets_nothing()
    {
        fx.Sh("git", "tag", "v1");
        fx.Write("tools/probe/corpus/registry.json", """{"probes": {}}""");
        fx.Write("tools/probe/corpus/waivers.json", """[{"claim": "C1", "reason": "later"}]""");
        TouchTheClaim();
        fx.Ledger("gate", "--since", "v1").Exit.ShouldNotBe(0);
    }

    [Fact]
    public void State_goes_where_PROBE_STATE_DIR_says()
    {
        var state = Path.Combine(Path.GetTempPath(), "prober-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            fx.LedgerWith(new() { ["PROBE_STATE_DIR"] = state }, "lock", "--holder", "ci", "--mode", "replay").Exit.ShouldBe(0);
            Directory.EnumerateFiles(state, "round.lock", SearchOption.AllDirectories).ShouldNotBeEmpty();
            File.Exists(fx.LockFile()).ShouldBeFalse();
        }
        finally { if (Directory.Exists(state)) Directory.Delete(state, recursive: true); }
    }
}
