using System.Text.Json.Nodes;

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
            File.Delete(Path.Combine(fx.Memory, "round.lock"));
        }
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
            File.Exists(Path.Combine(state, "round.lock")).ShouldBeTrue();
            File.Exists(Path.Combine(fx.Memory, "round.lock")).ShouldBeFalse();
        }
        finally { if (Directory.Exists(state)) Directory.Delete(state, recursive: true); }
    }
}
