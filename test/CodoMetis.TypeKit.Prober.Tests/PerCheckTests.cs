using System.Text.Json.Nodes;
using Prober;
using Probes.Shapes;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>Runs rows through the probe corpus's own harness (harness/Row.cs, compiled in) and reads the lines it writes.</summary>
internal static class HarnessRun
{
    /// <summary>
    /// Runs one row of axes x and y through <c>Rec.Run</c>, writing to <paramref name="outDir"/>: the exception the
    /// test ends with (a failure or a skip), if any, and the line the harness wrote for the row.
    /// </summary>
    public static (Exception? Ended, JsonObject Line) Row(string outDir, int idx, Func<Row, Task<string?>> check, string x = "1", string y = "1")
    {
        var previous = Environment.GetEnvironmentVariable("PROBE_OUT");
        Environment.SetEnvironmentVariable("PROBE_OUT", outDir);
        Exception? ended = null;
        try { Rec.Run("C1", new Row(idx, new Dictionary<string, string> { ["x"] = x, ["y"] = y }), check).GetAwaiter().GetResult(); }
        catch (Exception e) { ended = e; }
        finally { Environment.SetEnvironmentVariable("PROBE_OUT", previous); }
        var line = File.ReadAllLines(Path.Combine(outDir, "C1.jsonl")).Select(l => JsonNode.Parse(l)!.AsObject()).Last(l => (int)Files.Num(l["idx"]) == idx);
        return (ended, line);
    }

    public static JsonObject Check(JsonObject line, string obligation) =>
        line["checks"]!.AsArray().OfType<JsonObject>().Single(c => Files.Str(c["obligation"]) == obligation);
}

/// <summary>
/// A check of a row (<c>Row.Check</c>) has one obligation, one baseline, and reads, notes and an outcome of its own: a
/// failure it returns is a violation, an exception a harness error that the other checks of the row survive, and a
/// pass with no baseline a harness error. The row passes when no check failed and one passed, and is skipped, each
/// check's outcome listed, when none passed.
/// </summary>
[Collection(nameof(ProbeOut))]
public sealed class CheckHarnessTests : IDisposable
{
    private readonly string outDir = Path.Combine(Path.GetTempPath(), "prober-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(outDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_failure_a_check_returns_is_its_violation_with_its_own_reads()
    {
        string? returned = "not run";
        var (ended, line) = HarnessRun.Row(outDir, 0, async r =>
        {
            _ = r["x"];
            returned = await r.Check("keys", Baseline.Twin, c => Task.FromResult<string?>(c["y"] == "1" ? "the key differs" : null));
            return returned;
        });
        returned.ShouldBe("the key differs", "Check returns the failure, so a probe can stop the row at it");
        ended.ShouldNotBeNull().Message.ShouldContain("keys violation: the key differs");
        Files.Str(line["category"]).ShouldBe("violation");
        Files.StringMap(line["applied"]).ShouldBe(new Dictionary<string, string> { ["x"] = "1" }, "the row's own reads are those outside any check");
        Files.Strings(line["reasons"]).ShouldBeEmpty("the failure is the check's, not the row's own");
        var check = HarnessRun.Check(line, "keys");
        Files.Str(check["baseline"]).ShouldBe("twin");
        Files.Str(check["category"]).ShouldBe("violation");
        Files.StringMap(check["applied"]).ShouldBe(new Dictionary<string, string> { ["y"] = "1" });
        Files.Strings(check["reasons"]).ShouldBe(["violation:the key differs"]);
    }

    [Fact]
    public void An_exception_in_a_check_is_a_harness_error_the_other_checks_survive()
    {
        string? returned = "not run";
        var (ended, line) = HarnessRun.Row(outDir, 0, async r =>
        {
            returned = await r.Check("keys", Baseline.Twin, (Func<Check, Task<string?>>)(_ => throw new InvalidOperationException("the check broke")));
            return r.Check("nulls", Baseline.RoundTrip, c => c.Is("x", "1") ? null : "never");
        });
        returned.ShouldBeNull("an exception never reads as a failure, so a probe returning it makes no violation");
        ended.ShouldNotBeNull().Message.ShouldContain("keys harness-error: UNEXPECTED InvalidOperationException: the check broke");
        Files.Str(line["category"]).ShouldBe("harness-error");
        Files.Str(HarnessRun.Check(line, "keys")["category"]).ShouldBe("harness-error");
        Files.Str(HarnessRun.Check(line, "nulls")["category"]).ShouldBe("pass", "the next check of the row still ran");
    }

    [Fact]
    public void The_synchronous_form_keeps_an_exception_from_the_row_too()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, r =>
        {
            var returned = r.Check("keys", Baseline.Twin, (Func<Check, string?>)(_ => throw new FormatException("bad")));
            return Task.FromResult(returned);
        });
        ended.ShouldNotBeNull().Message.ShouldContain("keys harness-error: UNEXPECTED FormatException: bad");
        Files.Strings(line["reasons"]).ShouldBeEmpty("the exception stayed in the check");
    }

    [Fact]
    public void A_pass_without_a_baseline_is_a_harness_error()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, r => Task.FromResult(r.Check("keys", null, c => c.Is("x", "1") ? null : "differs")));
        ended.ShouldNotBeNull().Message.ShouldContain("keys harness-error: pass-without-baseline");
        Files.Str(HarnessRun.Check(line, "keys")["category"]).ShouldBe("harness-error");
        HarnessRun.Check(line, "keys")["baseline"].ShouldBeNull();
    }

    [Fact]
    public void A_row_with_a_passing_check_and_one_that_did_not_apply_passes()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, r =>
        {
            r.Check("keys", Baseline.Twin, c => c.Is("x", "1") ? null : "differs");
            r.Check("nulls", Baseline.Twin, c => c.Inapplicable(Cite.NotPromised("closed-set-has-no-null"), "x=1"));
            return Task.FromResult<string?>(null);
        });
        ended.ShouldBeNull("one check compared and passed; the other did not apply, which voids nothing");
        Files.Str(line["status"]).ShouldBe("pass");
        Files.Str(line["category"]).ShouldBe("inapplicable", "the row's category is the highest of its checks'");
        Files.Strings(HarnessRun.Check(line, "nulls")["reasons"]).ShouldBe(["inapplicable:not-promised:closed-set-has-no-null(x=1)"]);
        Files.Str(HarnessRun.Check(line, "nulls")["notes"]).ShouldBe("not-promised:closed-set-has-no-null(x=1)");
    }

    [Fact]
    public void A_row_where_no_check_passed_is_skipped_listing_each_check()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, r =>
        {
            r.Check("smallest-scale", Baseline.Twin, c => c.Inapplicable(Cite.NotPromised("closed-set-has-no-scale")));
            r.Check("number-is-a-number", Baseline.Fact("KB-031"), c => c.Refused(Cite.Fact("KB-031")));
            r.Check("reads-a-number", Baseline.Model, c => c.Inconclusive("both threw, not alike"));
            r.Check("covers", Baseline.Docs("docs/json.md#decimals-in-one-spelling"), c => c.Refused(Cite.Docs("docs/json.md#decimals-in-one-spelling")));
            r.Check("keeps", Baseline.Cost, c => c.Inapplicable(Cite.Record("0094")));
            return Task.FromResult<string?>(null);
        });
        ended.ShouldNotBeNull().GetType().Name.ShouldBe("SkipException");
        ended.Message.ShouldContain("smallest-scale inapplicable: not-promised:closed-set-has-no-scale; number-is-a-number expected-refusal: fact:KB-031; " +
                                    "reads-a-number inconclusive: both threw, not alike; covers expected-refusal: docs:docs/json.md#decimals-in-one-spelling; " +
                                    "keeps inapplicable: record:0094");
        Files.Str(line["status"]).ShouldBe("fail");
        Files.Str(line["category"]).ShouldBe("inconclusive");
        line["checks"]!.AsArray().Select(c => Files.Str(c!["baseline"])).ShouldBe(["twin", "fact:KB-031", "model", "docs:docs/json.md#decimals-in-one-spelling", "cost"]);
    }

    [Fact]
    public void A_violation_fails_the_row_even_beside_a_passing_check()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, r =>
        {
            r.Check("keys", Baseline.Twin, _ => (string?)null);
            r.Check("nulls", Baseline.Twin, _ => "a null was written");
            return Task.FromResult<string?>(null);
        });
        ended.ShouldNotBeNull().GetType().Name.ShouldNotBe("SkipException");
        ended.Message.ShouldContain("nulls violation: a null was written");
        ended.Message.ShouldNotContain("keys pass");
        Files.Str(line["status"]).ShouldBe("fail");
    }

    [Fact]
    public void A_row_that_runs_no_check_is_written_as_before()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, r => Task.FromResult<string?>(r["x"] == "1" ? null : "differs"));
        ended.ShouldBeNull();
        line.ContainsKey("checks").ShouldBeFalse();
        line.Select(kv => kv.Key).ShouldBe(["idx", "status", "category", "completed", "applied", "reasons", "findings", "note"]);
    }

    [Fact]
    public void Reads_notes_and_substitutions_through_the_row_inside_a_check_are_the_checks()
    {
        var (ended, line) = HarnessRun.Row(outDir, 0, async r =>
        {
            await r.Check("keys", Baseline.Twin, async c =>
            {
                await Task.Yield();
                _ = r["y"];                                            // a helper handed the row, inside the check
                _ = r.Get("x");
                r.Substituted("x", "2", "the options ran as x=2");
                r.Note(NoteKeys.Compared, "write");
                return null;
            });
            return null;
        });
        ended.ShouldBeNull();
        Files.StringMap(line["applied"]).ShouldBeEmpty("the row read nothing outside the check");
        Files.Str(line["note"]).ShouldBeEmpty();
        var check = HarnessRun.Check(line, "keys");
        Files.StringMap(check["applied"]).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}").ShouldBe(["x=2", "y=1"]);
        Files.Str(check["notes"]).ShouldBe("substituted(x=2, the options ran as x=2);compared(write)");
    }

    [Fact]
    public void A_malformed_citation_or_baseline_is_refused_at_construction()
    {
        Should.Throw<ArgumentException>(() => Cite.Fact("KB-31"));
        Should.Throw<ArgumentException>(() => Cite.Record("94"));
        Should.Throw<ArgumentException>(() => Cite.NotPromised("Closed set"));
        Should.Throw<ArgumentException>(() => Cite.Docs("README.md#What TypeKit guarantees"));
        Should.Throw<ArgumentException>(() => Cite.Docs("../outside.md"));
        Should.Throw<ArgumentException>(() => Baseline.Fact("fact:KB-031"));
        Should.Throw<ArgumentException>(() => Baseline.Docs("#anchor-alone"));
        new[] { Baseline.Twin, Baseline.Framework, Baseline.RoundTrip, Baseline.Model, Baseline.Refusal, Baseline.Cost, Baseline.Fact("KB-031"), Baseline.Docs("README.md") }
            .Select(b => b.Spelling).ShouldBe(["twin", "framework", "round-trip", "model", "refusal", "cost", "fact:KB-031", "docs:README.md"]);
        new[] { Cite.NotPromised("a-b"), Cite.Docs("CHANGELOG.md#2.1.0-fixed"), Cite.Fact("KB-001"), Cite.Record("0094") }
            .Select(c => c.Spelling).ShouldBe(["not-promised:a-b", "docs:CHANGELOG.md#2.1.0-fixed", "fact:KB-001", "record:0094"]);

        var (ended, line) = HarnessRun.Row(outDir, 0, r => Task.FromResult(r.Check("keys", Baseline.Twin, c => c.Refused(Cite.Fact("KB-31")))));
        ended.ShouldNotBeNull().Message.ShouldContain("keys harness-error: UNEXPECTED ArgumentException:");
        Files.Str(HarnessRun.Check(line, "keys")["category"]).ShouldBe("harness-error");
    }
}

/// <summary>
/// The ledger credits checks, not rows: a passing check earns the pairs of the settings it ran under (the row's, its
/// own laid over them) whatever another check of the row ended as, never a pair put together from two checks' reads,
/// and `status` says per obligation what current checks earned and which no passing check named. The evidence is the
/// harness's own lines, copied as the replay copies them.
/// </summary>
[Collection(nameof(ProbeOut))]
public sealed class CheckLedgerTests : IDisposable
{
    private readonly Fixture fx = new();
    public void Dispose() => fx.Dispose();

    /// <summary>Runs P's two rows (x=1 y=1, x=2 y=2) through the harness and records the run as the replay would.</summary>
    private JsonObject Record(Func<Row, Task<string?>> check)
    {
        var outDir = Path.Combine(fx.Root, "rows-out");
        var rows = new JsonObject();
        foreach (var (idx, v) in new[] { (0, "1"), (1, "2") })
            rows[idx.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Replay.EvidenceRow(HarnessRun.Row(outDir, idx, check, v, v).Line);
        var path = Path.Combine(fx.Root, "evidence.json");
        File.WriteAllText(path, new JsonObject
        {
            ["variants"] = new JsonArray(new JsonObject { ["env"] = new JsonObject(), ["expected"] = new JsonArray(0, 1), ["executed"] = new JsonArray(0, 1) }),
            ["rows"] = rows,
        }.ToJsonString());
        fx.Footprint();
        fx.Ledger("record-run", "--probe", "P", "--result", "pass", "--evidence", path).Exit.ShouldBe(0);
        return JsonNode.Parse(File.ReadAllText(Path.Combine(fx.BranchDir, "evidence", "P.json")))!.AsObject();
    }

    private void Obligations()
    {
        fx.Write("README.md", "# a\n\nThe claim.\n\nA key is written as the wrapped type writes it.\n\nA null is refused.\n");
        fx.Write("tools/probe/corpus/claims.json", """
            [{"id": "C1", "source": "README.md#a", "surfaces": ["s"], "obligations": [
              {"id": "keys", "sentence": "A key is written as the wrapped type writes it."},
              {"id": "nulls", "sentence": "A null is refused."}]}]
            """);
        fx.Commit("obligations");
        fx.Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
    }

    private JsonObject Status()
    {
        var (exit, output, error) = fx.Ledger("status", "--json");
        exit.ShouldBe(0, error);
        return JsonNode.Parse(output)!["claims"]!["C1"]!.AsObject();
    }

    [Fact]
    public void A_passing_check_earns_its_pairs_beside_a_check_that_did_not_apply()
    {
        var evidence = Record(r =>
        {
            r.Check("keys", Baseline.Twin, c => c["x"] == c["y"] ? null : "differs");
            r.Check("nulls", Baseline.Twin, c => c.Inapplicable(Cite.NotPromised("no-null-here")));
            return Task.FromResult<string?>(null);
        });
        Files.Num(evidence["schemaVersion"]).ShouldBe(2);
        fx.Covered().ShouldBe(2, "x1y1 and x2y2, each read by the passing check, though every row ended inapplicable");
    }

    [Fact]
    public void A_pair_is_never_put_together_from_two_checks_reads()
    {
        Record(r =>
        {
            r.Check("keys", Baseline.Twin, c => c["x"] is not null ? null : "never");
            if (r.Index == 0) r.Check("nulls", Baseline.Twin, c => c["y"] is not null ? null : "never");
            else r.Check("nulls", Baseline.Twin, c => c["x"] is not null && c["y"] is not null ? null : "never");
            return Task.FromResult<string?>(null);
        });
        fx.Covered().ShouldBe(1, "row 0's checks read x and y apart, which credits no pair; row 1's second check read both");
    }

    [Fact]
    public void The_rows_own_reads_are_what_every_check_ran_under()
    {
        Record(r =>
        {
            _ = r["x"];
            r.Check("keys", Baseline.Twin, c => c["y"] is not null ? null : "never");
            return Task.FromResult<string?>(null);
        });
        fx.Covered().ShouldBe(2);
    }

    [Fact]
    public void A_row_without_checks_is_one_check_of_the_claim()
    {
        Record(r => Task.FromResult<string?>(r["x"] == r["y"] ? null : "differs"));
        fx.Covered().ShouldBe(2);
        var obligation = Status()["obligations"]!.AsArray().ShouldHaveSingleItem()!;
        Files.Str(obligation["id"]).ShouldBe("claim");
        Files.Num(obligation["current"]).ShouldBe(2);
    }

    [Fact]
    public void Status_gives_each_obligation_its_current_pairs_and_counts_those_no_passing_check_named()
    {
        Obligations();
        Record(r =>
        {
            r.Check("keys", Baseline.Twin, c => c["x"] == c["y"] ? null : "differs");
            r.Check("nulls", Baseline.Twin, c => c.Inapplicable(Cite.NotPromised("no-null-here")));
            return Task.FromResult<string?>(null);
        });
        var claim = Status();
        claim["obligations"]!.AsArray().Select(o => $"{Files.Str(o!["id"])} {Files.Num(o["current"])} {o["passed"]!.GetValue<bool>()}").ShouldBe(["keys 2 True", "nulls 0 False"]);
        claim["check_outcomes"]!.AsObject().Select(kv => $"{kv.Key} {Files.Num(kv.Value)}").ShouldBe(["inapplicable 2", "pass 2"]);
        claim["outcomes"]!.AsObject().Select(kv => $"{kv.Key} {Files.Num(kv.Value)}").ShouldBe(["inapplicable 2"]);
        fx.Ledger("status").Out.ReplaceLineEndings("\n")
          .ShouldContain("\n  C1: 2/4  (rows: inapplicable 2; checks: inapplicable 2, pass 2)  obligations 2 (2 without a seed: keys, nulls), 1 uncredited\n");
    }

    [Fact]
    public void The_replay_keeps_each_checks_fields_and_six_reasons()
    {
        var line = JsonNode.Parse("""
            {"idx": 0, "category": "inapplicable", "applied": {"x": "1"}, "reasons": [], "findings": [], "note": "",
             "checks": [{"obligation": "keys", "baseline": "twin", "category": "inapplicable", "applied": {"y": "1"},
                         "reasons": ["inapplicable:1", "inapplicable:2", "inapplicable:3", "inapplicable:4", "inapplicable:5", "inapplicable:6", "inapplicable:7"], "notes": "n"},
                        {"obligation": "nulls", "baseline": null, "category": "harness-error", "applied": {}, "reasons": [], "notes": ""}]}
            """)!.AsObject();
        var row = Replay.EvidenceRow(line);
        var checks = row["checks"]!.AsArray();
        checks.Count.ShouldBe(2);
        Files.Str(checks[0]!["obligation"]).ShouldBe("keys");
        Files.Str(checks[0]!["baseline"]).ShouldBe("twin");
        Files.StringMap(checks[0]!["applied"]).ShouldBe(new Dictionary<string, string> { ["y"] = "1" });
        Files.Strings(checks[0]!["reasons"]).Count().ShouldBe(6);
        Files.Str(checks[0]!["notes"]).ShouldBe("n");
        checks[1]!["baseline"].ShouldBeNull();
        Replay.EvidenceRow(JsonNode.Parse("""{"idx": 0, "category": "pass", "applied": {}}""")!.AsObject()).ContainsKey("checks").ShouldBeFalse();
    }
}

/// <summary>
/// `seeds` judges a seed by the checks it fires: a seed naming obligation O is caught only by a clean violation of a
/// check naming O, in a row whose unseeded run had no violation of O; a seed naming none by any check. P's rows run a
/// check of each obligation through the real harness; SEED names the check the seeded build breaks.
/// </summary>
[Collection(nameof(ProbeOut))]
public sealed class CheckSeedsTests : IDisposable
{
    private readonly Fixture fx = new();
    private readonly string cache;

    public CheckSeedsTests()
    {
        cache = fx.Root + "-cache";
        fx.Sh("git", "config", "core.autocrlf", "false");
        fx.Write("README.md", "# a\n\nThe claim.\n\nA key is written as the wrapped type writes it.\n\nA null is refused.\n");
        fx.Write("tools/probe/corpus/claims.json", """
            [{"id": "C1", "source": "README.md#a", "surfaces": ["s"], "obligations": [
              {"id": "keys", "sentence": "A key is written as the wrapped type writes it."},
              {"id": "nulls", "sentence": "A null is refused."}]}]
            """);
        fx.Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
            ["replay"] = new JsonObject { ["probeDir"] = "probes", ["include"] = new JsonArray("src") },
        }.ToJsonString() + "\n```\n");
        fx.Commit("obligations and the replay configuration");
    }

    public void Dispose()
    {
        fx.Dispose();
        try { Directory.Delete(cache, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Registers P with one seed per (obligation, SEED value): the seed names the obligation, and its build breaks the check SEED names.</summary>
    private void Register(params (string? Obligation, string Breaks)[] seeds)
    {
        fx.Write("tools/probe/corpus/registry.json", new JsonObject
        {
            ["probes"] = new JsonObject
            {
                ["P"] = new JsonObject
                {
                    ["claim"] = "C1", ["filter"] = "*P",
                    ["seeds"] = new JsonArray([.. seeds.Select(s => (JsonNode)new JsonObject
                    {
                        ["obligation"] = s.Obligation, ["env"] = new JsonObject { ["SEED"] = s.Breaks }, ["defect"] = $"{s.Breaks} breaks.",
                    })]),
                },
            },
        }.ToJsonString());
        fx.Commit("register");
    }

    private (int Exit, string Out) Seeds(HarnessAdapter adapter)
    {
        var output = new StringWriter();
        var context = new Context(fx.Root, new Dictionary<string, string?> { ["PROBE_CACHE_DIR"] = cache }, output, output);
        int exit;
        try { exit = global::Prober.Seeds.Run(context, new Args([], new HashSet<string>(StringComparer.Ordinal)), adapter); }
        catch (Refusal refusal) { output.WriteLine(refusal.Message); exit = refusal.ExitCode; }
        return (exit, output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>Row 1 runs a check of keys and one of nulls; the one SEED names fails. <paramref name="unseeded"/> fails a check of row 1 without a seed.</summary>
    private static Func<Row, string?, Task<string?>> TwoChecks(string? unseeded = null) => (r, seed) =>
    {
        foreach (var obligation in new[] { "keys", "nulls" })
            r.Check(obligation, Baseline.Twin, _ => r.Index == 1 && (seed == obligation || unseeded == obligation) ? $"{obligation} broke" : null);
        return Task.FromResult<string?>(null);
    };

    [Fact]
    public void A_seed_naming_an_obligation_is_caught_only_by_a_check_naming_it()
    {
        Register(("keys", "nulls"), ("nulls", "nulls"));
        var (exit, output) = Seeds(new HarnessAdapter(TwoChecks()));
        exit.ShouldBe(1, output);
        output.ShouldContain("\n  P/keys (C1): missed, no row violates that did not already (2 rows: pass 1, violation 1; 1 violate by a check naming nulls, not keys;");
        output.ShouldContain("\n  P/nulls (C1): caught, 1 of 2 rows violate; e.g. #1 nulls: violation:nulls broke\n");
    }

    [Fact]
    public void The_baseline_is_per_row_and_obligation()
    {
        Register(("keys", "keys"));
        var (exit, output) = Seeds(new HarnessAdapter(TwoChecks(unseeded: "nulls")));
        exit.ShouldBe(0, output);
        output.ShouldContain("\n  P/keys (C1): caught, 1 of 2 rows violate; e.g. #1 keys: violation:keys broke\n",
                             Case.Sensitive, "row 1 violated nulls unseeded, which takes nothing from a violation of keys there");
    }

    [Fact]
    public void A_seed_naming_none_is_caught_by_any_check()
    {
        Register((null, "nulls"));
        var (exit, output) = Seeds(new HarnessAdapter(TwoChecks()));
        exit.ShouldBe(0, output);
        output.ShouldContain("\n  P (C1): caught, 1 of 2 rows violate; e.g. #1 nulls: violation:nulls broke\n");
    }

    [Fact]
    public void A_harness_error_of_the_row_outside_its_checks_makes_no_check_a_catch()
    {
        Register(("keys", "keys"));
        var (exit, output) = Seeds(new HarnessAdapter((r, seed) =>
        {
            if (seed is not null && r.Index == 1) r.Outcome("harness-error", "the host did not start");
            return TwoChecks()(r, seed);
        }));
        exit.ShouldBe(1, output);
        output.ShouldContain("\n  P/keys (C1): missed, no row violates that did not already (2 rows: pass 1, violation 1; 1 ended in a harness error, which is no catch;");
    }

    [Fact]
    public void A_row_without_checks_catches_no_seed_naming_an_obligation()
    {
        Register(("keys", "keys"));
        var (exit, output) = Seeds(new HarnessAdapter((r, seed) => Task.FromResult<string?>(seed is not null && r.Index == 1 ? "row 1 broke" : null)));
        exit.ShouldBe(1, output);
        output.ShouldContain("1 violate by a check naming claim, not keys;");
    }

    /// <summary>The probe P over two rows, each run through the real harness with the value of SEED (null unseeded).</summary>
    private sealed class HarnessAdapter(Func<Row, string?, Task<string?>> check) : IProbeAdapter
    {
        public Ran Build(string worktree, IReadOnlyDictionary<string, string?> environment) => new(0, "Build: OK", "");

        public Ran RunProbe(string worktree, string filter, string results, string coverageFile, IReadOnlyDictionary<string, string?> environment)
        {
            var seed = environment.GetValueOrDefault("SEED");
            var outDir = environment["PROBE_OUT"]!;
            var failed = false;
            for (var idx = 0; idx < 2; idx++)
                failed |= HarnessRun.Row(outDir, idx, r => check(r, seed)).Ended is not null;
            return new Ran(failed ? 1 : 0, "", "");
        }

        public (JsonObject? Axes, string? Error) DeriveAxes(string checkout, string workDirectory) => (null, "none");

        public IReadOnlyDictionary<string, string?> Environment(string cacheDirectory) => new Dictionary<string, string?>();
    }
}
